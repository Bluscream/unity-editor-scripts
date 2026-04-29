using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Bluscream.MenuManager
{
    public class MenuTreeViewItem : TreeViewItem
    {
        public string OriginalPath { get; set; }
        public VRCExpressionsMenu.Control Control { get; set; }
        public MenuExportNode ExportNode { get; set; }
    }

    public class MenuTreeView : TreeView
    {
        private VRCExpressionsMenu rootMenu;
        private List<MenuTreeViewItem> allItems = new List<MenuTreeViewItem>();
        private int idCounter = 1;

        public MenuTreeView(TreeViewState state, VRCExpressionsMenu menu) : base(state)
        {
            rootMenu = menu;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            Reload();
        }

        public VRCExpressionsMenu GetMenu() => rootMenu;

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
            allItems.Clear();
            idCounter = 1;

            if (rootMenu != null)
            {
                BuildMenuTree(rootMenu, root, "");
            }

            if (!root.hasChildren)
            {
                root.AddChild(new TreeViewItem { id = -1, displayName = "No menu items found" });
            }

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        private void BuildMenuTree(VRCExpressionsMenu menu, TreeViewItem parent, string currentPath)
        {
            if (menu == null || menu.controls == null) return;

            foreach (var control in menu.controls)
            {
                var itemPath = string.IsNullOrEmpty(currentPath) ? control.name : currentPath + "/" + control.name;
                
                var node = new MenuTreeViewItem
                {
                    id = idCounter++,
                    displayName = string.IsNullOrEmpty(control.name) ? "(Unnamed)" : control.name,
                    OriginalPath = itemPath,
                    Control = control
                };
                
                parent.AddChild(node);
                allItems.Add(node);

                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null)
                {
                    BuildMenuTree(control.subMenu, node, itemPath);
                }
            }
        }

        protected override bool CanStartDrag(CanStartDragArgs args)
        {
            return true;
        }

        protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData("MenuTreeViewItem", args.draggedItemIDs);
            DragAndDrop.StartDrag("MenuManagerDrag");
        }

        protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
        {
            if (args.performDrop)
            {
                var draggedIDs = (IList<int>)DragAndDrop.GetGenericData("MenuTreeViewItem");
                if (draggedIDs != null)
                {
                    foreach (var id in draggedIDs)
                    {
                        var draggedItem = FindItem(id, rootItem);
                        if (draggedItem != null && args.parentItem != null && draggedItem != args.parentItem && !IsDescendant(draggedItem, args.parentItem))
                        {
                            if (draggedItem.parent != null && draggedItem.parent.children != null) {
                                draggedItem.parent.children.Remove(draggedItem);
                            }
                            args.parentItem.AddChild(draggedItem);
                            draggedItem.parent = args.parentItem;
                        }
                    }
                    SetupDepthsFromParentsAndChildren(rootItem);
                    SetExpanded(args.parentItem.id, true);
                    Reload();
                }
            }
            return DragAndDropVisualMode.Move;
        }

        private bool IsDescendant(TreeViewItem parent, TreeViewItem child)
        {
            var p = child.parent;
            while (p != null)
            {
                if (p == parent) return true;
                p = p.parent;
            }
            return false;
        }

        public List<MenuMoveOperation> GetMoveOperations()
        {
            var operations = new List<MenuMoveOperation>();
            CollectMoveOperations(rootItem, "", operations);
            return operations;
        }

        private void CollectMoveOperations(TreeViewItem node, string currentPath, List<MenuMoveOperation> operations)
        {
            if (node == null || node.children == null) return;

            foreach (var child in node.children)
            {
                var menuItem = child as MenuTreeViewItem;
                if (menuItem != null)
                {
                    var newPath = string.IsNullOrEmpty(currentPath) ? menuItem.displayName : currentPath + "/" + menuItem.displayName;
                    
                    if (!string.IsNullOrEmpty(menuItem.OriginalPath) && menuItem.OriginalPath != newPath)
                    {
                        operations.Add(new MenuMoveOperation {
                            fromPath = menuItem.OriginalPath,
                            toPath = newPath
                        });
                    }

                    CollectMoveOperations(child, newPath, operations);
                }
            }
        }

        public List<MenuExportNode> GetExportNodes()
        {
            var nodes = new List<MenuExportNode>();
            if (rootItem != null && rootItem.children != null)
            {
                foreach (var child in rootItem.children)
                {
                    var node = ExportNodeRecursive(child as MenuTreeViewItem);
                    if (node != null) nodes.Add(node);
                }
            }
            return nodes;
        }

        private MenuExportNode ExportNodeRecursive(MenuTreeViewItem item)
        {
            if (item == null) return null;

            var node = new MenuExportNode
            {
                name = item.displayName,
                originalPath = item.OriginalPath,
            };

            if (item.Control != null)
            {
                node.type = (int)item.Control.type;
                node.parameter = item.Control.parameter?.name;
                node.value = item.Control.value;
                if (item.Control.icon != null) {
                    string assetPath = AssetDatabase.GetAssetPath(item.Control.icon);
                    node.iconGuid = AssetDatabase.AssetPathToGUID(assetPath);
                }
            } else if (item.ExportNode != null) {
                node.type = item.ExportNode.type;
                node.parameter = item.ExportNode.parameter;
                node.value = item.ExportNode.value;
                node.iconGuid = item.ExportNode.iconGuid;
            }

            if (item.children != null)
            {
                foreach (var child in item.children)
                {
                    var childNode = ExportNodeRecursive(child as MenuTreeViewItem);
                    if (childNode != null) node.children.Add(childNode);
                }
            }

            return node;
        }

        public void LoadFromExportData(MenuExportData data)
        {
            if (rootItem.children != null) rootItem.children.Clear();
            idCounter = 1;
            allItems.Clear();

            foreach (var node in data.rootNodes)
            {
                var item = ImportNodeRecursive(node);
                if (item != null) rootItem.AddChild(item);
            }

            SetupDepthsFromParentsAndChildren(rootItem);
            Reload();
        }

        private MenuTreeViewItem ImportNodeRecursive(MenuExportNode node)
        {
            if (node == null) return null;

            var item = new MenuTreeViewItem
            {
                id = idCounter++,
                displayName = node.name,
                OriginalPath = node.originalPath,
                ExportNode = node
            };

            allItems.Add(item);

            if (node.children != null)
            {
                foreach (var childNode in node.children)
                {
                    var childItem = ImportNodeRecursive(childNode);
                    if (childItem != null) item.AddChild(childItem);
                }
            }

            return item;
        }
        public new void ExpandAll()
        {
            if (rootItem != null) SetExpandedRecursive(rootItem.id, true);
        }
        public new void CollapseAll()
        {
            if (rootItem != null) SetExpandedRecursive(rootItem.id, false);
        }
    }
}
