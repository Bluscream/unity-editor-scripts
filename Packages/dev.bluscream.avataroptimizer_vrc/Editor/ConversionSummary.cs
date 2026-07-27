using Bluscream.VRC;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Data structure and UI for conversion summary
    /// </summary>
    [System.Serializable]
    public class ConversionSummary
    {
        public int materialsReplaced = 0;
        public int materialsSkipped = 0;
        public int materialsFailed = 0;
        public int componentsRemoved = 0;
        public int texturesOptimized = 0;
        public int gpuInstancingEnabled = 0;

        public List<SummaryItem> successes = new List<SummaryItem>();
        public List<SummaryItem> errors = new List<SummaryItem>();
        public List<SummaryItem> warnings = new List<SummaryItem>();

        [System.Serializable]
        public class SummaryItem
        {
            public string message;
            public UnityEngine.Object targetObject;
            public string objectPath;
            public Action onClickAction;

            public SummaryItem(string msg, UnityEngine.Object obj = null, string path = null)
            {
                message = msg;
                targetObject = obj;
                objectPath = path;
            }
        }

        public long CompressedAvatarSizeBytes = -1;

        public AvatarSDKEvaluator.AvatarStats InitialStats;
        public AvatarSDKEvaluator.AvatarStats FinalStats;

        /// <summary>
        /// Renders the summary UI in the editor window
        /// </summary>
        public void RenderGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Patch Results Summary", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Metrics Comparison Table
            if (InitialStats != null && FinalStats != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Category", "Original (PC)  →  Patched (Quest)", EditorStyles.boldLabel);
                EditorGUILayout.Space(3);

                RenderMetricRow("Performance Rating", InitialStats.RatingName, FinalStats.RatingName);
                RenderMetricRow("Triangles", $"{InitialStats.TriangleCount:N0}", $"{FinalStats.TriangleCount:N0}");
                RenderMetricRow("Texture Memory (VRAM)", $"{InitialStats.TotalTextureMemoryBytes / (1024.0 * 1024.0):F2} MB", $"{FinalStats.TotalTextureMemoryBytes / (1024.0 * 1024.0):F2} MB");
                if (CompressedAvatarSizeBytes > 0)
                {
                    RenderMetricRow("Compressed Avatar Size (Disk)", "N/A", $"{CompressedAvatarSizeBytes / (1024.0 * 1024.0):F2} MB / 10.00 MB");
                }
                RenderMetricRow("Material Slots", $"{InitialStats.MaterialSlotCount}", $"{FinalStats.MaterialSlotCount}");
                RenderMetricRow("PhysBone Components", $"{InitialStats.PhysBoneComponentCount}", $"{FinalStats.PhysBoneComponentCount}");
                RenderMetricRow("PhysBone Colliders", $"{InitialStats.PhysBoneColliderCount}", $"{FinalStats.PhysBoneColliderCount}");
                RenderMetricRow("PhysBone Collision Checks", $"{InitialStats.PhysBoneCollisionCheckCount}", $"{FinalStats.PhysBoneCollisionCheckCount}");
                RenderMetricRow("Contacts", $"{InitialStats.ContactCount}", $"{FinalStats.ContactCount}");
                RenderMetricRow("Constraints", $"{InitialStats.ConstraintCount}", $"{FinalStats.ConstraintCount}");
                RenderMetricRow("Particle Systems", $"{InitialStats.ParticleSystemCount}", $"{FinalStats.ParticleSystemCount}");
                RenderMetricRow("Active Particles", $"{InitialStats.ActiveParticleCount}", $"{FinalStats.ActiveParticleCount}");
                RenderMetricRow("Trail / Line Renderers", $"{InitialStats.TrailRendererCount} / {InitialStats.LineRendererCount}", $"{FinalStats.TrailRendererCount} / {FinalStats.LineRendererCount}");
                RenderMetricRow("Cloth Components", $"{InitialStats.ClothCount}", $"{FinalStats.ClothCount}");
                RenderMetricRow("Skinned Meshes", $"{InitialStats.SkinnedMeshCount}", $"{FinalStats.SkinnedMeshCount}");
                RenderMetricRow("Mesh Renderers", $"{InitialStats.MeshRendererCount}", $"{FinalStats.MeshRendererCount}");

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField($"Pipeline Operations: {materialsReplaced} Mats Replaced, {texturesOptimized} Textures Compressed, {componentsRemoved} Incompatible Components Removed.", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(10);

            // Warnings
            if (warnings.Count > 0)
            {
                EditorGUILayout.LabelField($"Warnings ({warnings.Count})", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                foreach (var item in warnings)
                {
                    RenderSummaryItem(item, Color.yellow);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }

            // Errors
            if (errors.Count > 0)
            {
                EditorGUILayout.LabelField($"Errors ({errors.Count})", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                foreach (var item in errors)
                {
                    RenderSummaryItem(item, Color.red);
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void RenderMetricRow(string label, string oldVal, string newVal)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(170));
            EditorGUILayout.LabelField($"{oldVal}  →  {newVal}", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Prints comparison report to Console
        /// </summary>
        public void PrintConsoleSummary(string avatarName, PlatformProfile profile = null)
        {
            if (InitialStats == null || FinalStats == null) return;

            string triLimit = (profile != null && profile.MaxTriangles < int.MaxValue) ? $"/ {profile.MaxTriangles:N0}" : "/ Unlimited";
            string matLimit = (profile != null && profile.MaxMaterialSlots < int.MaxValue) ? $"/ {profile.MaxMaterialSlots}" : "/ Unlimited";
            string pbCompLimit = (profile != null && profile.MaxPhysBoneComponents < int.MaxValue) ? $"/ {profile.MaxPhysBoneComponents}" : "/ 8";
            string contactLimit = (profile != null && profile.MaxContacts < int.MaxValue) ? $"/ {profile.MaxContacts}" : "/ Unlimited";
            string constraintLimit = (profile != null && profile.MaxConstraints < int.MaxValue) ? $"/ {profile.MaxConstraints}" : "/ Unlimited";
            string smrLimit = (profile != null && profile.MaxSkinnedMeshes < int.MaxValue) ? $"/ {profile.MaxSkinnedMeshes}" : "/ Unlimited";

            Debug.Log($"<color=cyan><b>================================================================================</b></color>");
            Debug.Log($"<color=cyan><b>[VRC-AvatarOptimizer Summary] Target Platform Conversion Comparison for '{avatarName}':</b></color>");
            Debug.Log($"<color=cyan><b>--------------------------------------------------------------------------------</b></color>");
            Debug.Log($"[VRC-AvatarOptimizer Summary] • Performance Rating:        {InitialStats.RatingName}  →  {FinalStats.RatingName}");
            Debug.Log($"[VRC-AvatarOptimizer Summary] • Triangles:                 {InitialStats.TriangleCount:N0}  →  {FinalStats.TriangleCount:N0} {triLimit}");
            Debug.Log($"[VRC-AvatarOptimizer Summary] • Texture Memory (VRAM):     {InitialStats.TotalTextureMemoryBytes / (1024.0 * 1024.0):F2} MB  →  {FinalStats.TotalTextureMemoryBytes / (1024.0 * 1024.0):F2} MB / 40.00 MB");
            if (CompressedAvatarSizeBytes > 0)
            {
                Debug.Log($"[VRC-AvatarOptimizer Summary] • Compressed Avatar Size (Disk): {CompressedAvatarSizeBytes / (1024.0 * 1024.0):F2} MB / 10.00 MB");
            }
            Debug.Log($"[VRC-AvatarOptimizer Summary] • Material Slots:            {InitialStats.MaterialSlotCount}  →  {FinalStats.MaterialSlotCount} {matLimit}");
            Debug.Log($"[VRC-AvatarOptimizer Summary] • PhysBone Components:       {InitialStats.PhysBoneComponentCount}  →  {FinalStats.PhysBoneComponentCount} {pbCompLimit}");
            Debug.Log($"[VRC-AvatarOptimizer Summary] • Contacts:                  {InitialStats.ContactCount}  →  {FinalStats.ContactCount} {contactLimit}");
            Debug.Log($"[VRC-AvatarOptimizer Summary] • Constraints:               {InitialStats.ConstraintCount}  →  {FinalStats.ConstraintCount} {constraintLimit}");
            Debug.Log($"[VRC-AvatarOptimizer Summary] • Skinned Meshes:            {InitialStats.SkinnedMeshCount}  →  {FinalStats.SkinnedMeshCount} {smrLimit}");
            Debug.Log($"[VRC-AvatarOptimizer Summary] • Operations:                {materialsReplaced} Materials Replaced, {texturesOptimized} Textures Compressed, {componentsRemoved} Components Removed.");
            Debug.Log($"<color=cyan><b>================================================================================</b></color>");
        }

        private void RenderSummaryItem(SummaryItem item, UnityEngine.Color color)
        {
            EditorGUILayout.BeginHorizontal();

            // Color indicator
            Rect colorRect = GUILayoutUtility.GetRect(5, EditorGUIUtility.singleLineHeight, GUILayout.Width(5));
            EditorGUI.DrawRect(colorRect, color);

            // Message
            EditorGUILayout.LabelField(item.message, GUILayout.ExpandWidth(true));

            // Clickable object reference
            if (item.targetObject != null || !string.IsNullOrEmpty(item.objectPath))
            {
                if (GUILayout.Button("Select", GUILayout.Width(60)))
                {
                    if (item.targetObject != null)
                    {
                        Selection.activeObject = item.targetObject;
                        EditorGUIUtility.PingObject(item.targetObject);
                    }
                    else if (!string.IsNullOrEmpty(item.objectPath))
                    {
                        // Try to find the object by path
                        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.objectPath);
                        if (obj != null)
                        {
                            Selection.activeObject = obj;
                            EditorGUIUtility.PingObject(obj);
                        }
                        else
                        {
                            // Try to find GameObject in scene
                            GameObject go = GameObject.Find(item.objectPath);
                            if (go != null)
                            {
                                Selection.activeGameObject = go;
                                EditorGUIUtility.PingObject(go);
                            }
                        }
                    }

                    if (item.onClickAction != null)
                    {
                        item.onClickAction();
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        public void AddSuccess(string message, UnityEngine.Object obj = null, string path = null)
        {
            successes.Add(new SummaryItem(message, obj, path));
        }

        public void AddError(string message, UnityEngine.Object obj = null, string path = null)
        {
            errors.Add(new SummaryItem(message, obj, path));
        }

        public void AddWarning(string message, UnityEngine.Object obj = null, string path = null)
        {
            warnings.Add(new SummaryItem(message, obj, path));
        }

        public void Clear()
        {
            materialsReplaced = 0;
            materialsSkipped = 0;
            materialsFailed = 0;
            componentsRemoved = 0;
            texturesOptimized = 0;
            gpuInstancingEnabled = 0;
            successes.Clear();
            errors.Clear();
            warnings.Clear();
        }
    }
}
