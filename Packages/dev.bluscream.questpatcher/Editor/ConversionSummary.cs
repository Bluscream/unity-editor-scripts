using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRCQuestPatcher
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

        /// <summary>
        /// Renders the summary UI in the editor window
        /// </summary>
        public void RenderGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Conversion Summary", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Statistics
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Statistics", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Materials Replaced: {materialsReplaced}");
            EditorGUILayout.LabelField($"Materials Skipped (already compatible): {materialsSkipped}");
            EditorGUILayout.LabelField($"Materials Failed: {materialsFailed}");
            EditorGUILayout.LabelField($"Components Removed: {componentsRemoved}");
            EditorGUILayout.LabelField($"Textures Optimized: {texturesOptimized}");
            EditorGUILayout.LabelField($"GPU Instancing Enabled: {gpuInstancingEnabled}");
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Successes
            if (successes.Count > 0)
            {
                EditorGUILayout.LabelField($"Successes ({successes.Count})", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                foreach (var item in successes)
                {
                    RenderSummaryItem(item, Color.green);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }

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
