using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Bluscream.MenuManager
{
    public class MenuManagerWindow : EditorWindow
    {
        [SerializeField] private GameObject avatarObject;
        [SerializeField] private VRCExpressionsMenu mergedMenu;
        
        private SerializedObject serializedObject;
        private SerializedProperty avatarObjectProperty;
        private Vector2 scrollPos;
        private Dictionary<string, string> pendingMoves = new Dictionary<string, string>();
        private Dictionary<string, bool> foldouts = new Dictionary<string, bool>();

        [MenuItem("Bluscream/VRChat/Menu Manager")]
        public static void ShowWindow()
        {
            var window = GetWindow<MenuManagerWindow>("Menu Manager");
            window.Show();
        }

        private void OnEnable()
        {
            serializedObject = new SerializedObject(this);
            avatarObjectProperty = serializedObject.FindProperty("avatarObject");
            AutoSelectActiveAvatar();
            if (avatarObject != null)
            {
                LoadMenu(silent: true);
            }
        }

        private void OnSelectionChange()
        {
            if (AutoSelectActiveAvatar())
            {
                Repaint();
            }
        }

        private void OnHierarchyChange()
        {
            if (avatarObject != null)
            {
                LoadMenu(silent: true);
                Repaint();
            }
        }

        private bool AutoSelectActiveAvatar()
        {
            if (Selection.activeGameObject != null)
            {
                var descriptor = Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>();
                if (descriptor != null && descriptor.gameObject != avatarObject)
                {
                    avatarObject = descriptor.gameObject;
                    serializedObject.Update();
                    LoadMenu(silent: true);
                    return true;
                }
            }
            return false;
        }

        private void OnGUI()
        {
            serializedObject.Update();
            
            EditorGUILayout.BeginVertical();
            GUILayout.Label("VRCFury Menu Manager", EditorStyles.boldLabel);
            
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(avatarObjectProperty, new GUIContent("Avatar Root"));
                if (EditorGUI.EndChangeCheck() && avatarObject != null)
                {
                    LoadMenu();
                }

                if (GUILayout.Button("Load", GUILayout.Width(60)))
                {
                    if (avatarObject == null && Selection.activeGameObject != null)
                    {
                        avatarObject = Selection.activeGameObject;
                        serializedObject.Update();
                    }
                    LoadMenu();
                }
            }

            if (mergedMenu != null) {
                EditorGUILayout.HelpBox($"Menu: {mergedMenu.name} ({mergedMenu.controls.Count} root controls)", MessageType.Info);
                
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                    if (GUILayout.Button("Expand All", EditorStyles.toolbarButton)) SetAllFoldouts(true);
                    if (GUILayout.Button("Collapse All", EditorStyles.toolbarButton)) SetAllFoldouts(false);
                    if (GUILayout.Button("Clear Moves", EditorStyles.toolbarButton)) pendingMoves.Clear();
                    GUILayout.FlexibleSpace();
                }

                scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
                DrawMenuRecursive(mergedMenu, "");
                EditorGUILayout.EndScrollView();

                GUILayout.Space(10);
                if (pendingMoves.Count > 0) {
                    EditorGUILayout.LabelField($"Pending Moves: {pendingMoves.Count}", EditorStyles.boldLabel);
                    foreach (var move in pendingMoves.Take(5)) {
                        EditorGUILayout.LabelField($"{move.Key} -> {move.Value}", EditorStyles.miniLabel);
                    }
                    if (pendingMoves.Count > 5) EditorGUILayout.LabelField("...", EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope()) {
                    GUI.enabled = pendingMoves.Count > 0;
                    if (GUILayout.Button("Apply Moves via VRCFury")) ApplyMoves();
                    GUI.enabled = true;
                    if (GUILayout.Button("Export JSON")) ExportJson();
                    if (GUILayout.Button("Import JSON")) ImportJson();
                }
            } else {
                EditorGUILayout.HelpBox("Select an avatar and click Load to see the menu hierarchy.", MessageType.Info);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(avatarObject != null ? $"Active: {avatarObject.name}" : "No Avatar", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawMenuRecursive(VRCExpressionsMenu menu, string currentPath)
        {
            if (menu == null || menu.controls == null) return;

            const int maxPageSlots = 8;
            bool hasPagination = menu.controls.Count > maxPageSlots;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                // When pagination is needed, the first page can hold 7 items + "Next",
                // and subsequent pages also hold 7 items + "Next" (or up to 8 on the final page).
                if (hasPagination && i > 0 && i % (maxPageSlots - 1) == 0)
                {
                    int pageNum = (i / (maxPageSlots - 1)) + 1;
                    DrawPageSeparator(pageNum, i);
                }

                var control = menu.controls[i];
                if (control == null) continue;

                var itemPath = string.IsNullOrEmpty(currentPath) ? control.name : currentPath + "/" + control.name;
                var isSubMenuType = control.type == VRCExpressionsMenu.Control.ControlType.SubMenu;
                var hasSubMenu = isSubMenuType && control.subMenu != null;
                var isSubMenuEmpty = isSubMenuType && control.subMenu == null;

                var savedIndent = EditorGUI.indentLevel;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.indentLevel = 0; // Prevent Unity from double-indenting or clipping controls inside HorizontalScope

                    // Manual indent for tree depth
                    if (savedIndent > 0)
                    {
                        GUILayout.Space(savedIndent * 15);
                    }

                    // Index number (e.g. "1.", "2.")
                    var indexStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        padding = new RectOffset(0, 2, 0, 0),
                        normal = { textColor = new Color(0.6f, 0.6f, 0.6f, 0.85f) }
                    };
                    EditorGUILayout.LabelField($"{i + 1}.", indexStyle, GUILayout.Width(22));

                    // Icon or placeholder spacing
                    if (control.icon != null)
                    {
                        var iconRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(16));
                        GUI.DrawTexture(iconRect, control.icon, ScaleMode.ScaleToFit);
                        GUILayout.Space(4);
                    }
                    else
                    {
                        GUILayout.Space(20); // 16px icon + 4px space placeholder
                    }

                    var displayName = FormatDisplayName(control);
                    var rawName = control.name ?? "";

                    if (hasSubMenu)
                    {
                        if (!foldouts.ContainsKey(itemPath)) foldouts[itemPath] = false;
                        var foldoutStyle = new GUIStyle(EditorStyles.foldout) { richText = true };
                        foldouts[itemPath] = EditorGUILayout.Foldout(foldouts[itemPath], new GUIContent(displayName, rawName != displayName ? rawName : null), true, foldoutStyle);
                    }
                    else
                    {
                        GUILayout.Space(15);
                        var labelStyle = new GUIStyle(EditorStyles.label) { richText = true };
                        if (isSubMenuEmpty)
                        {
                            var emptySubStyle = new GUIStyle(EditorStyles.label) { richText = true, normal = { textColor = new Color(0.9f, 0.6f, 0.2f) } };
                            EditorGUILayout.LabelField(new GUIContent($"{displayName} <color=orange>[SubMenu (Unassigned)]</color>", rawName), emptySubStyle);
                        }
                        else
                        {
                            EditorGUILayout.LabelField(new GUIContent(displayName, rawName != displayName ? rawName : null), labelStyle);
                        }
                    }

                    GUILayout.FlexibleSpace();

                    if (pendingMoves.ContainsKey(itemPath))
                    {
                        EditorGUILayout.LabelField($"-> {pendingMoves[itemPath]}", EditorStyles.miniLabel);
                        if (GUILayout.Button("X", GUILayout.Width(20))) pendingMoves.Remove(itemPath);
                    }
                    else
                    {
                        if (GUILayout.Button("Move", GUILayout.Width(50))) ShowMoveSelector(itemPath);
                    }

                    EditorGUI.indentLevel = savedIndent;
                }

                if (hasSubMenu && foldouts[itemPath])
                {
                    EditorGUI.indentLevel++;
                    DrawMenuRecursive(control.subMenu, itemPath);
                    EditorGUI.indentLevel--;
                }
            }
        }

        public static string CleanTags(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            // Strip XML/HTML/TMP tags like <b>, </b>, <size=...>, <color=...>, <line-height=...>, <voffset=...>, etc.
            string cleaned = Regex.Replace(input, @"<[^>]*>", "").Trim();
            return cleaned;
        }

        public static string FormatDisplayName(VRCExpressionsMenu.Control control)
        {
            if (control == null) return "<null>";
            string cleaned = CleanTags(control.name);

            if (string.IsNullOrEmpty(cleaned))
            {
                // If stripping tags left nothing (e.g. "<b>", "<size=20>"), provide a clear, helpful fallback
                if (!string.IsNullOrEmpty(control.parameter?.name))
                {
                    cleaned = $"<i>({control.parameter.name})</i>";
                }
                else if (control.icon != null && !string.IsNullOrEmpty(control.icon.name))
                {
                    cleaned = $"<i>({control.icon.name})</i>";
                }
                else
                {
                    cleaned = $"<i>({control.type})</i>";
                }
            }

            return cleaned;
        }

        private void DrawPageSeparator(int pageNum, int itemIndex)
        {
            GUILayout.Space(3);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (EditorGUI.indentLevel > 0)
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15);
                }
                var originalColor = GUI.color;
                GUI.color = new Color(0.3f, 0.8f, 1f, 0.8f);
                var sepStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontStyle = FontStyle.Bold
                };
                EditorGUILayout.LabelField($"─── ↷ Next ── Page {pageNum} (Items {itemIndex + 1}+) ────────────────────────", sepStyle);
                GUI.color = originalColor;
            }
            GUILayout.Space(2);
        }

        private void ShowMoveSelector(string originalPath)
        {
            var menu = VRCFuryMenuHelper.GetMergedMenu(avatarObject);
            if (menu == null) return;

            MenuSelectorWindow.ShowWindow(menu, (targetParent) => {
                SetMove(originalPath, targetParent);
            });
        }

        private void CollectPaths(VRCExpressionsMenu menu, string currentPath, List<string> paths)
        {
            if (menu == null || menu.controls == null) return;
            foreach (var control in menu.controls)
            {
                var path = string.IsNullOrEmpty(currentPath) ? control.name : currentPath + "/" + control.name;
                paths.Add(path);
                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null)
                    CollectPaths(control.subMenu, path, paths);
            }
        }

        private void SetMove(string originalPath, string targetParent)
        {
            var fileName = originalPath.Split('/').Last();
            var targetPath = string.IsNullOrEmpty(targetParent) ? fileName : targetParent + "/" + fileName;
            if (originalPath == targetPath) return;
            pendingMoves[originalPath] = targetPath;
        }

        private void SetAllFoldouts(bool state)
        {
            var keys = foldouts.Keys.ToList();
            foreach (var key in keys) foldouts[key] = state;
        }

        private void LoadMenu(bool silent = false)
        {
            if (avatarObject == null)
            {
                if (!silent)
                {
                    EditorUtility.DisplayDialog("Error", "Please select an Avatar Root GameObject first.", "OK");
                }
                return;
            }

            mergedMenu = VRCFuryMenuHelper.GetMergedMenu(avatarObject);
            if (mergedMenu == null)
            {
                Debug.LogWarning("[MenuManagerWindow] VRCFuryMenuHelper.GetMergedMenu returned null! Falling back to VRCAvatarDescriptor.expressionsMenu.");
                // Fallback: try reading expressionsMenu directly from VRCAvatarDescriptor
                var descriptor = avatarObject.GetComponent<VRCAvatarDescriptor>();
                if (descriptor != null && descriptor.expressionsMenu != null)
                {
                    mergedMenu = descriptor.expressionsMenu;
                }
            }
            else
            {
                Debug.Log($"[MenuManagerWindow] Successfully loaded merged menu via VRCFury with {mergedMenu.controls?.Count ?? 0} root controls.");
            }

            if (mergedMenu != null)
            {
                mergedMenu.hideFlags = HideFlags.DontSave | HideFlags.DontUnloadUnusedAsset;
                pendingMoves.Clear();
                foldouts.Clear();
                ShowNotification(new GUIContent($"Loaded '{mergedMenu.name}'!"));
            }
            else if (!silent)
            {
                EditorUtility.DisplayDialog("Menu Manager", "Could not resolve Expression Menu for the selected avatar.\n\nEnsure VRCFury is installed/active or the avatar has a valid VRCAvatarDescriptor with an Expressions Menu assigned.", "OK");
            }
        }

        private void ApplyMoves()
        {
            var moves = pendingMoves.Select(kvp => new MenuMoveOperation { fromPath = kvp.Key, toPath = kvp.Value }).ToList();
            if (moves.Count == 0) return;

            if (EditorUtility.DisplayDialog("Apply Moves", $"Apply {moves.Count} move operations?", "Apply", "Cancel"))
            {
                VRCFuryMenuHelper.ApplyMovesToAvatar(avatarObject, moves);
                pendingMoves.Clear();
                EditorUtility.DisplayDialog("Success", "Applied moves via VRCFury.", "OK");
            }
        }

        private void ExportJson()
        {
            if (mergedMenu == null)
            {
                EditorUtility.DisplayDialog("Export JSON", "No menu loaded to export.", "OK");
                return;
            }

            var defaultName = avatarObject != null ? $"{avatarObject.name}_menu.json" : "menu_export.json";
            var path = EditorUtility.SaveFilePanel("Export Menu JSON", "", defaultName, "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var data = new MenuExportData();
                data.moveOperations = pendingMoves.Select(kvp => new MenuMoveOperation { fromPath = kvp.Key, toPath = kvp.Value }).ToList();

                var visited = new HashSet<VRCExpressionsMenu>();
                foreach (var control in mergedMenu.controls)
                {
                    var node = ExportControlRecursive(control, "", visited);
                    if (node != null) data.rootNodes.Add(node);
                }

                var sb = new System.Text.StringBuilder(16384);
                SerializeDataToJson(data, sb);
                File.WriteAllText(path, sb.ToString());
                EditorUtility.DisplayDialog("Success", $"Exported menu JSON successfully to:\n{path}", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MenuManager] Export JSON failed: {ex}");
                EditorUtility.DisplayDialog("Error", $"Failed to export JSON:\n{ex.Message}", "OK");
            }
        }

        private static void SerializeDataToJson(MenuExportData data, System.Text.StringBuilder sb)
        {
            sb.AppendLine("{");
            sb.AppendLine("  \"rootNodes\": [");
            for (int i = 0; i < data.rootNodes.Count; i++)
            {
                SerializeNodeToJson(data.rootNodes[i], sb, 4);
                if (i < data.rootNodes.Count - 1) sb.AppendLine(",");
                else sb.AppendLine();
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"moveOperations\": [");
            for (int i = 0; i < data.moveOperations.Count; i++)
            {
                var move = data.moveOperations[i];
                sb.Append("    { \"fromPath\": \"").Append(EscapeJson(move.fromPath))
                  .Append("\", \"toPath\": \"").Append(EscapeJson(move.toPath)).Append("\" }");
                if (i < data.moveOperations.Count - 1) sb.AppendLine(",");
                else sb.AppendLine();
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
        }

        private static void SerializeNodeToJson(MenuExportNode node, System.Text.StringBuilder sb, int indent)
        {
            var ind = new string(' ', indent);
            var childInd = new string(' ', indent + 2);
            sb.AppendLine(ind + "{");
            sb.AppendLine($"{childInd}\"name\": \"{EscapeJson(node.name)}\",");
            sb.AppendLine($"{childInd}\"originalPath\": \"{EscapeJson(node.originalPath)}\",");
            sb.AppendLine($"{childInd}\"type\": {node.type},");
            sb.AppendLine($"{childInd}\"typeName\": \"{EscapeJson(node.typeName)}\",");
            sb.AppendLine($"{childInd}\"parameter\": \"{EscapeJson(node.parameter)}\",");
            sb.AppendLine($"{childInd}\"value\": {node.value.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.AppendLine($"{childInd}\"iconGuid\": \"{EscapeJson(node.iconGuid)}\",");
            sb.AppendLine($"{childInd}\"isSubMenu\": {(node.isSubMenu ? "true" : "false")},");
            sb.AppendLine($"{childInd}\"subMenuNull\": {(node.subMenuNull ? "true" : "false")},");
            sb.Append($"{childInd}\"children\": [");
            if (node.children != null && node.children.Count > 0)
            {
                sb.AppendLine();
                for (int i = 0; i < node.children.Count; i++)
                {
                    SerializeNodeToJson(node.children[i], sb, indent + 4);
                    if (i < node.children.Count - 1) sb.AppendLine(",");
                    else sb.AppendLine();
                }
                sb.AppendLine($"{childInd}]");
            }
            else
            {
                sb.AppendLine("]");
            }
            sb.Append(ind + "}");
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        private MenuExportNode ExportControlRecursive(VRCExpressionsMenu.Control control, string parentPath, HashSet<VRCExpressionsMenu> visited)
        {
            if (control == null) return null;

            var fullPath = string.IsNullOrEmpty(parentPath) ? control.name : parentPath + "/" + control.name;
            var isSubMenuType = control.type == VRCExpressionsMenu.Control.ControlType.SubMenu;
            var isSubMenuNull = isSubMenuType && control.subMenu == null;

            var node = new MenuExportNode
            {
                name = control.name,
                originalPath = fullPath,
                type = (int)control.type,
                typeName = control.type.ToString(),
                parameter = control.parameter != null ? control.parameter.name : null,
                value = control.value,
                isSubMenu = isSubMenuType,
                subMenuNull = isSubMenuNull
            };

            if (control.icon != null)
            {
                var assetPath = AssetDatabase.GetAssetPath(control.icon);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    node.iconGuid = AssetDatabase.AssetPathToGUID(assetPath);
                }
            }

            if (isSubMenuType && control.subMenu != null && !visited.Contains(control.subMenu))
            {
                visited.Add(control.subMenu);
                if (control.subMenu.controls != null)
                {
                    foreach (var childCtrl in control.subMenu.controls)
                    {
                        var childNode = ExportControlRecursive(childCtrl, fullPath, visited);
                        if (childNode != null) node.children.Add(childNode);
                    }
                }
            }

            return node;
        }

        private void ImportJson()
        {
            var path = EditorUtility.OpenFilePanel("Import Menu JSON", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<MenuExportData>(json);
                if (data != null && data.moveOperations != null && data.moveOperations.Count > 0)
                {
                    pendingMoves.Clear();
                    foreach (var move in data.moveOperations)
                    {
                        if (!string.IsNullOrEmpty(move.fromPath) && !string.IsNullOrEmpty(move.toPath))
                        {
                            pendingMoves[move.fromPath] = move.toPath;
                        }
                    }
                    ShowNotification(new GUIContent($"Loaded {pendingMoves.Count} moves from JSON!"));
                    Repaint();
                }
                else
                {
                    EditorUtility.DisplayDialog("Import JSON", "No pending move operations found in JSON file.", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MenuManager] Import JSON failed: {ex}");
                EditorUtility.DisplayDialog("Error", $"Failed to import JSON:\n{ex.Message}", "OK");
            }
        }
    }
}
// Trigger recompile
