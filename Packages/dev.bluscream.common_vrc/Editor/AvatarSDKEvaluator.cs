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
            public int ContactCount;
            public int ConstraintCount;
            public int ConstraintDepth;
            public int ParticleSystemCount;
            public int ActiveParticleCount;
            public int MeshParticlePolyCount;
            public int TrailRendererCount;
            public int LineRendererCount;
            public int RaycastCount;
            public int ClothCount;
            public int ClothVertexCount;
            public int PhysicsColliderCount;
            public int PhysicsRigidbodyCount;
            public int LightCount;
            public int AudioSourceCount;
            public int AnimatorCount;
            public int BoneCount;
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
                            CalculateFallbackStats(avatarRoot, stats);
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
            stats.ContactCount = GetIntProp(t, perfStatsObj, "contactCount", "contactsCount");
            stats.ConstraintCount = GetIntProp(t, perfStatsObj, "constraintCount");
            stats.ConstraintDepth = GetIntProp(t, perfStatsObj, "constraintDepth");
            stats.ParticleSystemCount = GetIntProp(t, perfStatsObj, "particleSystemCount");
            stats.ActiveParticleCount = GetIntProp(t, perfStatsObj, "particleCount", "activeParticlesCount");
            stats.TrailRendererCount = GetIntProp(t, perfStatsObj, "trailRendererCount");
            stats.LineRendererCount = GetIntProp(t, perfStatsObj, "lineRendererCount");
            stats.ClothCount = GetIntProp(t, perfStatsObj, "clothCount");
            stats.ClothVertexCount = GetIntProp(t, perfStatsObj, "clothMaxVertices");
            stats.LightCount = GetIntProp(t, perfStatsObj, "lightCount");
            stats.AudioSourceCount = GetIntProp(t, perfStatsObj, "audioSourceCount");
            stats.AnimatorCount = GetIntProp(t, perfStatsObj, "animatorCount");
            stats.BoneCount = GetIntProp(t, perfStatsObj, "boneCount");
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

            Component[] allComps = avatarRoot.GetComponentsInChildren<Component>(true);
            stats.ContactCount = allComps.Count(c => c != null && (c.GetType().Name.Contains("VRCContactSender") || c.GetType().Name.Contains("VRCContactReceiver")));
            stats.ConstraintCount = allComps.Count(c => c != null && c.GetType().Name.ToLowerInvariant().Contains("constraint"));
            stats.TrailRendererCount = avatarRoot.GetComponentsInChildren<TrailRenderer>(true).Length;
            stats.LineRendererCount = avatarRoot.GetComponentsInChildren<LineRenderer>(true).Length;
            stats.LightCount = avatarRoot.GetComponentsInChildren<Light>(true).Length;
            stats.AudioSourceCount = avatarRoot.GetComponentsInChildren<AudioSource>(true).Length;
            stats.AnimatorCount = avatarRoot.GetComponentsInChildren<Animator>(true).Length;

            var cloths = avatarRoot.GetComponentsInChildren<Cloth>(true);
            stats.ClothCount = cloths.Length;
            stats.ClothVertexCount = cloths.Sum(c => c.vertices != null ? c.vertices.Length : 0);

            var particleSystems = avatarRoot.GetComponentsInChildren<ParticleSystem>(true);
            stats.ParticleSystemCount = particleSystems.Length;
            stats.ActiveParticleCount = particleSystems.Sum(ps => ps.main.maxParticles);
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

        private static readonly Dictionary<TextureFormat, float> FormatBPP = new Dictionary<TextureFormat, float>()
        {
            { TextureFormat.Alpha8, 8f },
            { TextureFormat.ARGB4444, 16f },
            { TextureFormat.RGB24, 24f },
            { TextureFormat.RGBA32, 32f },
            { TextureFormat.ARGB32, 32f },
            { TextureFormat.RGB565, 16f },
            { TextureFormat.R16, 16f },
            { TextureFormat.DXT1, 4f },
            { TextureFormat.DXT5, 8f },
            { TextureFormat.RGBA4444, 16f },
            { TextureFormat.BGRA32, 32f },
            { TextureFormat.RHalf, 16f },
            { TextureFormat.RGHalf, 32f },
            { TextureFormat.RGBAHalf, 64f },
            { TextureFormat.RFloat, 32f },
            { TextureFormat.RGFloat, 64f },
            { TextureFormat.RGBAFloat, 128f },
            { TextureFormat.BC6H, 8f },
            { TextureFormat.BC7, 8f },
            { TextureFormat.BC4, 4f },
            { TextureFormat.BC5, 8f },
            { TextureFormat.DXT1Crunched, 4f },
            { TextureFormat.DXT5Crunched, 8f },
            { TextureFormat.PVRTC_RGB2, 2f },
            { TextureFormat.PVRTC_RGBA2, 2f },
            { TextureFormat.PVRTC_RGB4, 4f },
            { TextureFormat.PVRTC_RGBA4, 4f },
            { TextureFormat.ETC_RGB4, 4f },
            { TextureFormat.ETC2_RGB, 4f },
            { TextureFormat.ETC2_RGBA1, 4f },
            { TextureFormat.ETC2_RGBA8, 8f },
            { TextureFormat.ETC_RGB4Crunched, 4f },
            { TextureFormat.ETC2_RGBA8Crunched, 8f },
            { TextureFormat.ASTC_4x4, 8f },
            { TextureFormat.ASTC_5x5, 5.12f },
            { TextureFormat.ASTC_6x6, 3.55f },
            { TextureFormat.ASTC_8x8, 2f },
            { TextureFormat.ASTC_10x10, 1.28f },
            { TextureFormat.ASTC_12x12, 1f },
            { TextureFormat.R8, 8f }
        };

        private static float GetFormatBPP(TextureImporterFormat format, TextureFormat fallbackFormat)
        {
            switch (format)
            {
                case TextureImporterFormat.DXT1:
                case TextureImporterFormat.DXT1Crunched:
                case TextureImporterFormat.BC4:
                case TextureImporterFormat.ETC_RGB4:
                case TextureImporterFormat.ETC2_RGB4:
                case TextureImporterFormat.ETC2_RGB4_PUNCHTHROUGH_ALPHA:
                case TextureImporterFormat.ETC_RGB4Crunched:
                    return 4.0f;
                case TextureImporterFormat.DXT5:
                case TextureImporterFormat.DXT5Crunched:
                case TextureImporterFormat.BC5:
                case TextureImporterFormat.BC7:
                case TextureImporterFormat.BC6H:
                case TextureImporterFormat.ETC2_RGBA8:
                case TextureImporterFormat.ETC2_RGBA8Crunched:
                    return 8.0f;
                case TextureImporterFormat.ASTC_4x4:
                case TextureImporterFormat.ASTC_HDR_4x4:
                    return 8.0f;
                case TextureImporterFormat.ASTC_5x5:
                case TextureImporterFormat.ASTC_HDR_5x5:
                    return 5.12f;
                case TextureImporterFormat.ASTC_6x6:
                case TextureImporterFormat.ASTC_HDR_6x6:
                    return 3.55f;
                case TextureImporterFormat.ASTC_8x8:
                case TextureImporterFormat.ASTC_HDR_8x8:
                    return 2.0f;
                case TextureImporterFormat.ASTC_10x10:
                case TextureImporterFormat.ASTC_HDR_10x10:
                    return 1.28f;
                case TextureImporterFormat.ASTC_12x12:
                case TextureImporterFormat.ASTC_HDR_12x12:
                    return 1.0f;
                case TextureImporterFormat.RGBA32:
                case TextureImporterFormat.ARGB32:
                    return 32.0f;
                case TextureImporterFormat.RGB24:
                    return 24.0f;
                case TextureImporterFormat.RGB16:
                case TextureImporterFormat.RGBA16:
                    return 16.0f;
                case TextureImporterFormat.Alpha8:
                    return 8.0f;
            }

            if (FormatBPP.TryGetValue(fallbackFormat, out float bpp)) return bpp;
            return 16.0f;
        }

        private static long CalculateTextureMemory(GameObject avatarRoot)
        {
            string platformName = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? "Android" :
                           (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS ? "iPhone" : "Standalone");

            long totalBytes = 0;
            HashSet<Texture> textures = new HashSet<Texture>();

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

                                string path = AssetDatabase.GetAssetPath(tex);
                                if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is TextureImporter imp)
                                {
                                    TextureImporterPlatformSettings settings = imp.GetPlatformTextureSettings(platformName);
                                    bool isOverridden = settings != null && settings.overridden;

                                    if (isOverridden)
                                    {
                                        // Use importer metadata for textures that have a platform override (accurate for our compressed Android textures)
                                        int maxRes = settings.maxTextureSize;
                                        float bpp = GetFormatBPP(settings.format, (tex is Texture2D t2d2) ? t2d2.format : TextureFormat.RGBA32);

                                        int w = Math.Min(tex.width, maxRes > 0 ? maxRes : tex.width);
                                        int h = Math.Min(tex.height, maxRes > 0 ? maxRes : tex.height);

                                        long texBytes = 0;
                                        int mipCount = tex.mipmapCount > 0 ? tex.mipmapCount : 1;
                                        for (int mLevel = 0; mLevel < mipCount; mLevel++)
                                        {
                                            int mipW = Math.Max(1, w >> mLevel);
                                            int mipH = Math.Max(1, h >> mLevel);
                                            texBytes += (long)Math.Max(1, (mipW * mipH * bpp) / 8.0f);
                                        }

                                        if (tex is Cubemap) texBytes *= 6;
                                        else if (tex is Texture2DArray arr) texBytes *= arr.depth;

                                        totalBytes += texBytes;
                                    }
                                    else
                                    {
                                        // No platform override — use GPU profiler actual size (matches what VRChat SDK reports)
                                        totalBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                                    }
                                }
                                else
                                {
                                    // Not an asset importer texture (e.g. render texture / proc gen) — use GPU profiler
                                    totalBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                                }
                            }
                        }
                    }
                }
            }

            return totalBytes;
        }

        public static long CalculateMeshVRAM(GameObject avatarRoot)
        {
            long totalBytes = 0;
            HashSet<Mesh> meshes = new HashSet<Mesh>();

            foreach (Renderer r in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : (r is MeshRenderer mr && mr.GetComponent<MeshFilter>() != null ? mr.GetComponent<MeshFilter>().sharedMesh : null);
                if (mesh == null || meshes.Contains(mesh)) continue;
                meshes.Add(mesh);

                long vertexAttributeVRAMSize = 0;
                var vertexAttributes = mesh.GetVertexAttributes();
                bool isSkinned = r is SkinnedMeshRenderer;

                foreach (var attr in vertexAttributes)
                {
                    int skinnedMultiplier = (isSkinned && (attr.attribute == UnityEngine.Rendering.VertexAttribute.Position || attr.attribute == UnityEngine.Rendering.VertexAttribute.Normal || attr.attribute == UnityEngine.Rendering.VertexAttribute.Tangent)) ? 2 : 1;
                    int formatSize = 4;
                    if (attr.format == UnityEngine.Rendering.VertexAttributeFormat.Float16 || attr.format == UnityEngine.Rendering.VertexAttributeFormat.SNorm16 || attr.format == UnityEngine.Rendering.VertexAttributeFormat.UNorm16) formatSize = 2;
                    else if (attr.format == UnityEngine.Rendering.VertexAttributeFormat.UInt8 || attr.format == UnityEngine.Rendering.VertexAttributeFormat.SInt8 || attr.format == UnityEngine.Rendering.VertexAttributeFormat.UNorm8 || attr.format == UnityEngine.Rendering.VertexAttributeFormat.SNorm8) formatSize = 1;

                    vertexAttributeVRAMSize += formatSize * attr.dimension * skinnedMultiplier;
                }

                long blendShapeVRAMSize = 0;
                var deltaPositions = new Vector3[mesh.vertexCount];
                var deltaNormals = new Vector3[mesh.vertexCount];
                var deltaTangents = new Vector3[mesh.vertexCount];

                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    int frameCount = mesh.GetBlendShapeFrameCount(i);
                    for (int f = 0; f < frameCount; f++)
                    {
                        mesh.GetBlendShapeFrameVertices(i, f, deltaPositions, deltaNormals, deltaTangents);
                        for (int k = 0; k < deltaPositions.Length; k++)
                        {
                            if (deltaPositions[k] != Vector3.zero || deltaNormals[k] != Vector3.zero || deltaTangents[k] != Vector3.zero)
                            {
                                blendShapeVRAMSize += 40;
                            }
                        }
                    }
                }

                totalBytes += (vertexAttributeVRAMSize * mesh.vertexCount) + blendShapeVRAMSize;
            }

            return totalBytes;
        }

        public enum AlertSeverity { Info, Warning, Error, BlockingError }

        public class SDKAlert
        {
            public AlertSeverity Severity;
            public string Category;
            public string Message;
            public UnityEngine.Object TargetObject;
            public System.Action SelectAction;
            public System.Action AutoFixAction;

            public bool HasSelect => SelectAction != null;
            public bool HasAutoFix => AutoFixAction != null;

            public void InvokeSelect() => SelectAction?.Invoke();
            public void InvokeAutoFix() => AutoFixAction?.Invoke();
        }

        /// <summary>
        /// Attempts to extract live VRChat SDK validation alerts directly from the VRCSdkControlPanelBuilder instance,
        /// including exact alert messages, target object pointers, Select actions, and Auto-Fix delegates.
        /// </summary>
        public static List<SDKAlert> GetSDKAlertsFromVRCSDK(GameObject avatarRoot)
        {
            var alerts = new List<SDKAlert>();
            try
            {
                Type windowType = Type.GetType("VRCSdkControlPanel, VRCSDK3A-Editor")
                    ?? Type.GetType("VRCSdkControlPanel, VRC.SDKBase.Editor")
                    ?? Type.GetType("VRCSdkControlPanel");

                if (windowType == null) return alerts;

                FieldInfo windowInstanceField = windowType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object windowInstance = windowInstanceField?.GetValue(null);

                if (windowInstance == null)
                {
                    UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(windowType);
                    if (windows != null && windows.Length > 0) windowInstance = windows[0];
                }

                if (windowInstance == null) return alerts;

                FieldInfo builderField = windowType.GetField("_selectedBuilder", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object selectedBuilder = builderField?.GetValue(windowInstance);

                if (selectedBuilder == null) return alerts;

                ExtractIssuesFromDict(selectedBuilder, "GUIErrors", AlertSeverity.Error, alerts);
                ExtractIssuesFromDict(selectedBuilder, "GUIWarnings", AlertSeverity.Warning, alerts);
                ExtractIssuesFromDict(selectedBuilder, "GUIInfos", AlertSeverity.Info, alerts);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AvatarSDKEvaluator] Failed to reflect VRChat SDK Builder alerts: {ex.Message}");
            }
            return alerts;
        }

        private static void ExtractIssuesFromDict(object selectedBuilder, string dictFieldName, AlertSeverity severity, List<SDKAlert> alerts)
        {
            Type builderType = selectedBuilder.GetType();
            FieldInfo dictField = builderType.GetField(dictFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (dictField == null) return;

            object dictObj = dictField.GetValue(selectedBuilder);
            if (dictObj is System.Collections.IDictionary dict)
            {
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    UnityEngine.Object targetObj = entry.Key as UnityEngine.Object;
                    if (entry.Value is System.Collections.IEnumerable issueList)
                    {
                        foreach (object issue in issueList)
                        {
                            if (issue == null) continue;
                            Type issueType = issue.GetType();

                            string text = issueType.GetField("IssueText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(issue) as string;
                            System.Action showAct = issueType.GetField("ShowAction", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(issue) as System.Action;
                            System.Action fixAct = issueType.GetField("FixAction", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(issue) as System.Action;

                            alerts.Add(new SDKAlert
                            {
                                Severity = severity,
                                Category = severity.ToString(),
                                Message = text,
                                TargetObject = targetObj,
                                SelectAction = showAct,
                                AutoFixAction = fixAct
                            });
                        }
                    }
                }
            }
        }

        private static string DetermineRating(AvatarStats stats, bool isMobile = false)
        {
            if (isMobile)
            {
                if (stats.TriangleCount <= 7500 && stats.MaterialSlotCount <= 1 && stats.PhysBoneComponentCount == 0 && stats.ContactCount <= 2)
                    return "Excellent";
                if (stats.TriangleCount <= 10000 && stats.MaterialSlotCount <= 1 && stats.PhysBoneComponentCount <= 4 && stats.ContactCount <= 4)
                    return "Good";
                if (stats.TriangleCount <= 15000 && stats.MaterialSlotCount <= 2 && stats.PhysBoneComponentCount <= 6 && stats.ContactCount <= 8)
                    return "Medium";
                if (stats.TriangleCount <= 20000 && stats.MaterialSlotCount <= 4 && stats.PhysBoneComponentCount <= 8 && stats.ContactCount <= 16)
                    return "Poor";
                return "Very Poor";
            }
            else
            {
                if (stats.TriangleCount <= 32000 && stats.MaterialSlotCount <= 4 && stats.PhysBoneComponentCount <= 4 && stats.ContactCount <= 8 && stats.TotalTextureMemoryBytes <= 40 * 1024 * 1024L /* 40 MB */)
                    return "Excellent";
                if (stats.TriangleCount <= 70000 && stats.MaterialSlotCount <= 8 && stats.PhysBoneComponentCount <= 8 && stats.ContactCount <= 16 && stats.TotalTextureMemoryBytes <= 75 * 1024 * 1024L /* 75 MB */)
                    return "Good";
                if (stats.TriangleCount <= 70000 && stats.MaterialSlotCount <= 16 && stats.PhysBoneComponentCount <= 16 && stats.ContactCount <= 24 && stats.TotalTextureMemoryBytes <= 110 * 1024 * 1024L /* 110 MB */)
                    return "Medium";
                if (stats.TriangleCount <= 70000 && stats.MaterialSlotCount <= 32 && stats.PhysBoneComponentCount <= 32 && stats.ContactCount <= 32 && stats.TotalTextureMemoryBytes <= 150 * 1024 * 1024L /* 150 MB */)
                    return "Poor";
                return "Very Poor";
            }
        }

        /// <summary>
        /// Retrieves all active VRChat SDK validation alerts and performance issues for an avatar.
        /// </summary>
        public static System.Collections.Generic.List<SDKAlert> GetSDKAlerts(GameObject avatarRoot)
        {
            var alerts = GetSDKAlertsFromVRCSDK(avatarRoot);
            if (alerts.Count > 0) return alerts;

            if (avatarRoot == null) return alerts;

            AvatarStats stats = EvaluateAvatar(avatarRoot);
            bool isMobile = VRCAvatarHelper.IsMobilePlatformActive();

            if (isMobile)
            {
                if (stats.TriangleCount > 20000)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.Error, Category = "Polygon Count", Message = $"Polygons ({stats.TriangleCount:N0}) exceed Mobile Very Poor limit of 20,000.", TargetObject = avatarRoot });
                if (stats.MaterialSlotCount > 4)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.Error, Category = "Material Slots", Message = $"Material Slots ({stats.MaterialSlotCount}) exceed Mobile Very Poor limit of 4.", TargetObject = avatarRoot });
                if (stats.PhysBoneComponentCount > 8)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.BlockingError, Category = "PhysBones", Message = $"PhysBone Components ({stats.PhysBoneComponentCount}) exceed Mobile hard cap of 8.", TargetObject = avatarRoot });
                if (stats.PhysBoneTransformCount > 64)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.BlockingError, Category = "PhysBones", Message = $"PhysBone Transforms ({stats.PhysBoneTransformCount}) exceed Mobile hard cap of 64.", TargetObject = avatarRoot });
                if (stats.PhysBoneColliderCount > 16)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.BlockingError, Category = "PhysBones", Message = $"PhysBone Colliders ({stats.PhysBoneColliderCount}) exceed Mobile hard cap of 16.", TargetObject = avatarRoot });
                if (stats.PhysBoneCollisionCheckCount > 64)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.BlockingError, Category = "PhysBones", Message = $"PhysBone Collision Checks ({stats.PhysBoneCollisionCheckCount}) exceed Mobile hard cap of 64.", TargetObject = avatarRoot });
                if (stats.ContactCount > 16)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.BlockingError, Category = "Contacts", Message = $"Contacts ({stats.ContactCount}) exceed Mobile hard cap of 16.", TargetObject = avatarRoot });
                if (stats.ConstraintCount > 150)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.BlockingError, Category = "Constraints", Message = $"Constraints ({stats.ConstraintCount}) exceed Mobile hard cap of 150.", TargetObject = avatarRoot });
                if (stats.ClothCount > 0)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.BlockingError, Category = "Cloth", Message = "Cloth components are completely disallowed on Mobile.", TargetObject = avatarRoot });
                if (stats.LightCount > 0)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.BlockingError, Category = "Lights", Message = "Lights are completely disallowed on Mobile.", TargetObject = avatarRoot });
                if (stats.AudioSourceCount > 0)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.BlockingError, Category = "Audio", Message = "AudioSources are completely disallowed on Mobile.", TargetObject = avatarRoot });
            }
            else
            {
                if (stats.TriangleCount > 70000)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.Warning, Category = "Polygon Count", Message = $"Polygons ({stats.TriangleCount:N0}) exceed PC Poor limit of 70,000 (Avatar rated Very Poor).", TargetObject = avatarRoot });
                if (stats.MaterialSlotCount > 32)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.Warning, Category = "Material Slots", Message = $"Material Slots ({stats.MaterialSlotCount}) exceed PC Poor limit of 32.", TargetObject = avatarRoot });
                if (stats.TotalTextureMemoryBytes > 150 * 1024 * 1024L /* 150 MB */)
                    alerts.Add(new SDKAlert { Severity = AlertSeverity.Warning, Category = "VRAM", Message = $"Texture Memory ({stats.TotalTextureMemoryBytes / (1024.0 * 1024.0):F1} MB) exceeds PC Poor limit of 150 MB.", TargetObject = avatarRoot });
            }

            return alerts;
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

        public const int MAX_BUNDLE_BUILD_TIMEOUT_SECONDS = 120;

        /// <summary>
        /// Invokes a VRChat SDK dry-run build and returns the size of the resulting .vrca bundle in bytes.
        /// Throws InvalidOperationException if the bundle could not be built — callers must handle this explicitly.
        /// </summary>
        public static long BuildAvatarAssetBundle(GameObject avatarRoot, out string bundlePath, Action<string> progressCallback = null)
        {
            bundlePath = null;
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot), "[AvatarSDKEvaluator] BuildAvatarAssetBundle: avatarRoot is null.");
            DateTime buildStartTime = DateTime.Now.AddSeconds(-2);

            // Preferred: drive the SDK's synchronous exporter directly (no SDK panel, no async
            // orchestration, no main-thread deadlock). Falls back to the panel's async Build()
            // machinery only when the exporter API can't be resolved.
            try
            {
                return BuildAvatarAssetBundleSync(avatarRoot, out bundlePath, progressCallback, buildStartTime);
            }
            catch (MissingMemberException mm)
            {
                Debug.LogWarning($"[AvatarSDKEvaluator] Synchronous SDK exporter unavailable ({mm.Message}) — falling back to async panel Build().");
            }

            try
            {
                Type builderType = Type.GetType("VRC.SDK3A.Editor.VRCSdkControlPanelAvatarBuilder, VRC.SDK3A.Editor")
                    ?? AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } }).FirstOrDefault(t => t.FullName == "VRC.SDK3A.Editor.VRCSdkControlPanelAvatarBuilder");

                if (builderType == null)
                    throw new InvalidOperationException("[AvatarSDKEvaluator] VRChat SDK builder type 'VRCSdkControlPanelAvatarBuilder' could not be located. Ensure the VRChat Avatars SDK package is installed.");

                // The SDK's Build() throws BuilderException("Open the SDK panel...") unless the builder's
                // _builder field holds the (open) VRCSdkControlPanel window. Acquire the panel's own
                // registered avatar builder via the public TryGetBuilder<T> API, opening the panel if needed.
                object builderInstance = AcquireRegisteredAvatarBuilder(builderType);
                if (builderInstance == null)
                    throw new InvalidOperationException("[AvatarSDKEvaluator] Could not acquire a VRChat SDK avatar builder registered with the SDK Control Panel. Open 'VRChat SDK > Show Control Panel' once and retry.");

                builderType = builderInstance.GetType();
                MethodInfo buildMethod = builderType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == "Build" && m.GetParameters().Length == 3);

                if (buildMethod == null)
                    throw new InvalidOperationException("[AvatarSDKEvaluator] Could not find 'Build(GameObject, bool, List<Option>)' method on VRCSdkControlPanelAvatarBuilder. SDK API may have changed.");

                // Register live VRCSdkControlPanelAvatarBuilder instance events for detailed console feedback.
                // The SDK's error event is also the abort signal for the wait loop below: the async Build()
                // task's fault continuation can't run while we block the main thread, so waiting for
                // task.IsFaulted after an error would just burn the full timeout.
                string sdkBuildError = null;
                RegisterBuildCallbacks(
                    builderInstance,
                    onProgress: (status) => Debug.Log($"[VRChat SDK Live] Build Progress: {status}"),
                    onError:    (err) => { Debug.LogError($"[VRChat SDK Live] Build Error: {err}"); sdkBuildError = err; },
                    onSuccess:  (path) => Debug.Log($"[VRChat SDK Live] Build Success: {path}")
                );

                // Pass testAvatar based on SDK PlatformSupportsBuildAndTest()
                bool isTestAvatar = PlatformSupportsBuildAndTest(builderInstance, builderType);
                object taskObj = buildMethod.Invoke(builderInstance, new object[] { avatarRoot, isTestAvatar, null });

                if (taskObj is Task task)
                {
                    double startWait = UnityEditor.EditorApplication.timeSinceStartup;

                    // The SDK's async Build() posts its continuations (e.g. after 'await Task.Delay')
                    // to Unity's SynchronizationContext, which only runs when the main thread idles.
                    // Since this wait loop BLOCKS the main thread, the build cannot proceed unless we
                    // pump the context ourselves — otherwise Build() stalls at its first await, this
                    // loop times out, and the queued build runs *after* the conversion finishes.
                    var syncContext = System.Threading.SynchronizationContext.Current;
                    MethodInfo syncExec = null;
                    object pumpTarget = null;
                    if (syncContext != null)
                    {
                        // Walk the type hierarchy — private 'Exec' lives on UnitySynchronizationContext,
                        // but Current may be a derived/wrapped context.
                        for (Type t = syncContext.GetType(); t != null && syncExec == null; t = t.BaseType)
                            syncExec = t.GetMethod("Exec", BindingFlags.NonPublic | BindingFlags.Instance);
                        pumpTarget = syncContext;
                    }

                    // Fallback: static pump entry points on UnityEngine.UnitySynchronizationContext
                    MethodInfo staticPump = null;
                    object[] staticPumpArgs = null;
                    if (syncExec == null)
                    {
                        Type unityCtxType = typeof(UnityEngine.Object).Assembly.GetType("UnityEngine.UnitySynchronizationContext");
                        MethodInfo execPending = unityCtxType?.GetMethod("ExecutePendingTasks", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                        MethodInfo execTasks = unityCtxType?.GetMethod("ExecuteTasks", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                        if (execPending != null)
                        {
                            staticPump = execPending;
                            staticPumpArgs = execPending.GetParameters().Length == 1 ? new object[] { 10L } : null;
                        }
                        else if (execTasks != null)
                        {
                            staticPump = execTasks;
                        }
                        Debug.LogWarning($"[AvatarSDKEvaluator] Instance sync-context pump unavailable (Current = {(syncContext == null ? "null" : syncContext.GetType().FullName)}). " +
                                         $"Falling back to {(staticPump != null ? $"UnitySynchronizationContext.{staticPump.Name}" : "no pump — async SDK build continuations may not run until the editor idles")}.");
                    }

                    try
                    {
                        while (!task.IsCompleted && !task.IsFaulted && !task.IsCanceled)
                        {
                            // Abort immediately when the SDK reported a build error — the task itself
                            // will never fault while the main thread is blocked here.
                            if (sdkBuildError != null)
                                throw new InvalidOperationException($"[AvatarSDKEvaluator] SDK build failed: {sdkBuildError}");

                            double elapsed = UnityEditor.EditorApplication.timeSinceStartup - startWait;
                            int elapsedSec = (int)elapsed;
                            progressCallback?.Invoke($"Building VRChat AssetBundle dry-run... (elapsed {elapsedSec}s / up to {MAX_BUNDLE_BUILD_TIMEOUT_SECONDS}s)");

                            // Pump queued async continuations so the SDK build actually advances
                            try
                            {
                                if (syncExec != null) syncExec.Invoke(pumpTarget, null);
                                else staticPump?.Invoke(null, staticPumpArgs);
                            }
                            catch (Exception pumpEx) { Debug.LogWarning($"[AvatarSDKEvaluator] Sync context pump threw: {pumpEx.InnerException?.Message ?? pumpEx.Message}"); }

                            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                            System.Threading.Thread.Sleep(5);

                            // Early success exit if the .vrca bundle file has already been written to temp cache
                            // (verbose: false — this polls every few ms and would otherwise flood Editor.log)
                            if (GetBuiltBundleSize(out string earlyPath, buildStartTime, verbose: false) > 0)
                            {
                                Debug.Log($"[AvatarSDKEvaluator] Detected generated .vrca AssetBundle on disk early during build loop: '{earlyPath}'");
                                break;
                            }

                            if (elapsed > MAX_BUNDLE_BUILD_TIMEOUT_SECONDS)
                            {
                                Debug.LogError($"[AvatarSDKEvaluator] ⚠️ CRITICAL: Dry-run AssetBundle build timed out after {MAX_BUNDLE_BUILD_TIMEOUT_SECONDS} seconds for '{avatarRoot.name}'.");
                                break;
                            }
                        }

                        if (task.IsFaulted)
                            throw new InvalidOperationException($"[AvatarSDKEvaluator] SDK build task faulted: {task.Exception?.GetBaseException()?.Message}");
                    }
                    finally
                    {
                    }
                }
            }
            catch (InvalidOperationException)
            {
                throw; // Re-throw critical setup/build failures
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"[AvatarSDKEvaluator] ⚠️ CRITICAL: Dry-run AssetBundle build failed unexpectedly for '{avatarRoot.name}': {e.Message}", e);
            }

            long size = GetBuiltBundleSize(out bundlePath, buildStartTime);
            if (size <= 0)
                throw new InvalidOperationException($"[AvatarSDKEvaluator] ⚠️ CRITICAL: AssetBundle dry-run completed but no valid .vrca file was found in Unity's temp cache for '{avatarRoot.name}'. The SDK build may have been suppressed by VRCFury or another hook, or the output was not written to the expected location ('{Application.temporaryCachePath}').");

            return size;
        }

        /// <summary>
        /// Synchronous dry-run build: replicates the essential steps of
        /// VRCSdkControlPanelAvatarBuilder.Build() but drives VRC_SdkBuilder.RunExportAvatarBlueprint
        /// directly. The export itself is synchronous — the SDK's async Build() only wraps it in
        /// orchestration (delays + TaskCompletionSources) that deadlocks against a blocked main thread.
        /// Throws MissingMemberException when the SDK exporter API cannot be resolved (caller falls back).
        /// </summary>
        private static long BuildAvatarAssetBundleSync(GameObject avatarRoot, out string bundlePath, Action<string> progressCallback, DateTime buildStartTime)
        {
            bundlePath = null;

            Type sdkBuilderType = FindSdkType("VRC.SDKBase.Editor.VRC_SdkBuilder");
            Type callbacksType = FindSdkType("VRC.SDKBase.Editor.BuildPipeline.VRCBuildPipelineCallbacks");
            Type requestedBuildType = FindSdkType("VRC.SDKBase.Editor.BuildPipeline.VRCSDKRequestedBuildType");
            Type avatarBuilderInterface = FindSdkType("VRC.SDKBase.Editor.ISDKAvatarBuilder");

            MethodInfo runExport = sdkBuilderType?.GetMethod("RunExportAvatarBlueprint", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (sdkBuilderType == null || callbacksType == null || requestedBuildType == null || avatarBuilderInterface == null || runExport == null)
                throw new MissingMemberException("VRC_SdkBuilder.RunExportAvatarBlueprint or its supporting SDK types were not found");

            Debug.Log($"[AvatarSDKEvaluator] Using synchronous SDK exporter (VRC_SdkBuilder.RunExportAvatarBlueprint) for '{avatarRoot.name}'.");
            progressCallback?.Invoke("Running VRChat SDK export (synchronous)...");

            // 1. Fire the SDK build-requested gate (build hooks like VRCFury can veto/prepare here)
            object avatarBuildType = Enum.Parse(requestedBuildType, "Avatar");
            MethodInfo onBuildRequested = callbacksType.GetMethod("OnVRCSDKBuildRequested", BindingFlags.Public | BindingFlags.Static);
            if (onBuildRequested != null && onBuildRequested.Invoke(null, new[] { avatarBuildType }) is bool allowed && !allowed)
                throw new InvalidOperationException("[AvatarSDKEvaluator] Build was blocked by an SDK build-requested callback.");

            // 2. Shader stripping pref, same as the SDK's Build()
            BuildTarget activeTarget = EditorUserBuildSettings.activeBuildTarget;
            EditorPrefs.SetBool("VRC.SDKBase_StripAllShaders", activeTarget == BuildTarget.Android || activeTarget == BuildTarget.iOS);

            // 3. Ensure the avatar's PipelineManager has an id (the exporter derives cache paths from it)
            try
            {
                Component pm = avatarRoot.GetComponent("PipelineManager");
                if (pm != null)
                {
                    FieldInfo idField = pm.GetType().GetField("blueprintId", BindingFlags.Public | BindingFlags.Instance);
                    if (idField != null && string.IsNullOrWhiteSpace(idField.GetValue(pm) as string))
                    {
                        MethodInfo assignId = pm.GetType().GetMethod("AssignId", BindingFlags.Public | BindingFlags.Instance);
                        Type contentTypeEnum = pm.GetType().GetNestedType("ContentType");
                        if (assignId != null && contentTypeEnum != null)
                        {
                            assignId.Invoke(pm, new[] { Enum.Parse(contentTypeEnum, "avatar") });
                            Debug.Log("[AvatarSDKEvaluator] Assigned temporary blueprint id for dry-run export.");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarSDKEvaluator] Could not verify/assign PipelineManager blueprint id: {e.Message}");
            }

            // 4. Configure the static builder exactly like the SDK panel does
            sdkBuilderType.GetField("shouldBuildUnityPackage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, false);
            MethodInfo setCurrentBuilder = sdkBuilderType.GetMethod("SetCurrentBuilder", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            setCurrentBuilder?.MakeGenericMethod(avatarBuilderInterface).Invoke(null, null);

            MethodInfo clearCallbacks = sdkBuilderType.GetMethod("ClearCallbacks", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            clearCallbacks?.Invoke(null, null);

            // 5. Capture success/error through the static builder callbacks
            string builtPath = null;
            string buildError = null;
            RegisterStaticCallback(sdkBuilderType, "RegisterBuildProgressCallback", (s, msg) => { Debug.Log($"[VRChat SDK Sync] Build Progress: {msg}"); progressCallback?.Invoke(msg); });
            RegisterStaticCallback(sdkBuilderType, "RegisterBuildErrorCallback", (s, err) => { Debug.LogError($"[VRChat SDK Sync] Build Error: {err}"); buildError = err; });
            RegisterStaticCallback(sdkBuilderType, "RegisterBuildSuccessCallback", (s, path) => { Debug.Log($"[VRChat SDK Sync] Build Success: {path}"); builtPath = path; });

            try
            {
                // 6. The actual synchronous export
                runExport.Invoke(null, new object[] { avatarRoot });
            }
            catch (TargetInvocationException tie)
            {
                throw new InvalidOperationException($"[AvatarSDKEvaluator] SDK export threw: {tie.InnerException?.Message ?? tie.Message}", tie.InnerException ?? tie);
            }
            finally
            {
                clearCallbacks?.Invoke(null, null);
            }

            if (buildError != null)
                throw new InvalidOperationException($"[AvatarSDKEvaluator] SDK export failed: {buildError}");

            if (!string.IsNullOrEmpty(builtPath) && File.Exists(builtPath))
            {
                bundlePath = builtPath;
                long size = new FileInfo(builtPath).Length;
                Debug.Log($"[AvatarSDKEvaluator] Synchronous dry-run build complete: '{builtPath}' ({size / (1024.0 * 1024.0):F2} MB)");
                return size;
            }

            // Success callback didn't fire with a usable path — fall back to scanning the temp cache
            long scanned = GetBuiltBundleSize(out bundlePath, buildStartTime);
            if (scanned <= 0)
                throw new InvalidOperationException($"[AvatarSDKEvaluator] ⚠️ CRITICAL: Synchronous export completed but no .vrca bundle was found for '{avatarRoot.name}'.");
            return scanned;
        }

        private static Type FindSdkType(string fullName)
        {
            return Type.GetType($"{fullName}, VRCSDKBase-Editor")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                    .FirstOrDefault(t => t.FullName == fullName);
        }

        /// <summary>
        /// Subscribes an (object, string) handler to a VRC_SdkBuilder.RegisterXCallback method,
        /// adapting to whatever delegate type the SDK expects.
        /// </summary>
        private static void RegisterStaticCallback(Type sdkBuilderType, string registerMethodName, Action<object, string> handler)
        {
            try
            {
                MethodInfo register = sdkBuilderType.GetMethod(registerMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (register == null) return;

                Type delType = register.GetParameters()[0].ParameterType;
                MethodInfo invoke = delType.GetMethod("Invoke");
                var pars = invoke.GetParameters();
                if (pars.Length == 2 && pars[1].ParameterType == typeof(string))
                {
                    Delegate del = Delegate.CreateDelegate(delType, handler.Target, handler.Method);
                    register.Invoke(null, new object[] { del });
                }
                else
                {
                    Debug.LogWarning($"[AvatarSDKEvaluator] {registerMethodName} has unexpected delegate shape ({delType.Name}) — skipping.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarSDKEvaluator] Could not subscribe {registerMethodName}: {e.Message}");
            }
        }

        /// <summary>
        /// Returns an avatar builder instance that is registered with the VRChat SDK Control Panel
        /// (required for VRCSdkControlPanelAvatarBuilder.Build() to run). Opens the panel if needed.
        /// Returns null if no registered builder could be acquired.
        /// </summary>
        private static object AcquireRegisteredAvatarBuilder(Type builderType)
        {
            try
            {
                Type panelType = Type.GetType("VRCSdkControlPanel, VRC.SDKBase.Editor")
                    ?? AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } }).FirstOrDefault(t => t.Name == "VRCSdkControlPanel" && typeof(UnityEditor.EditorWindow).IsAssignableFrom(t));
                if (panelType == null) return null;

                FieldInfo windowField = panelType.GetField("window", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object panelWindow = windowField?.GetValue(null);
                if (panelWindow == null)
                {
                    Debug.Log("[AvatarSDKEvaluator] VRChat SDK Control Panel is not open — opening it (the SDK build pipeline requires it)...");
                    MethodInfo showMethod = panelType.GetMethod("ShowControlPanel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    showMethod?.Invoke(null, null);
                    panelWindow = windowField?.GetValue(null);
                }
                if (panelWindow == null) return null;

                // Preferred: the panel's own registered builder via public static TryGetBuilder<IVRCSdkAvatarBuilderApi>()
                Type builderApiType = Type.GetType("VRC.SDK3A.Editor.IVRCSdkAvatarBuilderApi, VRC.SDK3A.Editor")
                    ?? AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } }).FirstOrDefault(t => t.FullName == "VRC.SDK3A.Editor.IVRCSdkAvatarBuilderApi");
                MethodInfo tryGetBuilder = panelType.GetMethod("TryGetBuilder", BindingFlags.Public | BindingFlags.Static);
                if (builderApiType != null && tryGetBuilder != null)
                {
                    object[] args = new object[] { null };
                    if (tryGetBuilder.MakeGenericMethod(builderApiType).Invoke(null, args) is bool found && found && args[0] != null)
                    {
                        Debug.Log($"[AvatarSDKEvaluator] Acquired panel-registered avatar builder: {args[0].GetType().Name}");
                        return args[0];
                    }
                }

                // Fallback: create our own instance and register it with the panel window
                object instance = Activator.CreateInstance(builderType);
                MethodInfo registerMethod = builderType.GetMethod("RegisterBuilder", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (registerMethod != null)
                {
                    registerMethod.Invoke(instance, new object[] { panelWindow });
                    Debug.Log("[AvatarSDKEvaluator] Created avatar builder instance and registered it with the SDK Control Panel.");
                    return instance;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarSDKEvaluator] Failed to acquire panel-registered avatar builder: {e.Message}");
            }
            return null;
        }

        public static long GetBuiltBundleSize(out string bundlePath, DateTime minCreationTime, bool verbose = true)
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
                                    newestBundle = f;
                            }
                        }

                        if (newestBundle != null)
                        {
                            bundlePath = newestBundle.FullName;
                            if (verbose) Debug.Log($"[AvatarSDKEvaluator] Dry-run AssetBundle built successfully: '{bundlePath}' ({newestBundle.Length / (1024.0 * 1024.0):F2} MB)");
                            return newestBundle.Length;
                        }

                        // Bundles exist but none are newer than buildStartTime
                        if (verbose) Debug.LogWarning($"[AvatarSDKEvaluator] {files.Length} .vrca file(s) found in temp cache but none were written after {minCreationTime:HH:mm:ss}. The build may have been suppressed or cached.");
                    }
                    else
                    {
                        if (verbose) Debug.LogWarning($"[AvatarSDKEvaluator] No .vrca files found in Unity temp cache at '{cachePath}'. Build may not have produced output.");
                    }
                }
                else
                {
                    if (verbose) Debug.LogWarning($"[AvatarSDKEvaluator] Unity temp cache directory does not exist: '{cachePath}'.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarSDKEvaluator] Error reading temp cache for bundle size: {e.Message}");
            }

            return -1;
        }

        /// <summary>
        /// Registers custom callbacks with VRChat SDK's VRCSdkControlPanelAvatarBuilder via reflection.
        /// Allows other packages in dev.bluscream to hook into VRChat SDK build events cleanly without overwriting internal delegates.
        /// </summary>
        public static bool RegisterBuildCallbacks(object builderInstance, Action<string> onProgress = null, Action<string> onError = null, Action<string> onSuccess = null)
        {
            if (builderInstance == null) return false;

            try
            {
                Type builderType = builderInstance.GetType();

                // The panel's avatar builder is a long-lived instance: unhook any handlers we added on a
                // previous call first, otherwise every build re-subscribes and events fire multiple times.
                UnregisterPreviousBuildCallbacks();

                if (onProgress != null)
                {
                    EventInfo evtProgress = builderType.GetEvent("OnSdkBuildProgress", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (evtProgress != null)
                    {
                        EventHandler<string> handler = (s, msg) => onProgress.Invoke(msg);
                        evtProgress.AddEventHandler(builderInstance, handler);
                        _registeredBuildHandlers.Add((evtProgress, builderInstance, handler));
                    }
                }

                if (onError != null)
                {
                    EventInfo evtError = builderType.GetEvent("OnSdkBuildError", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (evtError != null)
                    {
                        EventHandler<string> handler = (s, err) => onError.Invoke(err);
                        evtError.AddEventHandler(builderInstance, handler);
                        _registeredBuildHandlers.Add((evtError, builderInstance, handler));
                    }
                }

                if (onSuccess != null)
                {
                    EventInfo evtSuccess = builderType.GetEvent("OnSdkBuildSuccess", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (evtSuccess != null)
                    {
                        EventHandler<string> handler = (s, path) => onSuccess.Invoke(path);
                        evtSuccess.AddEventHandler(builderInstance, handler);
                        _registeredBuildHandlers.Add((evtSuccess, builderInstance, handler));
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AvatarSDKEvaluator] Could not register VRCSdkControlPanelAvatarBuilder event handlers: {ex.Message}");
                return false;
            }
        }

        private static readonly List<(EventInfo evt, object target, Delegate handler)> _registeredBuildHandlers = new List<(EventInfo, object, Delegate)>();

        private static void UnregisterPreviousBuildCallbacks()
        {
            foreach (var (evt, target, handler) in _registeredBuildHandlers)
            {
                try { evt.RemoveEventHandler(target, handler); } catch { /* target may be gone */ }
            }
            _registeredBuildHandlers.Clear();
        }

        /// <summary>
        /// Checks if local "Build & Test" mode is supported on the active build target platform (e.g. PC = true, Android/iOS = false).
        /// </summary>
        public static bool PlatformSupportsBuildAndTest(object builderInstance = null, Type builderType = null)
        {
            // VRChat SDK only supports local Build & Test on Standalone Windows platform targets
            BuildTarget activeTarget = EditorUserBuildSettings.activeBuildTarget;
            if (activeTarget != BuildTarget.StandaloneWindows64 && activeTarget != BuildTarget.StandaloneWindows)
            {
                return false;
            }

            try
            {
                if (builderType == null)
                {
                    builderType = Type.GetType("VRC.SDK3A.Editor.VRCSdkControlPanelAvatarBuilder, com.vrchat.avatars.Editor")
                        ?? AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } }).FirstOrDefault(t => t.FullName == "VRC.SDK3A.Editor.VRCSdkControlPanelAvatarBuilder");
                }

                if (builderType != null)
                {
                    MethodInfo supportsTestParam = builderType.GetMethod("PlatformSupportsBuildAndTest", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (supportsTestParam != null)
                    {
                        if (builderInstance == null) builderInstance = Activator.CreateInstance(builderType);
                        object result = supportsTestParam.Invoke(builderInstance, null);
                        if (result is bool supports) return supports;
                    }
                }
            }
            catch { }

            return true;
        }
    }
}
