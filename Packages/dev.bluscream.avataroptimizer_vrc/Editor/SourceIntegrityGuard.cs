using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Verifies that a conversion left the source avatar and its assets alone.
    ///
    /// "Non-destructive" is a claim the pipeline makes in a dozen places, and every pass that clones an
    /// asset has a failure mode where it edits the original instead — writing to a shared mesh, a shared
    /// material, or a clip that was never cloned. Those mistakes are invisible at the time and only surface
    /// later, when the *source* avatar turns out to have been damaged by optimizing it.
    ///
    /// This takes a fingerprint of every asset the source avatar references, plus the shape of its
    /// hierarchy, and re-checks both after the run. Anything that changed without being declared as an
    /// intentional edit is reported as an error.
    /// </summary>
    public static class SourceIntegrityGuard
    {
        private const string Tag = "SourceIntegrityGuard";

        /// <summary>Files at or below this size are hashed; larger ones fall back to size + write time.</summary>
        private const long FullHashSizeLimit = 64L * 1024 * 1024;

        public sealed class Snapshot
        {
            public string AvatarName;
            /// <summary>Asset path → fingerprint of the file and of its .meta (importer settings).</summary>
            public Dictionary<string, string> AssetFingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
            /// <summary>Transform path → the components on it, so added/removed components are caught.</summary>
            public Dictionary<string, string> HierarchySignature = new Dictionary<string, string>(StringComparer.Ordinal);
            public int TransformCount;
        }

        /// <summary>
        /// Fingerprints the source avatar and everything it references. Call before the conversion starts.
        /// </summary>
        public static Snapshot Capture(GameObject sourceAvatar, Action<string> progressCallback = null)
        {
            var snapshot = new Snapshot();
            if (sourceAvatar == null) return snapshot;

            snapshot.AvatarName = sourceAvatar.name;
            progressCallback?.Invoke("Fingerprinting source avatar and its assets...");

            foreach (string path in CollectReferencedAssetPaths(sourceAvatar))
                snapshot.AssetFingerprints[path] = Fingerprint(path);

            CaptureHierarchy(sourceAvatar, snapshot);

            OptimizerLog.Info(Tag, $"Captured source fingerprint for '{sourceAvatar.name}': " +
                                   $"{snapshot.AssetFingerprints.Count} asset(s), {snapshot.TransformCount} transform(s).");
            OptimizerLog.Trace(Tag, () => "  assets:\n    " + string.Join("\n    ", snapshot.AssetFingerprints.Keys.OrderBy(p => p)));

            return snapshot;
        }

        /// <summary>
        /// Re-checks the snapshot after conversion and reports anything that changed.
        /// </summary>
        /// <param name="expectedChangedPaths">
        /// Asset paths the run was explicitly configured to modify — currently only the model importers
        /// touched by <see cref="AvatarRigOptimizer"/>. Changes to these are reported as information
        /// rather than errors; changes to anything else are defects.
        /// </param>
        /// <returns>True when nothing outside the expected set changed.</returns>
        public static bool Verify(
            GameObject sourceAvatar,
            Snapshot snapshot,
            ConversionSummary summary = null,
            IEnumerable<string> expectedChangedPaths = null)
        {
            if (snapshot == null) return true;

            var expected = new HashSet<string>(expectedChangedPaths ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            var modified = new List<string>();
            var deleted = new List<string>();
            var expectedHits = new List<string>();

            foreach (var kvp in snapshot.AssetFingerprints)
            {
                string path = kvp.Key;

                if (!File.Exists(path))
                {
                    deleted.Add(path);
                    continue;
                }

                if (Fingerprint(path) == kvp.Value) continue;

                if (expected.Contains(path)) expectedHits.Add(path);
                else modified.Add(path);
            }

            var hierarchyIssues = VerifyHierarchy(sourceAvatar, snapshot);

            foreach (string path in expectedHits)
                OptimizerLog.Info(Tag, $"Source asset '{path}' changed as configured (rig hygiene edits the shared model importer).");

            bool clean = modified.Count == 0 && deleted.Count == 0 && hierarchyIssues.Count == 0;

            if (clean)
            {
                OptimizerLog.Info(Tag, $"Source integrity verified: '{snapshot.AvatarName}' and all {snapshot.AssetFingerprints.Count} referenced asset(s) are unchanged.");
                summary?.AddSuccess($"Source avatar and its {snapshot.AssetFingerprints.Count} referenced asset(s) verified unchanged.");
                return true;
            }

            var report = new StringBuilder();
            report.AppendLine($"The conversion modified the source avatar or its assets. This breaks the non-destructive guarantee — the ORIGINAL avatar has been changed, not just the optimized copy.");

            if (deleted.Count > 0)
            {
                report.AppendLine($"  Deleted ({deleted.Count}):");
                foreach (string p in deleted.Take(20)) report.AppendLine($"    - {p}");
                if (deleted.Count > 20) report.AppendLine($"    ... and {deleted.Count - 20} more");
            }

            if (modified.Count > 0)
            {
                report.AppendLine($"  Modified ({modified.Count}):");
                foreach (string p in modified.Take(20)) report.AppendLine($"    - {p}");
                if (modified.Count > 20) report.AppendLine($"    ... and {modified.Count - 20} more");
            }

            if (hierarchyIssues.Count > 0)
            {
                report.AppendLine($"  Source hierarchy ({hierarchyIssues.Count}):");
                foreach (string issue in hierarchyIssues.Take(20)) report.AppendLine($"    - {issue}");
                if (hierarchyIssues.Count > 20) report.AppendLine($"    ... and {hierarchyIssues.Count - 20} more");
            }

            OptimizerLog.Error(Tag, report.ToString(), sourceAvatar);
            summary?.AddError($"Source integrity check FAILED: {modified.Count} asset(s) modified, {deleted.Count} deleted, {hierarchyIssues.Count} hierarchy change(s). See console — the original avatar was altered.");

            return false;
        }

        /// <summary>
        /// Asset paths the rig hygiene passes are expected to rewrite, so they can be excluded from the
        /// integrity check when those passes are enabled.
        /// </summary>
        public static IEnumerable<string> CollectModelImporterPaths(GameObject avatarRoot)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            if (avatarRoot == null) return paths;

            foreach (SkinnedMeshRenderer smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null) continue;
                string path = AssetDatabase.GetAssetPath(smr.sharedMesh);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }

            Animator animator = avatarRoot.GetComponent<Animator>();
            if (animator != null && animator.avatar != null)
            {
                string path = AssetDatabase.GetAssetPath(animator.avatar);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }

            return paths;
        }

        private static IEnumerable<string> CollectReferencedAssetPaths(GameObject sourceAvatar)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);

            UnityEngine.Object[] dependencies;
            try
            {
                dependencies = EditorUtility.CollectDependencies(new UnityEngine.Object[] { sourceAvatar });
            }
            catch (Exception e)
            {
                OptimizerLog.Warn(Tag, $"Could not collect dependencies for '{sourceAvatar.name}': {e.Message}. The asset check will be incomplete.");
                return paths;
            }

            foreach (UnityEngine.Object dep in dependencies)
            {
                if (dep == null) continue;

                string path = AssetDatabase.GetAssetPath(dep);
                // Scene objects have no asset path; built-in Unity resources cannot be written by us.
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal)) continue;
                if (!File.Exists(path)) continue;

                paths.Add(path);
            }

            return paths;
        }

        /// <summary>
        /// Combined fingerprint of an asset's contents and its .meta, so importer-setting changes (which
        /// leave the asset bytes alone) are caught too.
        /// </summary>
        private static string Fingerprint(string path)
        {
            return $"{FingerprintFile(path)}|{FingerprintFile(path + ".meta")}";
        }

        private static string FingerprintFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return "absent";

                var info = new FileInfo(path);

                // Hashing every large texture would dominate the conversion, so above the limit fall back
                // to size and write time — any real write updates both.
                if (info.Length > FullHashSizeLimit)
                    return $"size:{info.Length}:mtime:{info.LastWriteTimeUtc.Ticks}";

                using (var md5 = MD5.Create())
                using (FileStream stream = File.OpenRead(path))
                {
                    return "md5:" + BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "");
                }
            }
            catch (Exception e)
            {
                // An unreadable file is reported as such; it will compare unequal and be flagged, which is
                // the safe direction.
                return $"error:{e.GetType().Name}";
            }
        }

        private static void CaptureHierarchy(GameObject sourceAvatar, Snapshot snapshot)
        {
            foreach (Transform t in sourceAvatar.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                string path = AnimationUtility.CalculateTransformPath(t, sourceAvatar.transform);
                snapshot.HierarchySignature[path] = DescribeComponents(t);
                snapshot.TransformCount++;
            }
        }

        private static List<string> VerifyHierarchy(GameObject sourceAvatar, Snapshot snapshot)
        {
            var issues = new List<string>();
            if (sourceAvatar == null)
            {
                issues.Add("the source avatar GameObject no longer exists");
                return issues;
            }

            var current = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Transform t in sourceAvatar.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                current[AnimationUtility.CalculateTransformPath(t, sourceAvatar.transform)] = DescribeComponents(t);
            }

            foreach (var kvp in snapshot.HierarchySignature)
            {
                if (!current.TryGetValue(kvp.Key, out string now))
                {
                    issues.Add($"GameObject removed: '{(kvp.Key.Length == 0 ? "<root>" : kvp.Key)}'");
                    continue;
                }

                if (now != kvp.Value)
                    issues.Add($"components changed on '{(kvp.Key.Length == 0 ? "<root>" : kvp.Key)}': was [{kvp.Value}], now [{now}]");
            }

            foreach (string path in current.Keys)
            {
                if (!snapshot.HierarchySignature.ContainsKey(path))
                    issues.Add($"GameObject added: '{(path.Length == 0 ? "<root>" : path)}'");
            }

            return issues;
        }

        /// <summary>
        /// Component types on a transform, plus the mesh and material each renderer points at, so a
        /// swapped-in optimized asset on the source is caught as well as an added or removed component.
        /// </summary>
        private static string DescribeComponents(Transform t)
        {
            var parts = new List<string>();

            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null) { parts.Add("<missing script>"); continue; }

                string entry = c.GetType().Name;

                if (c is Renderer r)
                {
                    string mats = string.Join("+", (r.sharedMaterials ?? new Material[0]).Select(m => m == null ? "null" : m.name));
                    string mesh = r is SkinnedMeshRenderer smr && smr.sharedMesh != null ? smr.sharedMesh.name : "";
                    entry += $"({mesh}:{mats})";
                }
                else if (c is MeshFilter mf)
                {
                    entry += $"({(mf.sharedMesh != null ? mf.sharedMesh.name : "null")})";
                }

                parts.Add(entry);
            }

            // The pipeline deliberately disables the source avatar after cloning it, so active state is
            // not part of the signature.
            return string.Join(",", parts);
        }
    }
}
