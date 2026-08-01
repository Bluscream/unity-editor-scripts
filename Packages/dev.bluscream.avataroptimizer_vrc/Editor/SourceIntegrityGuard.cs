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
        private static readonly BluLog Log = BluLog.Get("SourceIntegrityGuard");

        /// <summary>Files at or below this size are hashed; larger ones fall back to size + write time.</summary>
        private const long FullHashSizeLimit = 64L * 1024 * 1024;

        /// <summary>How much of the project to fingerprint.</summary>
        public enum IntegrityScope
        {
            /// <summary>Only assets reachable from the source avatar. Fast, but blind to stray writes elsewhere.</summary>
            AvatarDependencies,
            /// <summary>Every asset under Assets/. Catches files written anywhere, at the cost of hashing the project.</summary>
            EntireProject
        }

        public sealed class Snapshot
        {
            public string AvatarName;
            public IntegrityScope Scope;
            /// <summary>Asset path → fingerprint of the file and of its .meta (importer settings).</summary>
            public Dictionary<string, string> AssetFingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
            /// <summary>Transform path → the components on it, so added/removed components are caught.</summary>
            public Dictionary<string, string> HierarchySignature = new Dictionary<string, string>(StringComparer.Ordinal);
            public int TransformCount;
        }

        /// <summary>One file whose fingerprint changed between capture and verification.</summary>
        public sealed class FileChange
        {
            public string Path;
            public string Before;
            public string After;
            public ChangeKind Kind;
            /// <summary>True when the run was configured to make this change.</summary>
            public bool Expected;

            public override string ToString()
            {
                switch (Kind)
                {
                    case ChangeKind.Added:    return $"{Path}\n        added   {After}";
                    case ChangeKind.Deleted:  return $"{Path}\n        deleted (was {Before})";
                    default:                  return $"{Path}\n        before  {Before}\n        after   {After}";
                }
            }
        }

        public enum ChangeKind { Modified, Added, Deleted }

        /// <summary>
        /// Fingerprints the source avatar and everything it references. Call before the conversion starts.
        /// </summary>
        public static Snapshot Capture(
            GameObject sourceAvatar,
            IntegrityScope scope = IntegrityScope.AvatarDependencies,
            Action<string> progressCallback = null)
        {
            var snapshot = new Snapshot { Scope = scope };
            if (sourceAvatar == null) return snapshot;

            snapshot.AvatarName = sourceAvatar.name;
            progressCallback?.Invoke($"Fingerprinting {(scope == IntegrityScope.EntireProject ? "the project" : "the source avatar and its assets")}...");

            var sw = System.Diagnostics.Stopwatch.StartNew();

            IEnumerable<string> paths = scope == IntegrityScope.EntireProject
                ? CollectAllProjectAssetPaths()
                : CollectReferencedAssetPaths(sourceAvatar);

            foreach (string path in paths)
                snapshot.AssetFingerprints[path] = Fingerprint(path);

            CaptureHierarchy(sourceAvatar, snapshot);

            Log.Info($"Captured {scope} fingerprint for '{sourceAvatar.name}': " +
                     $"{snapshot.AssetFingerprints.Count} file(s), {snapshot.TransformCount} transform(s), in {sw.Elapsed.TotalSeconds:F1}s.");
            Log.Trace(() => "  files:\n    " + string.Join("\n    ",
                snapshot.AssetFingerprints.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} = {kv.Value}")));

            return snapshot;
        }

        /// <summary>
        /// Re-checks the snapshot after conversion and reports every file whose fingerprint moved, with the
        /// before and after values so a change can be identified rather than merely noticed.
        /// </summary>
        /// <param name="expectedChangedPaths">
        /// Paths the run was configured to modify — model importers when rig hygiene is on, texture
        /// importers when the texture pass is on. Changes to these are reported as information; changes to
        /// anything else are defects.
        /// </param>
        /// <param name="expectedNewPathPrefixes">
        /// Directories the run is allowed to create files in, normally the asset output folder. Additions
        /// outside them are defects.
        /// </param>
        /// <returns>True when nothing outside the expected sets changed.</returns>
        public static bool Verify(
            GameObject sourceAvatar,
            Snapshot snapshot,
            ConversionSummary summary = null,
            IEnumerable<string> expectedChangedPaths = null,
            IEnumerable<string> expectedNewPathPrefixes = null)
        {
            if (snapshot == null) return true;

            var expected = new HashSet<string>(expectedChangedPaths ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var newPrefixes = (expectedNewPathPrefixes ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p.Replace('\\', '/').TrimEnd('/') + "/")
                .ToList();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var changes = new List<FileChange>();

            // Modified and deleted
            foreach (var kvp in snapshot.AssetFingerprints)
            {
                string path = kvp.Key;

                if (!File.Exists(path))
                {
                    changes.Add(new FileChange { Path = path, Before = kvp.Value, After = "(absent)", Kind = ChangeKind.Deleted });
                    continue;
                }

                string now = Fingerprint(path);
                if (now == kvp.Value) continue;

                changes.Add(new FileChange
                {
                    Path = path,
                    Before = kvp.Value,
                    After = now,
                    Kind = ChangeKind.Modified,
                    Expected = expected.Contains(path)
                });
            }

            // Added — only detectable when the whole project was fingerprinted, since a
            // dependency-scoped snapshot has no view of files that did not exist yet.
            if (snapshot.Scope == IntegrityScope.EntireProject)
            {
                foreach (string path in CollectAllProjectAssetPaths())
                {
                    if (snapshot.AssetFingerprints.ContainsKey(path)) continue;

                    bool inExpectedDir = newPrefixes.Any(prefix =>
                        path.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal));

                    changes.Add(new FileChange
                    {
                        Path = path,
                        Before = "(absent)",
                        After = Fingerprint(path),
                        Kind = ChangeKind.Added,
                        Expected = inExpectedDir
                    });
                }
            }

            var hierarchyIssues = VerifyHierarchy(sourceAvatar, snapshot);

            var unexpected = changes.Where(c => !c.Expected).ToList();
            var sanctioned = changes.Where(c => c.Expected).ToList();

            if (sanctioned.Count > 0)
            {
                Log.Info($"{sanctioned.Count} expected change(s) — the run was configured to make these:");
                foreach (FileChange c in sanctioned.Take(50)) Log.Info($"    {c}");
                if (sanctioned.Count > 50) Log.Info($"    ... and {sanctioned.Count - 50} more");
            }

            if (unexpected.Count == 0 && hierarchyIssues.Count == 0)
            {
                Log.Info($"Source integrity verified: '{snapshot.AvatarName}' and all {snapshot.AssetFingerprints.Count} " +
                         $"fingerprinted file(s) are unchanged apart from {sanctioned.Count} expected edit(s). Checked in {sw.Elapsed.TotalSeconds:F1}s.");
                summary?.AddSuccess($"Source integrity verified — {snapshot.AssetFingerprints.Count} file(s) checked, no unexpected changes.");
                return true;
            }

            var report = new StringBuilder();
            report.AppendLine("The conversion changed files it should not have. This breaks the non-destructive guarantee — " +
                              "something other than the optimized copy was modified.");

            foreach (ChangeKind kind in new[] { ChangeKind.Deleted, ChangeKind.Modified, ChangeKind.Added })
            {
                var group = unexpected.Where(c => c.Kind == kind).ToList();
                if (group.Count == 0) continue;

                report.AppendLine($"  {kind} ({group.Count}):");
                foreach (FileChange c in group.Take(50)) report.AppendLine($"    {c}");
                if (group.Count > 50) report.AppendLine($"    ... and {group.Count - 50} more");
            }

            if (hierarchyIssues.Count > 0)
            {
                report.AppendLine($"  Source hierarchy ({hierarchyIssues.Count}):");
                foreach (string issue in hierarchyIssues.Take(20)) report.AppendLine($"    - {issue}");
                if (hierarchyIssues.Count > 20) report.AppendLine($"    ... and {hierarchyIssues.Count - 20} more");
            }

            Log.Error(report.ToString(), sourceAvatar);
            summary?.AddError($"Source integrity check FAILED: {unexpected.Count(c => c.Kind == ChangeKind.Modified)} modified, " +
                              $"{unexpected.Count(c => c.Kind == ChangeKind.Deleted)} deleted, " +
                              $"{unexpected.Count(c => c.Kind == ChangeKind.Added)} unexpected new file(s), " +
                              $"{hierarchyIssues.Count} hierarchy change(s). See console for paths and hashes.");

            return false;
        }

        /// <summary>Every asset file under Assets/, excluding .meta (folded into each asset's fingerprint).</summary>
        private static IEnumerable<string> CollectAllProjectAssetPaths()
        {
            var paths = new List<string>();
            try
            {
                foreach (string guid in AssetDatabase.FindAssets(string.Empty))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;
                    if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                    if (!File.Exists(path)) continue; // folders
                    paths.Add(path);
                }
            }
            catch (Exception e)
            {
                Log.Warn($"Project-wide asset enumeration failed: {e.Message}. The integrity check will be incomplete.");
            }
            return paths.Distinct(StringComparer.Ordinal);
        }

        /// <summary>
        /// Texture asset paths the texture budget pass rewrites the importer settings of.
        ///
        /// Textures are deliberately NOT copied — per-platform import overrides are the mechanism VRChat
        /// expects, and duplicating every texture would bloat the project — so this pass edits the original
        /// .meta files. That is a real source-side change and has to be declared, or the integrity check
        /// would either miss it (when a previous run already wrote identical settings) or report it as a
        /// defect.
        /// </summary>
        public static IEnumerable<string> CollectTexturePaths(GameObject avatarRoot)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            if (avatarRoot == null) return paths;

            foreach (Renderer r in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;

                    int count = ShaderUtil.GetPropertyCount(m.shader);
                    for (int i = 0; i < count; i++)
                    {
                        if (ShaderUtil.GetPropertyType(m.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;

                        Texture tex = m.GetTexture(ShaderUtil.GetPropertyName(m.shader, i));
                        if (tex == null) continue;

                        string path = AssetDatabase.GetAssetPath(tex);
                        if (!string.IsNullOrEmpty(path)) paths.Add(path);
                    }
                }
            }

            return paths;
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
                Log.Warn($"Could not collect dependencies for '{sourceAvatar.name}': {e.Message}. The asset check will be incomplete.");
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
