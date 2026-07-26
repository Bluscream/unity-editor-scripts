using System;
using System.IO;
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
            int totalChecks = 0;

            foreach (Component c in components)
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                if (typeName == "VRCPhysBone" || typeName == "VRCPhysBoneBase")
                {
                    stats.PhysBoneComponentCount++;
                    SerializedObject so = new SerializedObject(c);
                    SerializedProperty rootTProp = so.FindProperty("rootTransform");
                    Transform rootT = rootTProp != null && rootTProp.objectReferenceValue != null ? (Transform)rootTProp.objectReferenceValue : c.transform;
                    int chainTransforms = rootT != null ? rootT.GetComponentsInChildren<Transform>(true).Length : 1;
                    stats.PhysBoneTransformCount += chainTransforms;

                    SerializedProperty collidersProp = so.FindProperty("colliders");
                    int explicitColliders = (collidersProp != null && collidersProp.isArray) ? collidersProp.arraySize : 0;
                    int effectiveColliders = explicitColliders > 0 ? explicitColliders : stats.PhysBoneColliderCount;

                    totalChecks += (chainTransforms * effectiveColliders);
                }
                else if (typeName.Contains("VRCPhysBoneCollider"))
                {
                    stats.PhysBoneColliderCount++;
                }
            }

            stats.PhysBoneCollisionCheckCount = totalChecks;
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

            Debug.Log($"<color=cyan><b>================================================================================</b></color>");
            Debug.Log($"<color=cyan><b>[VRC-QuestPatcher] VRChat SDK Alert Report for Avatar '{avatarRoot.name}':</b></color>");
            Debug.Log($"<color=cyan><b>================================================================================</b></color>");

            int sdkAlertCount = 0;

            // Extract exact VRChat SDK GUI alert cards via reflection if SDK is present
            try
            {
                Type avatarBuilderType = Type.GetType("VRCSdkControlPanelAvatarBuilder, VRCSDK3A-Editor")
                    ?? Type.GetType("VRCSdkControlPanelAvatarBuilder");

                if (avatarBuilderType != null)
                {
                    FieldInfo instanceField = avatarBuilderType.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    object builderInstance = instanceField?.GetValue(null);

                    if (builderInstance != null)
                    {
                        FieldInfo panelField = avatarBuilderType.GetField("_builder", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        object sdkPanel = panelField?.GetValue(builderInstance);

                        if (sdkPanel != null)
                        {
                            Type panelType = sdkPanel.GetType();
                            Type currType = panelType;
                            while (currType != null && currType != typeof(object))
                            {
                                FieldInfo selBuilderField = currType.GetField("_selectedBuilder", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                if (selBuilderField != null)
                                {
                                    if (selBuilderField.GetValue(sdkPanel) == null)
                                    {
                                        selBuilderField.SetValue(sdkPanel, builderInstance);
                                    }
                                    break;
                                }
                                currType = currType.BaseType;
                            }

                            // Force VRChat SDK to run validation pass to populate GUI issue dictionaries
                            try
                            {
                                MethodInfo validateMethod = avatarBuilderType.GetMethod("ValidateFeatures", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                if (validateMethod != null)
                                {
                                    Component desc = avatarRoot.GetComponent("VRC_AvatarDescriptor");
                                    Animator anim = avatarRoot.GetComponent<Animator>();
                                    validateMethod.Invoke(builderInstance, new object[] { desc, anim, null });
                                }
                            }
                            catch { }

                            object descriptorObj = avatarRoot.GetComponent("VRC_AvatarDescriptor") ?? (object)avatarRoot;

                            PrintDictIssues(builderInstance, avatarBuilderType, "GUIErrors", "🔴 [SDK GUI ERROR]", descriptorObj, ref sdkAlertCount);
                            PrintDictIssues(builderInstance, avatarBuilderType, "GUIWarnings", "🟡 [SDK GUI WARNING]", descriptorObj, ref sdkAlertCount);
                            PrintDictIssues(builderInstance, avatarBuilderType, "GUIInfos", "ℹ️ [SDK GUI INFO]", descriptorObj, ref sdkAlertCount);
                            PrintDictIssues(builderInstance, avatarBuilderType, "GUIStats", "📊 [SDK GUI STAT]", descriptorObj, ref sdkAlertCount);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRC-QuestPatcher] Could not dump live SDK GUI issue dictionary: {e.Message}");
            }

            // Also print calculated metrics summary
            Debug.Log($"<color=cyan><b>--------------------------------------------------------------------------------</b></color>");
            Debug.Log($"<color=cyan><b>[VRC-QuestPatcher] Performance Metrics Summary for '{avatarRoot.name}':</b></color>");
            Debug.Log($"[VRC-QuestPatcher Metrics] Triangles: {stats.TriangleCount:N0} (Quest Max: 20,000)");
            Debug.Log($"[VRC-QuestPatcher Metrics] Texture Memory: {stats.TotalTextureMemoryBytes / (1024.0 * 1024.0):F2} MB (Quest Max: 40.00 MB)");
            Debug.Log($"[VRC-QuestPatcher Metrics] Material Slots: {stats.MaterialSlotCount} (Quest Max: 4)");
            Debug.Log($"[VRC-QuestPatcher Metrics] PhysBone Components: {stats.PhysBoneComponentCount} (Quest Max: 8)");
            Debug.Log($"[VRC-QuestPatcher Metrics] PhysBone Colliders: {stats.PhysBoneColliderCount} (Quest Max: 16)");
            Debug.Log($"[VRC-QuestPatcher Metrics] PhysBone Collision Checks: {stats.PhysBoneCollisionCheckCount} (Quest Max: 64)");
            Debug.Log($"<color=cyan><b>[VRC-QuestPatcher] Estimated Rating: {stats.RatingName} | Extracted GUI Alerts: {sdkAlertCount}</b></color>");
            Debug.Log($"<color=cyan><b>================================================================================</b></color>");
        }

        private static void PrintDictIssues(object sdkPanel, Type panelType, string dictName, string prefix, object targetSubject, ref int alertCount)
        {
            FieldInfo dictField = panelType.GetField(dictName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (dictField == null) return;

            if (dictField.GetValue(sdkPanel) is System.Collections.IDictionary dict)
            {
                foreach (System.Collections.DictionaryEntry kvp in dict)
                {
                    if (kvp.Value is System.Collections.IEnumerable issueList)
                    {
                        foreach (object issue in issueList)
                        {
                            if (issue == null) continue;
                            FieldInfo textProp = issue.GetType().GetField("issueText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            string text = textProp?.GetValue(issue) as string;
                            if (!string.IsNullOrEmpty(text))
                            {
                                alertCount++;
                                if (prefix.Contains("ERROR"))
                                    Debug.LogError($"{prefix} {text}");
                                else if (prefix.Contains("WARNING"))
                                    Debug.LogWarning($"{prefix} {text}");
                                else
                                    Debug.Log($"{prefix} {text}");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to build the avatar AssetBundle via VRChat SDK reflection to inspect exact compressed bundle size on disk.
        /// </summary>
        public static long BuildAvatarBundleDryRun(GameObject avatarRoot, out string bundlePath)
        {
            bundlePath = null;
            if (avatarRoot == null) return -1;

            try
            {
                Type avatarBuilderType = Type.GetType("VRC.SDK3.Builder.VRCAvatarBuilder, VRCSDK3A-Editor")
                    ?? Type.GetType("VRC.SDK3.Builder.VRCAvatarBuilder");

                if (avatarBuilderType != null)
                {
                    MethodInfo exportBlueprintMethod = avatarBuilderType.GetMethod("ExportAvatarBlueprint", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (exportBlueprintMethod != null)
                    {
                        Debug.Log($"[QuestSDKEvaluator] Invoking VRCAvatarBuilder.ExportAvatarBlueprint for '{avatarRoot.name}'...");
                        exportBlueprintMethod.Invoke(null, new object[] { avatarRoot });

                        // Search temporary cache & temp directories for generated .vrca or asset bundle files
                        string tempDir = Path.Combine(Directory.GetCurrentDirectory(), "Temp");
                        string[] candidateFiles = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories);
                        
                        FileInfo newestBundle = null;
                        foreach (string file in candidateFiles)
                        {
                            if (file.EndsWith(".vrca") || file.EndsWith(".vrcb") || file.Contains("vrcAvatar"))
                            {
                                FileInfo fi = new FileInfo(file);
                                if (newestBundle == null || fi.LastWriteTime > newestBundle.LastWriteTime)
                                {
                                    newestBundle = fi;
                                }
                            }
                        }

                        if (newestBundle != null)
                        {
                            bundlePath = newestBundle.FullName;
                            Debug.Log($"[QuestSDKEvaluator] Dry-run AssetBundle built successfully: '{bundlePath}' ({newestBundle.Length / (1024.0 * 1024.0):F2} MB)");
                            return newestBundle.Length;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[QuestSDKEvaluator] Dry-run bundle build check skipped/failed: {e.Message}");
            }

            return -1;
        }
    }
}
