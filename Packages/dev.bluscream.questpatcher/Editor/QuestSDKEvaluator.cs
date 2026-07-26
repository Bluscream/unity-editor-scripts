using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Evaluates avatar stats and compares them against VRChat Quest performance limits
    /// </summary>
    public static class QuestSDKEvaluator
    {
        public class AvatarStats
        {
            public int TriangleCount;
            public int SkinnedMeshCount;
            public int MeshRendererCount;
            public int MaterialSlotCount;
            public int PhysBoneComponentCount;
            public int PhysBoneTransformCount;
            public int PhysBoneColliderCount;
            public int PhysBoneCollisionCheckCount;
            public long TotalTextureMemoryBytes;
            public string RatingName = "Unknown";
        }

        /// <summary>
        /// Calculates comprehensive stats for an avatar GameObject
        /// </summary>
        public static AvatarStats EvaluateAvatar(GameObject avatarRoot)
        {
            AvatarStats stats = new AvatarStats();
            if (avatarRoot == null) return stats;

            // Try VRChat SDK Reflection first
            try
            {
                Type perfStatsType = Type.GetType("VRC.SDKBase.Validation.Performance.Stats.AvatarPerformanceStats, VRC.SDKBase.Editor")
                    ?? Type.GetType("VRC.SDKBase.Validation.Performance.Stats.AvatarPerformanceStats, VRC.SDKBase");

                if (perfStatsType != null)
                {
                    MethodInfo calculateMethod = perfStatsType.GetMethod("CalculatePerformanceStats",
                        BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(GameObject), typeof(bool) }, null)
                        ?? perfStatsType.GetMethod("CalculatePerformanceStats", BindingFlags.Public | BindingFlags.Static);

                    if (calculateMethod != null)
                    {
                        object perfStatsObj = null;
                        var paramsCount = calculateMethod.GetParameters().Length;
                        if (paramsCount == 3)
                        {
                            perfStatsObj = calculateMethod.Invoke(null, new object[] { avatarRoot.name, avatarRoot, true }); // isMobile = true
                        }
                        else if (paramsCount == 2)
                        {
                            perfStatsObj = calculateMethod.Invoke(null, new object[] { avatarRoot.name, avatarRoot });
                        }

                        if (perfStatsObj != null)
                        {
                            FieldInfo polyField = perfStatsType.GetField("polyCount") ?? perfStatsType.GetField("triangleCount");
                            if (polyField != null) stats.TriangleCount = (int)polyField.GetValue(perfStatsObj);

                            FieldInfo smrField = perfStatsType.GetField("skinnedMeshCount");
                            if (smrField != null) stats.SkinnedMeshCount = (int)smrField.GetValue(perfStatsObj);

                            FieldInfo mrField = perfStatsType.GetField("meshRendererCount");
                            if (mrField != null) stats.MeshRendererCount = (int)mrField.GetValue(perfStatsObj);

                            FieldInfo matField = perfStatsType.GetField("materialCount");
                            if (matField != null) stats.MaterialSlotCount = (int)matField.GetValue(perfStatsObj);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[QuestSDKEvaluator] SDK reflection failed, using manual calculation: {e.Message}");
            }

            // Fallback / manual calculation if 0
            if (stats.TriangleCount == 0 || stats.MaterialSlotCount == 0)
            {
                Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers)
                {
                    if (r is SkinnedMeshRenderer smr)
                    {
                        stats.SkinnedMeshCount++;
                        if (smr.sharedMesh != null) stats.TriangleCount += smr.sharedMesh.triangles.Length / 3;
                    }
                    else if (r is MeshRenderer mr)
                    {
                        stats.MeshRendererCount++;
                        MeshFilter mf = mr.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) stats.TriangleCount += mf.sharedMesh.triangles.Length / 3;
                    }

                    if (r.sharedMaterials != null)
                    {
                        stats.MaterialSlotCount += r.sharedMaterials.Length;
                    }
                }
            }

            // Calculate PhysBones
            CalculatePhysBoneStats(avatarRoot, stats);

            // Calculate Texture Memory
            stats.TotalTextureMemoryBytes = CalculateTextureMemory(avatarRoot);

            // Determine Rank
            stats.RatingName = DetermineRating(stats);

            return stats;
        }

        private static void CalculatePhysBoneStats(GameObject avatarRoot, AvatarStats stats)
        {
            Component[] components = avatarRoot.GetComponentsInChildren<Component>(true);
            foreach (Component c in components)
            {
                if (c == null) continue;
                string typeName = c.GetType().FullName;
                if (typeName.Contains("VRCPhysBone"))
                {
                    if (typeName.EndsWith("VRCPhysBone"))
                    {
                        stats.PhysBoneComponentCount++;
                        // Inspect affected transforms if possible via SerializedObject
                        SerializedObject so = new SerializedObject(c);
                        SerializedProperty rootTransformProp = so.FindProperty("rootTransform");
                        Transform rootT = rootTransformProp != null ? rootTransformProp.objectReferenceValue as Transform : c.transform;
                        if (rootT != null)
                        {
                            stats.PhysBoneTransformCount += rootT.GetComponentsInChildren<Transform>(true).Length;
                        }
                    }
                    else if (typeName.Contains("VRCPhysBoneCollider"))
                    {
                        stats.PhysBoneColliderCount++;
                    }
                }
            }
            stats.PhysBoneCollisionCheckCount = stats.PhysBoneTransformCount * Math.Max(1, stats.PhysBoneColliderCount);
        }

        private static long CalculateTextureMemory(GameObject avatarRoot)
        {
            long totalBytes = 0;
            System.Collections.Generic.HashSet<Texture> textures = new System.Collections.Generic.HashSet<Texture>();

            Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    Shader shader = m.shader;
                    int count = ShaderUtil.GetPropertyCount(shader);
                    for (int i = 0; i < count; i++)
                    {
                        if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                        {
                            string propName = ShaderUtil.GetPropertyName(shader, i);
                            Texture tex = m.GetTexture(propName);
                            if (tex != null && !textures.Contains(tex))
                            {
                                textures.Add(tex);
                                totalBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                            }
                        }
                    }
                }
            }

            return totalBytes;
        }

        private static string DetermineRating(AvatarStats stats)
        {
            if (stats.TriangleCount <= 7500 && stats.MaterialSlotCount <= 1 && stats.PhysBoneComponentCount == 0)
                return "Excellent";
            if (stats.TriangleCount <= 15000 && stats.MaterialSlotCount <= 2 && stats.PhysBoneComponentCount <= 8)
                return "Good";
            if (stats.TriangleCount <= 32000 && stats.MaterialSlotCount <= 4 && stats.PhysBoneComponentCount <= 16)
                return "Medium";
            if (stats.TriangleCount <= 50000 && stats.MaterialSlotCount <= 8 && stats.PhysBoneComponentCount <= 32)
                return "Poor";

            return "Very Poor";
        }

        /// <summary>
        /// Prints all VRChat Quest validation alerts directly to the Unity Console
        /// </summary>
        public static void PrintSDKAlertsToConsole(GameObject avatarRoot, AvatarStats stats = null)
        {
            if (avatarRoot == null) return;
            if (stats == null) stats = EvaluateAvatar(avatarRoot);

            Debug.Log($"<color=cyan><b>[VRC-QuestPatcher] Avatar SDK Evaluation Report for '{avatarRoot.name}' (Platform: Android / Quest):</b></color>");

            // 1. Download & Uncompressed Size Limits
            Debug.Log($"[VRC-QuestPatcher] ℹ️ Quest Build Limits: Max Download Size: 10.00 MB | Max Uncompressed Size: 40.00 MB");

            // 2. Triangles
            if (stats.TriangleCount > 20000)
            {
                Debug.LogError($"[VRC-QuestPatcher Alert] 🔴 Triangles: {stats.TriangleCount:N0} (Quest Max: 20,000, Recommended: 7,500). Avatar will be blocked by default on Quest!");
            }
            else
            {
                Debug.Log($"[VRC-QuestPatcher Alert] 🟢 Triangles: {stats.TriangleCount:N0} / 20,000 max.");
            }

            // 3. Texture Memory
            double texMemMB = stats.TotalTextureMemoryBytes / (1024.0 * 1024.0);
            if (texMemMB > 40.0)
            {
                Debug.LogError($"[VRC-QuestPatcher Alert] 🔴 Texture Memory: {texMemMB:F2} MB (Quest Max: 40.00 MB, Recommended: 10.00 MB).");
            }
            else
            {
                Debug.Log($"[VRC-QuestPatcher Alert] 🟢 Texture Memory: {texMemMB:F2} MB / 40.00 MB max.");
            }

            // 4. Material Slots
            if (stats.MaterialSlotCount > 4)
            {
                Debug.LogWarning($"[VRC-QuestPatcher Alert] 🟡 Material Slots: {stats.MaterialSlotCount} (Quest Max for VeryPoor: 4, Recommended: 1).");
            }
            else
            {
                Debug.Log($"[VRC-QuestPatcher Alert] 🟢 Material Slots: {stats.MaterialSlotCount} / 4 max.");
            }

            // 5. PhysBone Components
            if (stats.PhysBoneComponentCount > 8)
            {
                Debug.LogError($"[VRC-QuestPatcher Alert] 🔴 PhysBone Components: {stats.PhysBoneComponentCount} (Quest Hard Limit: 8). ALL PhysBones will be stripped at runtime by VRChat!");
            }
            else
            {
                Debug.Log($"[VRC-QuestPatcher Alert] 🟢 PhysBone Components: {stats.PhysBoneComponentCount} / 8 max.");
            }

            // 6. PhysBone Collision Checks
            if (stats.PhysBoneCollisionCheckCount > 64)
            {
                Debug.LogError($"[VRC-QuestPatcher Alert] 🔴 PhysBone Collision Check Count: {stats.PhysBoneCollisionCheckCount} (Quest Hard Limit: 64). ALL PhysBone Colliders will be stripped at runtime by VRChat!");
            }
            else
            {
                Debug.Log($"[VRC-QuestPatcher Alert] 🟢 PhysBone Collision Checks: {stats.PhysBoneCollisionCheckCount} / 64 max.");
            }

            // 7. Incompatible Component Check
            Component[] components = avatarRoot.GetComponentsInChildren<Component>(true);
            int badCompCount = 0;
            foreach (Component c in components)
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                if (typeName == "Camera" || typeName == "Light" || typeName == "AudioSource" || typeName == "PostProcessVolume" || typeName == "VRC_Station")
                {
                    badCompCount++;
                    Debug.LogWarning($"[VRC-QuestPatcher Alert] ⚠️ Incompatible component found: '{typeName}' on '{c.gameObject.name}'.");
                }
            }

            Debug.Log($"<color=cyan><b>[VRC-QuestPatcher] Final Rank Estimate: {stats.RatingName} (Incompatible Components: {badCompCount})</b></color>");
        }
    }
}
