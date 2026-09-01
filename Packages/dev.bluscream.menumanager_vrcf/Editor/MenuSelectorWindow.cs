using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Bluscream.MenuManager
{
    public class MenuSelectorWindow : EditorWindow
    {
        private MenuTreeView treeView;
        private TreeViewState treeViewState;
        private Action<string> onMenuPathSelected;
        private Action<VRCExpressionsMenu> onMenuAssetSelected;
        private SearchField searchField;

        public static void ShowWindow(VRCExpressionsMenu rootMenu, Action<string> onSelectPath, Action<VRCExpressionsMenu> onSelectAsset = null)
        {
            var window = GetWindow<MenuSelectorWindow>(true, "Select VRChat Expressions Menu", true);
            window.minSize = new Vector2(350, 450);
            window.Initialize(rootMenu, onSelectPath, onSelectAsset);
            window.Show();
        }

        public void Initialize(VRCExpressionsMenu rootMenu, Action<string> onSelectPath, Action<VRCExpressionsMenu> onSelectAsset = null)
        {
            this.onMenuPathSelected = onSelectPath;
            this.onMenuAssetSelected = onSelectAsset;

            if (treeViewState == null)
                treeViewState = new TreeViewState();

            treeView = new MenuTreeView(treeViewState, rootMenu, OnItemSelected);
            searchField = new SearchField();
            searchField.downOrUpArrowKeyPressed += treeView.SetFocusAndEnsureSelectedItem;
        }

        private void OnGUI()
        {
            if (treeView == null)
            {
                EditorGUILayout.HelpBox("No menu loaded.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(5, 5, 5, 5) });

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                treeView.searchString = searchField.OnToolbarGUI(treeView.searchString);
                if (GUILayout.Button("Expand All", EditorStyles.toolbarButton, GUILayout.Width(75)))
                {
                    treeView.ExpandAll();
                }
                if (GUILayout.Button("Collapse All", EditorStyles.toolbarButton, GUILayout.Width(75)))
                {
                    treeView.CollapseAll();
                }
            }

            Rect rect = GUILayoutUtility.GetRect(0, 10000, 0, 10000);
            treeView.OnGUI(rect);

            EditorGUILayout.EndVertical();
        }

        private void OnItemSelected(MenuTreeItem item)
        {
            if (item == null) return;
            onMenuPathSelected?.Invoke(item.FullPath);
            if (item.MenuAsset != null)
            {
                onMenuAssetSelected?.Invoke(item.MenuAsset);
            }
            Close();
        }
    }

    public class MenuTreeItem : TreeViewItem
    {
        public string FullPath { get; set; }
        public VRCExpressionsMenu MenuAsset { get; set; }
        public VRCExpressionsMenu.Control Control { get; set; }
        public bool IsFolder { get; set; }

        public MenuTreeItem(int id, int depth, string displayName, string fullPath, VRCExpressionsMenu menuAsset, VRCExpressionsMenu.Control control, bool isFolder)
            : base(id, depth, displayName)
        {
            this.FullPath = fullPath;
            this.MenuAsset = menuAsset;
            this.Control = control;
            this.IsFolder = isFolder;
            
            // Assign Unity built-in icons based on folder/item type
            if (isFolder)
            {
                this.icon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
            }
            else
            {
                this.icon = EditorGUIUtility.IconContent("ScriptableObject Icon").image as Texture2D;
            }
        }
    }

    public class MenuTreeView : TreeView
    {
        private VRCExpressionsMenu rootMenu;
        private Action<MenuTreeItem> onSelectCallback;
        private int currentId = 1;

        public MenuTreeView(TreeViewState state, VRCExpressionsMenu rootMenu, Action<MenuTreeItem> onSelectCallback)
            : base(state)
        {
            this.rootMenu = rootMenu;
            this.onSelectCallback = onSelectCallback;
            this.showBorder = true;
            this.showAlternatingRowBackgrounds = true;
            Reload();
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
            var allItems = new List<TreeViewItem>();

            currentId = 1;

            var rootItem = new MenuTreeItem(
                currentId++,
                0,
                "(Root Path)",
                "",
                rootMenu,
                null,
                true
            );
            root.AddChild(rootItem);
            allItems.Add(rootItem);

            if (rootMenu != null)
            {
                BuildRecursive(rootMenu, "", 1, rootItem, allItems);
            }

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        private void BuildRecursive(VRCExpressionsMenu menu, string parentPath, int depth, TreeViewItem parentItem, List<TreeViewItem> allItems)
        {
            if (menu == null || menu.controls == null) return;

            foreach (var control in menu.controls)
            {
                var fullPath = string.IsNullOrEmpty(parentPath) ? control.name : parentPath + "/" + control.name;
                bool isSubMenu = control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null;

                var item = new MenuTreeItem(
                    currentId++,
                    depth,
                    control.name,
                    fullPath,
                    control.subMenu,
                    control,
                    isSubMenu
                );

                parentItem.AddChild(item);
                allItems.Add(item);

                if (isSubMenu)
                {
                    BuildRecursive(control.subMenu, fullPath, depth + 1, item, allItems);
                }
            }
        }

        protected override void DoubleClickedItem(int id)
        {
            var item = FindItem(id, rootItem) as MenuTreeItem;
            if (item != null)
            {
                onSelectCallback?.Invoke(item);
            }
        }

        protected override void SingleClickedItem(int id)
        {
            // Optional: can handle single click selection if needed
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = args.item as MenuTreeItem;
            if (item != null)
            {
                Rect contentRect = args.rowRect;
                contentRect.xMin += GetContentIndent(item);

                // Draw Icon if available
                if (item.icon != null)
                {
                    Rect iconRect = new Rect(contentRect.x, contentRect.y + 1, 16, 16);
                    GUI.DrawTexture(iconRect, item.icon);
                    contentRect.xMin += 20;
                }

                // Item Name
                var labelStyle = new GUIStyle(args.selected ? EditorStyles.whiteLabel : EditorStyles.label) { richText = true };
                GUI.Label(contentRect, item.displayName, labelStyle);
            }
            else
            {
                base.RowGUI(args);
            }
        }
    }
}
