using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using Bluscream;

namespace Bluscream.VRCFury
{
    public class MenuItemNode
    {
        public string Name;
        public string FullPath;
        public VRCExpressionsMenu.Control Control;
        public VRCExpressionsMenu ParentMenu;
        public List<MenuItemNode> Children = new List<MenuItemNode>();
    }

    public class MenuMoveOperation
    {
        public string FromPath;
        public string ToPath;

        public MenuMoveOperation(string fromPath, string toPath)
        {
            FromPath = fromPath;
            ToPath = toPath;
        }
    }

    /// <summary>
    /// Expression menu mapper and hierarchy builder using VRCFury menu estimation capabilities.
    /// </summary>
    public static class VRCFuryMenuMapper
    {
        public static VRCExpressionsMenu GetMergedMenu(GameObject avatarObj)
        {
            if (avatarObj == null || !Utils.TryInitialize()) return null;

            try
            {
                if (!ReflectionHelper.TryInvokeMethod(Utils.VFGameObjectType, "op_Implicit", out object vfGameObject, avatarObj) || vfGameObject == null)
                    return null;

                if (!Utils.EstimateMethod.TryInvoke(null, out object menuManager, vfGameObject) || menuManager == null)
                    return null;

                if (Utils.GetRawMethod.TryInvoke(menuManager, out VRCExpressionsMenu rawMenu))
                    return rawMenu;

                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VRCFuryMenuMapper] Failed to extract merged menu: {ex}");
                return null;
            }
        }

        public static MenuItemNode BuildMenuTree(GameObject avatarObj)
        {
            VRCExpressionsMenu rootMenu = GetMergedMenu(avatarObj);
            if (rootMenu == null) return null;

            MenuItemNode rootNode = new MenuItemNode { Name = "Main Menu", FullPath = "", ParentMenu = null };
            PopulateMenuTree(rootNode, rootMenu, "", new HashSet<VRCExpressionsMenu>());
            return rootNode;
        }

        private static void PopulateMenuTree(MenuItemNode parentNode, VRCExpressionsMenu currentMenu, string currentPath, HashSet<VRCExpressionsMenu> visitedMenus)
        {
            if (currentMenu == null || visitedMenus.Contains(currentMenu)) return;
            visitedMenus.Add(currentMenu);

            foreach (var control in currentMenu.controls)
            {
                if (control == null) continue;

                string itemPath = string.IsNullOrEmpty(currentPath) ? control.name : $"{currentPath}/{control.name}";
                MenuItemNode node = new MenuItemNode
                {
                    Name = control.name,
                    FullPath = itemPath,
                    Control = control,
                    ParentMenu = currentMenu
                };

                parentNode.Children.Add(node);

                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null)
                {
                    PopulateMenuTree(node, control.subMenu, itemPath, visitedMenus);
                }
            }
        }

        public static List<string> GetAllMenuPaths(GameObject avatarObj)
        {
            MenuItemNode root = BuildMenuTree(avatarObj);
            List<string> paths = new List<string>();
            if (root != null) CollectPaths(root, paths);
            return paths;
        }

        private static void CollectPaths(MenuItemNode node, List<string> paths)
        {
            foreach (var child in node.Children)
            {
                paths.Add(child.FullPath);
                CollectPaths(child, paths);
            }
        }
    }
}
