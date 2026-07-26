using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRC
{
    /// <summary>
    /// Evaluates avatar stats and compares them against VRChat performance limits
    /// </summary>
    public static class AvatarSDKEvaluator
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
                            perfStatsObj = calculateMethod.Invoke(null, new object[] { avatarRoot.name, avatarRoot, true });
                        }
                        else if (paramsCount == 2)
                        {
                            perfStatsObj = calculateMethod.Invoke(null, new object[] { avatarRoot.name, avatarRoot });
                        }

                        if (perfStatsObj != null)
                        {
                            ExtractSDKPerfStats(perfStatsObj, stats);
                            stats.RatingName = DetermineRating(stats);
                            return stats;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarSDKEvaluator] VRChat SDK Stats Reflection fallback: {e.Message}");
            }

            CalculateFallbackStats(avatarRoot, stats);
            stats.RatingName = DetermineRating(stats);
            return stats;
        }

        private static void ExtractSDKPerfStats(object perfStatsObj, AvatarStats stats)
        {
            Type t = perfStatsObj.GetType();

            stats.TriangleCount = GetIntProp(t, perfStatsObj, "polyCount", "polygonCount", "triangleCount");
            stats.SkinnedMeshCount = GetIntProp(t, perfStatsObj, "skinnedMeshCount");
            stats.MeshRendererCount = GetIntProp(t, perfStatsObj, "meshRendererCount");
            stats.MaterialSlotCount = GetIntProp(t, perfStatsObj, "materialCount");
            stats.PhysBoneComponentCount = GetIntProp(t, perfStatsObj, "physBoneComponentCount");
            stats.PhysBoneTransformCount = GetIntProp(t, perfStatsObj, "physBoneTransformCount");
            stats.PhysBoneColliderCount = GetIntProp(t, perfStatsObj, "physBoneColliderCount");
            stats.PhysBoneCollisionCheckCount = GetIntProp(t, perfStatsObj, "physBoneCollisionCheckCount");
        }

        private static int GetIntProp(Type t, object obj, params string[] propNames)
        {
            foreach (string name in propNames)
            {
                PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    object val = p.GetValue(obj);
                    if (val is int iVal) return iVal;
                }
                FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    object val = f.GetValue(obj);
                    if (val is int iVal) return iVal;
                }
            }
            return 0;
        }

        private static void CalculateFallbackStats(GameObject avatarRoot, AvatarStats stats)
        {
            int tris = 0;
            int skinned = 0;
            int renderers = 0;
            int matSlots = 0;

            foreach (Renderer r in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;

                if (r is SkinnedMeshRenderer smr)
                {
                    skinned++;
                    if (smr.sharedMesh != null) tris += smr.sharedMesh.triangles.Length / 3;
                    if (smr.sharedMaterials != null) matSlots += smr.sharedMaterials.Length;
                }
                else if (r is MeshRenderer mr)
                {
                    renderers++;
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                    if (mr.sharedMaterials != null) matSlots += mr.sharedMaterials.Length;
                }
            }

            stats.TriangleCount = tris;
            stats.SkinnedMeshCount = skinned;
            stats.MeshRendererCount = renderers;
            stats.MaterialSlotCount = matSlots;
            stats.TotalTextureMemoryBytes = CalculateTextureMemory(avatarRoot);

            CalculatePhysBoneStats(avatarRoot, stats);
        }

        private static void CalculatePhysBoneStats(GameObject avatarRoot, AvatarStats stats)
        {
            Component[] components = avatarRoot.GetComponentsInChildren<Component>(true);
            var pbList = new System.Collections.Generic.List<Component>();
            int colliders = 0;

            foreach (Component c in components)
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                if (typeName == "VRCPhysBone" || typeName == "VRCPhysBoneBase")
                {
                    pbList.Add(c);
                }
                else if (typeName.Contains("VRCPhysBoneCollider"))
                {
                    colliders++;
                }
            }

            int transforms = 0;
            int totalChecks = 0;

            foreach (Component pb in pbList)
            {
                int tCount = GetPhysBoneTransformCount(pb);
                transforms += tCount;

                int explicitColliders = GetPhysBoneColliderCount(pb);
                int effectiveColliders = explicitColliders > 0 ? explicitColliders : colliders;
                totalChecks += tCount * effectiveColliders;
            }

            stats.PhysBoneComponentCount = pbList.Count;
            stats.PhysBoneTransformCount = transforms;
            stats.PhysBoneColliderCount = colliders;
            stats.PhysBoneCollisionCheckCount = totalChecks;
        }

        private static int GetPhysBoneTransformCount(Component pb)
        {
            if (pb == null) return 1;
            try
            {
                Transform root = pb.transform;
                var rootProp = pb.GetType().GetProperty("rootTransform") ?? pb.GetType().GetProperty("RootTransform");
                if (rootProp != null && rootProp.GetValue(pb) is Transform customRoot && customRoot != null)
                {
                    root = customRoot;
                }
                return Bluscream.TransformExtensions.CountDescendants(root);
            }
            catch
            {
                return 1;
            }
        }

        private static int GetPhysBoneColliderCount(Component pb)
        {
            if (pb == null) return 0;
            try
            {
                SerializedObject so = new SerializedObject(pb);
                SerializedProperty collidersProp = so.FindProperty("colliders");
                return (collidersProp != null && collidersProp.isArray) ? collidersProp.arraySize : 0;
            }
            catch
            {
                return 0;
            }
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
            if (stats.TriangleCount <= 20000 && stats.MaterialSlotCount <= 4 && stats.PhysBoneComponentCount <= 8)
                return "Medium";
            if (stats.TriangleCount <= 20000 && stats.MaterialSlotCount <= 4 && stats.PhysBoneComponentCount <= 8)
                return "Poor";

            return "Very Poor";
        }

        /// <summary>
        /// Prints VRChat SDK validation alerts directly to Console
        /// </summary>
        public static void PrintSDKAlertsToConsole(GameObject avatarRoot, AvatarStats stats = null)
        {
            if (avatarRoot == null) return;
            if (stats == null) stats = EvaluateAvatar(avatarRoot);

            Debug.Log($"<color=cyan><b>================================================================================</b></color>");
            Debug.Log($"<color=cyan><b>[AvatarSDKEvaluator] VRChat SDK Alert Report for Avatar '{avatarRoot.name}':</b></color>");
            Debug.Log($"<color=cyan><b>================================================================================</b></color>");

            int sdkAlertCount = 0;

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
                                    try
                                    {
                                        if (selBuilderField.GetValue(sdkPanel) == null)
                                        {
                                            selBuilderField.SetValue(sdkPanel, builderInstance);
                                        }
                                    }
                                    catch { }
                                    break;
                                }
                                currType = currType.BaseType;
                            }

                            MethodInfo onGuiMethod = panelType.GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (onGuiMethod != null)
                            {
                                Debug.Log("[AvatarSDKEvaluator] Running VRChat SDK Avatar Builder GUI validation pass...");
                                try { onGuiMethod.Invoke(sdkPanel, null); } catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AvatarSDKEvaluator] Could not trigger SDK Builder GUI validation: {ex.Message}");
            }

            if (stats.TriangleCount > 20000)
            {
                Debug.LogWarning($"[AvatarSDKEvaluator] [SDK ALERT] Polygon count ({stats.TriangleCount}) exceeds Quest hard limit (20,000 max for Poor/Medium).");
                sdkAlertCount++;
            }
            if (stats.MaterialSlotCount > 4)
            {
                Debug.LogWarning($"[AvatarSDKEvaluator] [SDK ALERT] Material slot count ({stats.MaterialSlotCount}) exceeds Quest hard limit (4 max).");
                sdkAlertCount++;
            }

            if (sdkAlertCount == 0)
            {
                Debug.Log($"<color=green><b>[AvatarSDKEvaluator] No blocking VRChat SDK alerts detected for avatar '{avatarRoot.name}'.</b></color>");
            }
            else
            {
                Debug.LogWarning($"[AvatarSDKEvaluator] Total SDK Alert(s): {sdkAlertCount}.");
            }
            Debug.Log($"<color=cyan><b>================================================================================</b></color>");
        }

        public static long BuildAvatarAssetBundle(GameObject avatarRoot, out string bundlePath)
        {
            bundlePath = null;
            if (avatarRoot == null) return -1;
            DateTime buildStartTime = DateTime.Now.AddSeconds(-2);

            try
            {
                Type builderType = Type.GetType("VRC.SDK3A.Editor.VRCSdkControlPanelAvatarBuilder, com.vrchat.avatars.Editor")
                    ?? AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } }).FirstOrDefault(t => t.FullName == "VRC.SDK3A.Editor.VRCSdkControlPanelAvatarBuilder");
                if (builderType != null)
                {
                    MethodInfo buildMethod = builderType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        .FirstOrDefault(m => m.Name == "Build" && m.GetParameters().Length == 3);

                    if (buildMethod != null)
                    {
                        object builderInstance = Activator.CreateInstance(builderType);
                        Debug.Log($"[AvatarSDKEvaluator] Invoking VRChat SDK production build verification for '{avatarRoot.name}'...");
                        // Pass testAvatar: false so full upload pipeline (including VRCFury & shader locking) executes for 100% accurate bundle size
                        object taskObj = buildMethod.Invoke(builderInstance, new object[] { avatarRoot, false, null });
                        if (taskObj is Task task)
                        {
                            double startWait = UnityEditor.EditorApplication.timeSinceStartup;
                            UnityEditor.EditorApplication.CallbackFunction updateHandler = null;
                            updateHandler = () =>
                            {
                                if (task.IsCompleted || task.IsFaulted || task.IsCanceled)
                                {
                                    UnityEditor.EditorApplication.update -= updateHandler;
                                }
                            };
                            UnityEditor.EditorApplication.update += updateHandler;

                            while (!task.IsCompleted && !task.IsFaulted && !task.IsCanceled)
                            {
                                System.Threading.Thread.Sleep(10);
                                if (UnityEditor.EditorApplication.timeSinceStartup - startWait > 60) break;
                            }
                            UnityEditor.EditorApplication.update -= updateHandler;
                        }
                        return GetBuiltBundleSize(out bundlePath, buildStartTime);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarSDKEvaluator] Dry-run AssetBundle build via VRChat SDK failed: {e.Message}");
            }

            return GetBuiltBundleSize(out bundlePath, buildStartTime);
        }

        public static long GetBuiltBundleSize(out string bundlePath, DateTime minCreationTime)
        {
            bundlePath = null;
            try
            {
                string cachePath = Application.temporaryCachePath;
                if (Directory.Exists(cachePath))
                {
                    DirectoryInfo dir = new DirectoryInfo(cachePath);
                    FileInfo[] files = dir.GetFiles("*.vrca", SearchOption.AllDirectories);

                    if (files.Length > 0)
                    {
                        FileInfo newestBundle = null;
                        foreach (FileInfo f in files)
                        {
                            if (f.LastWriteTime >= minCreationTime)
                            {
                                if (newestBundle == null || f.LastWriteTime > newestBundle.LastWriteTime)
                                {
                                    newestBundle = f;
                                }
                            }
                        }

                        if (newestBundle != null)
                        {
                            bundlePath = newestBundle.FullName;
                            Debug.Log($"[AvatarSDKEvaluator] Dry-run AssetBundle built successfully: '{bundlePath}' ({newestBundle.Length / (1024.0 * 1024.0):F2} MB)");
                            return newestBundle.Length;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarSDKEvaluator] Dry-run bundle build check skipped/failed: {e.Message}");
            }

            return -1;
        }
    }
}
