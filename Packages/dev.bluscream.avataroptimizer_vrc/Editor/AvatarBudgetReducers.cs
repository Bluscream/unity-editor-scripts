using System;
using System.Collections.Generic;
using System.Linq;
using Bluscream.Budgeting;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>Budget names shared between the reducers and the conversion pipeline.</summary>
    public static class AvatarBudgets
    {
        public const string Bundle = "Compressed bundle";
        public const string Vram = "Texture VRAM";
    }

    /// <summary>
    /// Reduces texture resolution/format to fit the bundle and VRAM budgets.
    ///
    /// The bundle is (texture bytes + non-texture payload) and the payload is constant while only
    /// textures change, so the required texture budget can be solved for rather than guessed:
    ///     nonTexture     = measuredBundle − estimatedTextureDisk × modelScale
    ///     newTextureDisk = (bundleTarget − nonTexture) / modelScale
    /// modelScale is calibrated from consecutive (estimate, measured) samples, so systematic error in
    /// the disk model corrects itself instead of hiding in the residual.
    /// </summary>
    public class TextureBudgetReducer : IBudgetReducer
    {
        public string Name => "Texture compression";

        private readonly GameObject _avatar;
        private readonly Func<long, long, Bluscream.TextureCompressor.TextureBudgetRequest> _requestFactory;
        private readonly Action<string> _progress;

        private Bluscream.TextureCompressor.TextureBudgetResult _last;
        private double _diskModelScale = 1.0;
        private long _prevEstimatedDisk = -1, _prevMeasuredBundle = -1;

        public Bluscream.TextureCompressor.TextureBudgetResult LastResult => _last;
        /// <summary>Estimated real (calibrated) texture contribution to the bundle.</summary>
        public long EstimatedTextureBundleBytes => _last == null ? 0 : (long)(_last.EstimatedDiskBytes * _diskModelScale);

        public TextureBudgetReducer(
            GameObject avatar,
            Func<long, long, Bluscream.TextureCompressor.TextureBudgetRequest> requestFactory,
            Bluscream.TextureCompressor.TextureBudgetResult initialResult,
            Action<string> progress = null)
        {
            _avatar = avatar;
            _requestFactory = requestFactory;
            _last = initialResult;
            _progress = progress;
        }

        public bool CanReduce(BudgetSnapshot snapshot)
        {
            if (_avatar == null || _last == null) return false;
            // Once every texture sits at the 32px floor in its most aggressive format there is nothing left
            return !_last.HitFloor;
        }

        public string Reduce(BudgetSnapshot snapshot, int attempt)
        {
            BudgetItem bundle = snapshot[AvatarBudgets.Bundle];
            BudgetItem vram = snapshot[AvatarBudgets.Vram];
            if (_last == null) return null;

            long estimatedDisk = _last.EstimatedDiskBytes;

            // Calibrate the disk model from the last two samples
            if (_prevEstimatedDisk > 0 && _prevMeasuredBundle > 0 && bundle != null)
            {
                long estDelta = _prevEstimatedDisk - estimatedDisk;
                long measuredDelta = _prevMeasuredBundle - bundle.Actual;
                if (estDelta > 64 * 1024 && measuredDelta > 0)
                {
                    _diskModelScale = Math.Max(0.10, Math.Min(1.50, (double)measuredDelta / estDelta));
                    Debug.Log($"[TextureBudgetReducer] Calibrated disk model: 1 estimated MB ≈ {_diskModelScale:F2} MB of real bundle.");
                }
            }
            if (bundle != null)
            {
                _prevEstimatedDisk = estimatedDisk;
                _prevMeasuredBundle = bundle.Actual;
            }

            // Solve for the texture disk budget that lands the bundle on its target
            long newDiskBudget = long.MaxValue;
            if (bundle != null && bundle.Target != long.MaxValue)
            {
                long realTextureDisk = (long)(estimatedDisk * _diskModelScale);
                long nonTexture = Math.Max(0, bundle.Actual - realTextureDisk);
                newDiskBudget = (long)((bundle.Target - nonTexture) / Math.Max(0.10, _diskModelScale));

                if (newDiskBudget < 128 * 1024L)
                {
                    Debug.LogWarning($"[TextureBudgetReducer] Non-texture payload (~{nonTexture / (1024.0 * 1024.0):F2} MB) already fills the {bundle.Target / (1024.0 * 1024.0):F2} MB target — textures cannot help further.");
                    return null; // let the next reducer (meshes) take over
                }
            }

            long newVramBudget = vram != null && vram.Target != long.MaxValue ? vram.Target : long.MaxValue;

            _last = Bluscream.TextureCompressor.TextureBudgetOptimizer.Optimize(
                _avatar, _requestFactory(newVramBudget, newDiskBudget), _progress);

            return $"re-allocated textures to ≤ {newDiskBudget / (1024.0 * 1024.0):F2} MB on disk / ≤ {newVramBudget / (1024.0 * 1024.0):F1} MB VRAM ({_last.Describe()})";
        }
    }

    /// <summary>
    /// Reduces mesh triangle count when textures alone cannot bring the bundle under its cap.
    ///
    /// Decimation is destructive to silhouettes and blendshapes in a way texture compression is not,
    /// so it only runs after textures are exhausted, is sized against the measured overage rather
    /// than a blanket ratio, and never goes below a retention floor.
    /// </summary>
    public class MeshDecimationReducer : IBudgetReducer
    {
        public string Name => "Mesh decimation";

        private readonly GameObject _avatar;
        private readonly Action<string> _progress;
        private readonly float _minRetention;
        private readonly int _originalTriangles;
        private int _currentTriangles;

        public MeshDecimationReducer(GameObject avatar, float minRetention = 0.25f, Action<string> progress = null)
        {
            _avatar = avatar;
            _minRetention = Mathf.Clamp01(minRetention);
            _progress = progress;
            _originalTriangles = _currentTriangles = CountTriangles(avatar);
        }

        public bool CanReduce(BudgetSnapshot snapshot)
        {
            if (_avatar == null || _originalTriangles <= 0) return false;
            // Only the bundle budget is mesh-driven; VRAM here is texture memory
            BudgetItem bundle = snapshot[AvatarBudgets.Bundle];
            if (bundle == null || bundle.IsWithinTarget) return false;
            return _currentTriangles > Math.Max(1000, (int)(_originalTriangles * _minRetention));
        }

        public string Reduce(BudgetSnapshot snapshot, int attempt)
        {
            BudgetItem bundle = snapshot[AvatarBudgets.Bundle];
            if (bundle == null || bundle.Excess <= 0) return null;

            // The raw estimate is in UNCOMPRESSED asset terms and badly overshoots what a compressed
            // bundle actually stores (blendshape deltas especially). Meshes obviously cannot occupy
            // more than the whole bundle, so clamp to the measured size — otherwise the computed cut
            // is far too timid (183 MB "payload" for a 26 MB bundle yields a 9% cut per pass).
            long meshBytes = EstimateMeshBundleBytes(_avatar);
            if (meshBytes <= 0) return null;
            BudgetItem bundleItem = snapshot[AvatarBudgets.Bundle];
            if (bundleItem != null && bundleItem.Actual > 0)
                meshBytes = Math.Min(meshBytes, bundleItem.Actual);

            // Convert the measured overage into a proportional triangle cut. Under-estimating mesh
            // bytes just means a smaller cut and another iteration — never an overshoot.
            double removeFraction = Math.Min(0.60, (double)bundle.Excess / meshBytes);
            if (removeFraction < 0.02) removeFraction = 0.02; // always make meaningful progress

            int floorTris = Math.Max(1000, (int)(_originalTriangles * _minRetention));
            int targetTris = Math.Max(floorTris, (int)(_currentTriangles * (1.0 - removeFraction)));
            if (targetTris >= _currentTriangles) return null;

            _progress?.Invoke($"Decimating meshes {_currentTriangles:N0} → {targetTris:N0} triangles...");
            Debug.Log($"[MeshDecimationReducer] Bundle over by {bundle.Excess / (1024.0 * 1024.0):F2} MB; estimated mesh payload ~{meshBytes / (1024.0 * 1024.0):F2} MB → cutting {removeFraction * 100:F0}% of triangles ({_currentTriangles:N0} → {targetTris:N0}, floor {floorTris:N0}).");

            int finalTris = Bluscream.MobileDecimater.Editor.MobileDecimationProcessor.DecimateAvatarMeshesToTargetTris(
                _avatar, targetTris, _progress);

            int achieved = finalTris > 0 ? finalTris : CountTriangles(_avatar);
            if (achieved >= _currentTriangles) return null; // decimator could not reduce further

            int before = _currentTriangles;
            _currentTriangles = achieved;
            return $"decimated {before:N0} → {achieved:N0} triangles ({(1.0 - (double)achieved / _originalTriangles) * 100:F0}% below original)";
        }

        private static int CountTriangles(GameObject root)
        {
            if (root == null) return 0;
            int total = 0;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh m = MeshOf(r);
                if (m != null) total += (int)(TotalIndexCount(m) / 3);
            }
            return total;
        }

        private static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            if (r is MeshRenderer mr)
            {
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                return mf != null ? mf.sharedMesh : null;
            }
            return null;
        }

        private static uint TotalIndexCount(Mesh m)
        {
            uint total = 0;
            for (int i = 0; i < m.subMeshCount; i++) total += m.GetIndexCount(i);
            return total;
        }

        /// <summary>
        /// Rough estimate of what the avatar's meshes contribute to the bundle: vertex streams,
        /// index buffers and blendshape deltas. Absolute accuracy matters less than proportionality —
        /// it is only used to translate "bytes over" into "triangles to remove", and the loop
        /// re-measures after every pass.
        /// </summary>
        public static long EstimateMeshBundleBytes(GameObject root)
        {
            if (root == null) return 0;
            var seen = new HashSet<Mesh>();
            long bytes = 0;

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh m = MeshOf(r);
                if (m == null || !seen.Add(m)) continue;

                long stride = 12;                                  // position
                if (m.normals != null && m.normals.Length > 0) stride += 12;
                if (m.tangents != null && m.tangents.Length > 0) stride += 16;
                if (m.colors32 != null && m.colors32.Length > 0) stride += 4;
                if (m.uv != null && m.uv.Length > 0) stride += 8;
                if (m.uv2 != null && m.uv2.Length > 0) stride += 8;
                if (m.boneWeights != null && m.boneWeights.Length > 0) stride += 32; // 4 indices + 4 weights

                bytes += m.vertexCount * stride;
                bytes += TotalIndexCount(m) * (m.vertexCount > 65535 ? 4 : 2);

                // Blendshapes dominate on avatar meshes: each frame stores per-vertex deltas
                // (position + normal + tangent). Unity stores them sparsely, hence the 0.5 factor.
                if (m.blendShapeCount > 0)
                {
                    long frames = 0;
                    for (int i = 0; i < m.blendShapeCount; i++) frames += m.GetBlendShapeFrameCount(i);
                    bytes += (long)(frames * m.vertexCount * 40 * 0.5);
                }
            }
            return bytes;
        }
    }
}
