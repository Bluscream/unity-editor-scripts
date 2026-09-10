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
        private bool isLocatingItem = false;

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
                LoadMenu(silent: true, resetState: false);
            }
        }

        private void OnSelectionChange()
        {
            if (isLocatingItem) return;

            if (AutoSelectActiveAvatar())
            {
                Repaint();
            }
        }

        private void OnHierarchyChange()
        {
            if (isLocatingItem) return;

            if (avatarObject != null)
            {
                LoadMenu(silent: true, resetState: false);
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

                    // 1. Index number (always pinned at left: e.g. "1.", "2.")
                    var indexColor = GetDepthColor(savedIndent);
                    var indexStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        padding = new RectOffset(0, 2, 0, 0),
                        normal = { textColor = indexColor }
                    };
                    EditorGUILayout.LabelField($"{i + 1}", indexStyle, GUILayout.Width(20));

                    // 2. Icon or placeholder spacing (always pinned right next to index)
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

                    // 3. Tree depth indentation (indents ONLY the foldout and text label)
                    if (savedIndent > 0)
                    {
                        GUILayout.Space(savedIndent * 15);
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
                        var targetDisplay = string.IsNullOrEmpty(pendingMoves[itemPath]) ? "<color=red>[Delete]</color>" : $"-> {pendingMoves[itemPath]}";
                        var moveLabelStyle = new GUIStyle(EditorStyles.miniLabel) { richText = true };
                        EditorGUILayout.LabelField(targetDisplay, moveLabelStyle);
                        if (GUILayout.Button("X", GUILayout.Width(20))) pendingMoves.Remove(itemPath);
                    }

                    var menuIcon = EditorGUIUtility.IconContent("_Menu");
                    GUIContent btnContent = (menuIcon != null && menuIcon.image != null) 
                        ? new GUIContent(menuIcon.image, "Actions") 
                        : new GUIContent("...", "Actions");

                    var menuBtnStyle = new GUIStyle(EditorStyles.iconButton ?? EditorStyles.miniButton)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fixedWidth = 20,
                        fixedHeight = 18
                    };

                    if (GUILayout.Button(btnContent, menuBtnStyle, GUILayout.Width(20), GUILayout.Height(18)))
                    {
                        ShowItemMenu(control, itemPath, menu);
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

        private void ShowItemMenu(VRCExpressionsMenu.Control control, string itemPath, VRCExpressionsMenu parentMenu)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Move"), false, () => ShowMoveSelector(itemPath));
            menu.AddItem(new GUIContent("Rename"), false, () => PromptRename(itemPath));
            menu.AddItem(new GUIContent("Set Icon..."), false, () => PromptSetIcon(control));

            menu.AddSeparator("");

            // 1. Locate Item (Submenu asset in Project, or GameObject in Hierarchy)
            menu.AddItem(new GUIContent("Show Item in Hierarchy or Project"), false, () => LocateItem(control, itemPath));

            // 2. Locate Icon Asset separately in Project browser
            if (control.icon != null && EditorUtility.IsPersistent(control.icon))
            {
                menu.AddItem(new GUIContent("Show Icon in Project"), false, () => LocateIcon(control));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Show Icon in Project"));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete"), false, () => SetDelete(itemPath));

            menu.ShowAsContext();
        }

        private void LocateItem(VRCExpressionsMenu.Control control, string itemPath)
        {
            if (control == null) return;

            isLocatingItem = true;
            try
            {
                // 1. If control has a subMenu asset that exists on disk, ping it
                if (control.subMenu != null && EditorUtility.IsPersistent(control.subMenu))
                {
                    EditorGUIUtility.PingObject(control.subMenu);
                    Selection.activeObject = control.subMenu;
                    return;
                }

                if (avatarObject != null)
                {
                    var cleanName = CleanTags(control.name)?.Trim();
                    var paramName = control.parameter?.name?.Trim();
                    var cleanPath = CleanTags(itemPath)?.Trim();

                    // 2. Inspect VRCFury components on avatar and child GameObjects
                    if (Bluscream.VRCFury.Utils.TryInitialize())
                    {
                        var vrcfComponents = avatarObject.GetComponentsInChildren(Bluscream.VRCFury.Utils.VRCFuryComponentType, true);
                        foreach (var comp in vrcfComponents)
                        {
                            if (comp == null) continue;

                            var features = new List<object>();

                            // Modern VRCFury: content field
                            if (ReflectionHelper.TryGetFieldValue(comp, "content", out object contentObj) && contentObj != null)
                            {
                                features.Add(contentObj);
                            }

                            // Legacy VRCFury: config.features list
                            if (ReflectionHelper.TryGetFieldValue(comp, "config", out object config) && config != null)
                            {
                                if (ReflectionHelper.TryGetFieldValue(config, "features", out object featuresListObj) && featuresListObj is System.Collections.IEnumerable featuresEnum)
                                {
                                    foreach (var f in featuresEnum)
                                    {
                                        if (f != null) features.Add(f);
                                    }
                                }
                            }

                            foreach (var feature in features)
                            {
                                if (feature == null) continue;

                                // Check feature string fields: name, path, fromPath, toPath, etc.
                                string featName = null;
                                if (ReflectionHelper.TryGetFieldValue(feature, "name", out string n) ||
                                    ReflectionHelper.TryGetPropertyValue(feature, "name", out n))
                                {
                                    featName = n;
                                }
                                else if (ReflectionHelper.TryGetFieldValue(feature, "path", out string p) ||
                                         ReflectionHelper.TryGetPropertyValue(feature, "path", out p))
                                {
                                    featName = p;
                                }

                                if (!string.IsNullOrEmpty(featName))
                                {
                                    var cleanFeatName = CleanTags(featName).Trim();
                                    if (!string.IsNullOrEmpty(cleanPath) &&
                                        (string.Equals(cleanFeatName, cleanPath, StringComparison.OrdinalIgnoreCase) ||
                                         cleanFeatName.EndsWith("/" + cleanPath, StringComparison.OrdinalIgnoreCase) ||
                                         cleanPath.EndsWith("/" + cleanFeatName, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        EditorGUIUtility.PingObject(comp.gameObject);
                                        Selection.activeGameObject = comp.gameObject;
                                        return;
                                    }

                                    if (!string.IsNullOrEmpty(cleanName) &&
                                        (string.Equals(cleanFeatName, cleanName, StringComparison.OrdinalIgnoreCase) ||
                                         cleanFeatName.EndsWith("/" + cleanName, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        EditorGUIUtility.PingObject(comp.gameObject);
                                        Selection.activeGameObject = comp.gameObject;
                                        return;
                                    }
                                }

                                // Check if feature references parameter
                                if (!string.IsNullOrEmpty(paramName))
                                {
                                    if (ReflectionHelper.TryGetFieldValue(feature, "param", out string fParam) ||
                                        ReflectionHelper.TryGetFieldValue(feature, "parameter", out fParam) ||
                                        ReflectionHelper.TryGetFieldValue(feature, "driveGlobalParam", out fParam))
                                    {
                                        if (!string.IsNullOrEmpty(fParam) && (string.Equals(fParam, paramName, StringComparison.OrdinalIgnoreCase) || fParam.EndsWith("/" + paramName, StringComparison.OrdinalIgnoreCase)))
                                        {
                                            EditorGUIUtility.PingObject(comp.gameObject);
                                            Selection.activeGameObject = comp.gameObject;
                                            return;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // 3. Search matching GameObject names in hierarchy
                    var transforms = avatarObject.GetComponentsInChildren<Transform>(true);
                    foreach (var t in transforms)
                    {
                        if (!string.IsNullOrEmpty(cleanName) && t.name.IndexOf(cleanName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            EditorGUIUtility.PingObject(t.gameObject);
                            Selection.activeGameObject = t.gameObject;
                            return;
                        }
                    }

                    // 4. If parameter exists, search GameObject names matching the parameter
                    if (!string.IsNullOrEmpty(paramName))
                    {
                        foreach (var t in transforms)
                        {
                            if (t.name.IndexOf(paramName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                EditorGUIUtility.PingObject(t.gameObject);
                                Selection.activeGameObject = t.gameObject;
                                return;
                            }
                        }
                    }

                    // Default fallback: ping the avatar root itself
                    EditorGUIUtility.PingObject(avatarObject);
                    Selection.activeGameObject = avatarObject;
                }
            }
            finally
            {
                // Reset on next tick to absorb immediate selection/hierarchy events
                EditorApplication.delayCall += () => { isLocatingItem = false; };
            }
        }

        private void LocateIcon(VRCExpressionsMenu.Control control)
        {
            if (control?.icon != null && EditorUtility.IsPersistent(control.icon))
            {
                isLocatingItem = true;
                try
                {
                    EditorGUIUtility.PingObject(control.icon);
                    Selection.activeObject = control.icon;
                }
                finally
                {
                    EditorApplication.delayCall += () => { isLocatingItem = false; };
                }
            }
        }

        private void PromptRename(string originalPath)
        {
            var currentName = originalPath.Split('/').Last();
            var window = RenamePopup.ShowWindow(currentName, (newName) =>
            {
                if (!string.IsNullOrEmpty(newName) && newName != currentName)
                {
                    var parentPath = originalPath.Contains('/') ? originalPath.Substring(0, originalPath.LastIndexOf('/')) : "";
                    var targetPath = string.IsNullOrEmpty(parentPath) ? newName : $"{parentPath}/{newName}";
                    pendingMoves[originalPath] = targetPath;
                }
            });
        }

        private void PromptSetIcon(VRCExpressionsMenu.Control control)
        {
            var path = EditorUtility.OpenFilePanelWithFilters("Select Icon Texture", "Assets", new string[] { "Image files", "png,jpg,jpeg,tga,psd" });
            if (string.IsNullOrEmpty(path)) return;

            if (path.StartsWith(Application.dataPath))
            {
                var relativePath = "Assets" + path.Substring(Application.dataPath.Length);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(relativePath);
                if (tex != null)
                {
                    control.icon = tex;
                    EditorUtility.SetDirty(control.subMenu ?? (UnityEngine.Object)avatarObject);
                    ShowNotification(new GUIContent("Icon updated!"));
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Notice", "Selected icon must be inside the Unity project's Assets folder.", "OK");
            }
        }

        private void SetDelete(string originalPath)
        {
            // In VRCFury MoveMenuItem, moving an item to an empty toPath ("") removes/deletes the item
            pendingMoves[originalPath] = "";
        }

        public static string CleanTags(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            // Strip unsupported TextMeshPro layout tags that break Unity standard IMGUI
            string cleaned = Regex.Replace(input, @"</?(?:size|line-height|voffset|align|pos|space|font|sprite|material|alpha)[^>]*>", "", RegexOptions.IgnoreCase);

            // Ensure any opened <b> or <color=...> tags without closing tags are properly closed so they don't bleed into the whole UI
            int openB = Regex.Matches(cleaned, @"<b\b[^>]*>", RegexOptions.IgnoreCase).Count;
            int closeB = Regex.Matches(cleaned, @"</b>", RegexOptions.IgnoreCase).Count;
            while (openB > closeB)
            {
                cleaned += "</b>";
                closeB++;
            }

            int openI = Regex.Matches(cleaned, @"<i\b[^>]*>", RegexOptions.IgnoreCase).Count;
            int closeI = Regex.Matches(cleaned, @"</i>", RegexOptions.IgnoreCase).Count;
            while (openI > closeI)
            {
                cleaned += "</i>";
                closeI++;
            }

            int openColor = Regex.Matches(cleaned, @"<color\b[^>]*>", RegexOptions.IgnoreCase).Count;
            int closeColor = Regex.Matches(cleaned, @"</color>", RegexOptions.IgnoreCase).Count;
            while (openColor > closeColor)
            {
                cleaned += "</color>";
                closeColor++;
            }

            return cleaned.Trim();
        }

        public static string FormatDisplayName(VRCExpressionsMenu.Control control)
        {
            if (control == null) return "<null>";
            string cleaned = CleanTags(control.name);

            // Check if stripping tags left an empty string (e.g. was only "<b></b>" or "<size=20>")
            string pureText = Regex.Replace(cleaned, @"<[^>]*>", "").Trim();
            if (string.IsNullOrEmpty(pureText))
            {
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

        public static Color GetDepthColor(int depth)
        {
            // Curated palette of soft, distinct colors per hierarchy depth
            switch (depth % 6)
            {
                case 0: return new Color(0.85f, 0.85f, 0.85f, 0.95f); // Depth 0: Soft White / Light Grey
                case 1: return new Color(0.40f, 0.80f, 1.00f, 0.95f); // Depth 1: Soft Cyan / Sky Blue
                case 2: return new Color(0.45f, 0.90f, 0.55f, 0.95f); // Depth 2: Mint / Pastel Green
                case 3: return new Color(1.00f, 0.80f, 0.40f, 0.95f); // Depth 3: Soft Amber / Warm Yellow
                case 4: return new Color(0.90f, 0.55f, 0.95f, 0.95f); // Depth 4: Pastel Lavender / Purple
                case 5: return new Color(1.00f, 0.55f, 0.65f, 0.95f); // Depth 5: Pastel Rose / Coral
                default: return new Color(0.75f, 0.75f, 0.75f, 0.90f);
            }
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

        private void LoadMenu(bool silent = false, bool resetState = true)
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
                if (resetState)
                {
                    pendingMoves.Clear();
                    foldouts.Clear();
                }
                if (!silent)
                {
                    ShowNotification(new GUIContent($"Loaded '{mergedMenu.name}'!"));
                }
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

    public class RenamePopup : EditorWindow
    {
        private string newName = "";
        private Action<string> onConfirm;
        private bool isFirstFocus = true;

        public static RenamePopup ShowWindow(string currentName, Action<string> onConfirm)
        {
            var window = CreateInstance<RenamePopup>();
            window.titleContent = new GUIContent("Rename Menu Item");
            window.newName = currentName ?? "";
            window.onConfirm = onConfirm;
            window.minSize = new Vector2(300, 90);
            window.maxSize = new Vector2(450, 90);
            window.ShowUtility();
            return window;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            GUI.SetNextControlName("RenameField");
            newName = EditorGUILayout.TextField("New Name", newName);

            if (isFirstFocus)
            {
                EditorGUI.FocusTextInControl("RenameField");
                isFirstFocus = false;
            }

            EditorGUILayout.Space(10);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(70)))
                {
                    Close();
                }
                if (GUILayout.Button("Rename", GUILayout.Width(70)) || (Event.current.isKey && Event.current.keyCode == KeyCode.Return))
                {
                    onConfirm?.Invoke(newName);
                    Close();
                }
            }
        }
    }
}
