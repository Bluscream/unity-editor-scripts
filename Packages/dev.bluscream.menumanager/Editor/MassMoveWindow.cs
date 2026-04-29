using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Bluscream.MenuManager
{
    public class MassMoveWindow : EditorWindow
    {
        [SerializeField] private GameObject avatarObject;
        [SerializeField] private string fromPath = "";
        [SerializeField] private string toPath = "";

        private SerializedObject serializedObject;
        private SerializedProperty avatarObjectProperty;
        private SerializedProperty fromPathProperty;
        private SerializedProperty toPathProperty;

        [MenuItem("Bluscream/Menu Manager/Mass Move Menu Items")]
        public static void ShowWindow()
        {
            var window = GetWindow<MassMoveWindow>("Mass Move Menu Items");
            window.Show();
        }

        private void OnEnable()
        {
            serializedObject = new SerializedObject(this);
            avatarObjectProperty = serializedObject.FindProperty("avatarObject");
            fromPathProperty = serializedObject.FindProperty("fromPath");
            toPathProperty = serializedObject.FindProperty("toPath");
        }

        private void OnGUI()
        {
            serializedObject.Update();

            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(10, 10, 10, 10) });
            GUILayout.Label("Mass Move Menu Items", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Use globs in the 'From' path (e.g. Expressions/*). The 'To' path will be the parent for all moved items.", MessageType.Info);

            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(avatarObjectProperty, new GUIContent("Avatar Root"));

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(fromPathProperty, new GUIContent("From Path (Glob)"));
                if (GUILayout.Button("Select", GUILayout.Width(60))) ShowPathSelector(p => fromPath = p);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(toPathProperty, new GUIContent("Target Path"));
                if (GUILayout.Button("Select", GUILayout.Width(60))) ShowPathSelector(p => toPath = p);
            }

            EditorGUILayout.Space();

            GUI.enabled = avatarObject != null && !string.IsNullOrEmpty(fromPath);
            if (GUILayout.Button("Preview & Move", GUILayout.Height(30)))
            {
                PreviewAndMove();
            }
            GUI.enabled = true;

            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        private void ShowPathSelector(Action<string> onSelect)
        {
            if (avatarObject == null)
            {
                EditorUtility.DisplayDialog("Error", "Select an avatar first.", "OK");
                return;
            }

            var menu = VRCFuryMenuHelper.GetMergedMenu(avatarObject);
            if (menu == null)
            {
                EditorUtility.DisplayDialog("Error", "Could not load menu for this avatar.", "OK");
                return;
            }

            var paths = new List<string>();
            CollectPaths(menu, "", paths);

            var gm = new GenericMenu();
            gm.AddItem(new GUIContent("(Root)"), false, () => onSelect(""));
            foreach (var path in paths.OrderBy(p => p))
            {
                gm.AddItem(new GUIContent(path), false, () => onSelect(path));
            }
            gm.ShowAsContext();
        }

        private void CollectPaths(VRCExpressionsMenu menu, string currentPath, List<string> paths)
        {
            if (menu == null || menu.controls == null) return;

            foreach (var control in menu.controls)
            {
                var path = string.IsNullOrEmpty(currentPath) ? control.name : currentPath + "/" + control.name;
                paths.Add(path);

                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null)
                {
                    CollectPaths(control.subMenu, path, paths);
                }
            }
        }

        private void PreviewAndMove()
        {
            var menu = VRCFuryMenuHelper.GetMergedMenu(avatarObject);
            if (menu == null) return;

            var allPaths = new List<string>();
            CollectPaths(menu, "", allPaths);

            var regex = GlobToRegex(fromPath);
            var matches = allPaths.Where(p => Regex.IsMatch(p, regex, RegexOptions.IgnoreCase)).ToList();

            if (matches.Count == 0)
            {
                EditorUtility.DisplayDialog("Mass Move", "No menu items matched the pattern.", "OK");
                return;
            }

            var moves = new List<MenuMoveOperation>();
            foreach (var match in matches)
            {
                var fileName = match.Split('/').Last();
                var target = string.IsNullOrEmpty(toPath) ? fileName : toPath + "/" + fileName;
                if (match == target) continue;
                moves.Add(new MenuMoveOperation { fromPath = match, toPath = target });
            }

            if (moves.Count == 0)
            {
                EditorUtility.DisplayDialog("Mass Move", "No moves needed (items are already at the target path).", "OK");
                return;
            }

            string preview = string.Join("\n", moves.Select(m => $"{m.fromPath} -> {m.toPath}").Take(15));
            if (moves.Count > 15) preview += $"\n... and {moves.Count - 15} more";

            if (EditorUtility.DisplayDialog("Confirm Mass Move", $"Found {moves.Count} matching items. Apply moves?\n\n{preview}", "Apply", "Cancel"))
            {
                try
                {
                    VRCFuryMenuHelper.ApplyMovesToAvatar(avatarObject, moves);
                    EditorUtility.DisplayDialog("Success", $"Successfully applied {moves.Count} move operations.", "OK");
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                    EditorUtility.DisplayDialog("Error", $"Failed to apply moves: {ex.Message}", "OK");
                }
            }
        }

        private string GlobToRegex(string glob)
        {
            return "^" + Regex.Escape(glob).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        }
    }
}
