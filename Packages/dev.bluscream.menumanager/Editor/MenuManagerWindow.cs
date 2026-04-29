using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Bluscream.MenuManager
{
    public class MenuManagerWindow : EditorWindow
    {
        [SerializeField] private GameObject avatarObject;
        [SerializeField] private VRCExpressionsMenu mergedMenu;
        [SerializeField] private TreeViewState treeViewState;
        
        private MenuTreeView treeView;
        private SerializedObject serializedObject;
        private SerializedProperty avatarObjectProperty;
        private SerializedProperty mergedMenuProperty;
        private Vector2 scrollPos;
        
        [MenuItem("Bluscream/Menu Manager/Open Menu Manager")]
        public static void ShowWindow()
        {
            var window = GetWindow<MenuManagerWindow>("Menu Manager");
            window.Show();
        }

        private void OnEnable()
        {
            if (treeViewState == null) treeViewState = new TreeViewState();
            serializedObject = new SerializedObject(this);
            avatarObjectProperty = serializedObject.FindProperty("avatarObject");
            mergedMenuProperty = serializedObject.FindProperty("mergedMenu");
        }

        private void OnGUI()
        {
            serializedObject.Update();
            
            try {
                EditorGUILayout.BeginVertical();
                GUILayout.Label("VRCFury Menu Manager", EditorStyles.boldLabel);
                
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.PropertyField(avatarObjectProperty, new GUIContent("Avatar Root"));
                    if (GUILayout.Button("Load", GUILayout.Width(60))) LoadMenu();
                }

                if (mergedMenu != null) {
                    EditorGUILayout.HelpBox($"Menu: {mergedMenu.name} ({mergedMenu.controls.Count} root controls)", MessageType.Info);
                    
                    if (treeView == null || treeView.GetMenu() != mergedMenu) {
                        InitializeTreeView();
                    }

                    // Toolbar
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                        if (GUILayout.Button("Expand All", EditorStyles.toolbarButton)) treeView?.ExpandAll();
                        if (GUILayout.Button("Collapse All", EditorStyles.toolbarButton)) treeView?.CollapseAll();
                        if (GUILayout.Button("Reload", EditorStyles.toolbarButton)) treeView?.Reload();
                        GUILayout.FlexibleSpace();
                    }

                    // TreeView Area
                    Rect rect = EditorGUILayout.GetControlRect(false, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
                    if (rect.height < 100) rect.height = position.height - 180; // Fallback if layout gives small rect
                    
                    if (treeView != null) {
                        treeView.OnGUI(rect);
                    }

                    GUILayout.Space(5);
                    using (new EditorGUILayout.HorizontalScope()) {
                        if (GUILayout.Button("Apply via VRCFury")) ApplyMoves();
                        if (GUILayout.Button("Export JSON")) ExportJson();
                        if (GUILayout.Button("Import JSON")) ImportJson();
                    }
                } else {
                    EditorGUILayout.HelpBox("Select an avatar and click Load to see the menu hierarchy.", MessageType.Info);
                }

                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
                    if (GUILayout.Button("Repaint", GUILayout.Width(60))) Repaint();
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(avatarObject != null ? $"Active: {avatarObject.name}" : "No Avatar", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndVertical();
            } catch (Exception ex) {
                if (Event.current.type != EventType.Layout) Debug.LogError($"UI Error: {ex}");
            }

            if (serializedObject.ApplyModifiedProperties()) {
                // If avatar changed, maybe clear menu?
                // mergedMenu = null; 
            }
        }

        private void LoadMenu()
        {
            if (avatarObject == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select an avatar root first.", "OK");
                return;
            }

            Debug.Log($"MenuManagerWindow: Starting LoadMenu for {avatarObject.name}...");
            
            var result = VRCFuryMenuHelper.GetMergedMenu(avatarObject);
            
            if (result == null)
            {
                Debug.LogError("MenuManagerWindow: Failed to extract merged menu.");
                ShowNotification(new GUIContent("Failed to load menu!"));
                return;
            }

            mergedMenu = result;
            // Ensure the SO is not destroyed by GC if it's transient
            mergedMenu.hideFlags = HideFlags.DontSave | HideFlags.DontUnloadUnusedAsset; 

            InitializeTreeView();
            
            Debug.Log("MenuManagerWindow: LoadMenu complete.");
            ShowNotification(new GUIContent("Menu Loaded!"));
            Repaint();
        }

        private void InitializeTreeView()
        {
            if (mergedMenu == null) return;
            if (treeViewState == null) treeViewState = new TreeViewState();
            treeView = new MenuTreeView(treeViewState, mergedMenu);
            treeView.Reload();
            treeView.ExpandAll();
            Debug.Log("MenuManagerWindow: TreeView initialized.");
        }

        private void ApplyMoves()
        {
            if (avatarObject == null || treeView == null) return;
            
            var moves = treeView.GetMoveOperations();
            if (moves.Count == 0)
            {
                EditorUtility.DisplayDialog("Info", "No moves detected.", "OK");
                return;
            }

            // Implementation plan: Show confirmation first
            string moveList = string.Join("\n", moves.Select(m => $"{m.fromPath} -> {m.toPath}").Take(10));
            if (moves.Count > 10) moveList += $"\n... and {moves.Count - 10} more";

            if (!EditorUtility.DisplayDialog("Apply Moves", $"Apply {moves.Count} move operations to '{avatarObject.name}'?\n\n{moveList}", "Apply", "Cancel")) {
                return;
            }

            // Add VRCFury components (Logic remains similar but improved)
            try {
                VRCFuryMenuHelper.ApplyMovesToAvatar(avatarObject, moves);
                EditorUtility.DisplayDialog("Success", $"Applied {moves.Count} move operations via VRCFury.", "OK");
            } catch (Exception ex) {
                Debug.LogError($"Failed to apply moves: {ex}");
                EditorUtility.DisplayDialog("Error", $"Failed to apply moves: {ex.Message}", "OK");
            }
        }

        private void ExportJson()
        {
            var path = EditorUtility.SaveFilePanel("Export Menu JSON", "", "menu_export.json", "json");
            if (string.IsNullOrEmpty(path)) return;

            var data = new MenuExportData();
            data.moveOperations = treeView.GetMoveOperations();
            data.rootNodes = treeView.GetExportNodes();

            var json = JsonUtility.ToJson(data, true);
            File.WriteAllText(path, json);
            EditorUtility.DisplayDialog("Success", "Exported JSON successfully.", "OK");
        }

        private void ImportJson()
        {
            var path = EditorUtility.OpenFilePanel("Import Menu JSON", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            try {
                var json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<MenuExportData>(json);

                if (data != null && treeView != null)
                {
                    treeView.LoadFromExportData(data);
                }
            } catch (Exception ex) {
                Debug.LogError(ex);
                EditorUtility.DisplayDialog("Error", "Failed to import JSON.", "OK");
            }
        }
    }
}
// Trigger recompile
