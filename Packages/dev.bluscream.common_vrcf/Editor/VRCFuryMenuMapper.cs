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
        private static readonly BluLog Log = BluLog.Get("VRCFuryMenuMapper");

        public static VRCExpressionsMenu GetMergedMenu(GameObject avatarObj)
        {
            if (avatarObj == null)
            {
                Log.Warn("GetMergedMenu: avatarObj is null.");
                return null;
            }

            if (!Utils.TryInitialize())
            {
                Log.Warn($"GetMergedMenu: Utils.TryInitialize() returned false. VRCFuryComponentType={Utils.VRCFuryComponentType != null}");
                return null;
            }

            try
            {
                if (Utils.VFGameObjectType == null)
                {
                    Log.Warn("GetMergedMenu: VFGameObjectType is null.");
                    return null;
                }

                // Try converting GameObject to VFGameObject via implicit operator or constructor
                object vfGameObject = null;
                var opImplicit = Utils.VFGameObjectType.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(GameObject) }, null);
                if (opImplicit != null)
                {
                    vfGameObject = opImplicit.Invoke(null, new object[] { avatarObj });
                }
                else
                {
                    var ctor = Utils.VFGameObjectType.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(GameObject) }, null);
                    if (ctor != null) vfGameObject = ctor.Invoke(new object[] { avatarObj });
                }

                if (vfGameObject == null)
                {
                    Log.Warn($"GetMergedMenu: Failed to convert avatarObj '{avatarObj.name}' to VFGameObject.");
                    return null;
                }

                if (Utils.EstimateMethod == null)
                {
                    Log.Warn("GetMergedMenu: Utils.EstimateMethod is null.");
                    return null;
                }

                object menuManager = Utils.EstimateMethod.Invoke(null, new object[] { vfGameObject });
                if (menuManager == null)
                {
                    Log.Warn("GetMergedMenu: EstimateMethod returned null menuManager.");
                    return null;
                }

                if (Utils.GetRawMethod == null)
                {
                    Log.Warn("GetMergedMenu: Utils.GetRawMethod is null.");
                    return null;
                }

                var rawMenu = Utils.GetRawMethod.Invoke(menuManager, null) as VRCExpressionsMenu;
                if (rawMenu == null)
                {
                    Log.Warn("GetMergedMenu: GetRawMethod returned null rawMenu.");
                    return null;
                }

                Log.Info($"GetMergedMenu: Successfully extracted merged menu '{rawMenu.name}' with {rawMenu.controls?.Count ?? 0} root controls.");
                var clonedMenu = CloneMenu(rawMenu, new Dictionary<VRCExpressionsMenu, VRCExpressionsMenu>());
                ApplyMoveFeaturesFromAvatar(avatarObj, clonedMenu);
                return clonedMenu;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to extract merged menu: {ex}");
                return null;
            }
        }

        private static VRCExpressionsMenu CloneMenu(VRCExpressionsMenu source, Dictionary<VRCExpressionsMenu, VRCExpressionsMenu> clonedMap)
        {
            if (source == null) return null;
            if (clonedMap.TryGetValue(source, out var existing)) return existing;

            var clone = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            clone.name = source.name;
            clonedMap[source] = clone;

            if (source.controls != null)
            {
                foreach (var ctrl in source.controls)
                {
                    if (ctrl == null) continue;
                    var ctrlClone = new VRCExpressionsMenu.Control
                    {
                        name = ctrl.name,
                        icon = ctrl.icon,
                        type = ctrl.type,
                        parameter = ctrl.parameter != null ? new VRCExpressionsMenu.Control.Parameter { name = ctrl.parameter.name } : null,
                        value = ctrl.value,
                        style = ctrl.style,
                        subMenu = ctrl.subMenu != null ? CloneMenu(ctrl.subMenu, clonedMap) : null,
                        subParameters = ctrl.subParameters != null ? ctrl.subParameters.Select(p => new VRCExpressionsMenu.Control.Parameter { name = p.name }).ToArray() : null,
                        labels = ctrl.labels != null ? ctrl.labels.ToArray() : null
                    };
                    clone.controls.Add(ctrlClone);
                }
            }

            return clone;
        }

        private static void ApplyMoveFeaturesFromAvatar(GameObject avatarObj, VRCExpressionsMenu rootMenu)
        {
            if (avatarObj == null || rootMenu == null || !Utils.TryInitialize()) return;

            var vrcfComponents = avatarObj.GetComponentsInChildren(Utils.VRCFuryComponentType, true);
            foreach (var comp in vrcfComponents)
            {
                if (comp == null) continue;
                if (!ReflectionHelper.TryGetFieldValue(comp, "config", out object config) || config == null) continue;
                if (!ReflectionHelper.TryGetFieldValue(config, "features", out object featuresListObj) || featuresListObj == null) continue;
                if (!(featuresListObj is System.Collections.IEnumerable featuresEnumerable)) continue;

                foreach (var feature in featuresEnumerable)
                {
                    if (feature == null) continue;
                    string typeName = feature.GetType().Name;
                    if (typeName != "MoveMenuItem") continue;

                    if (ReflectionHelper.TryGetFieldValue(feature, "fromPath", out string fromPath) &&
                        ReflectionHelper.TryGetFieldValue(feature, "toPath", out string toPath) &&
                        !string.IsNullOrEmpty(fromPath))
                    {
                        ExecuteMove(rootMenu, fromPath, toPath);
                    }
                }
            }
        }

        private static void ExecuteMove(VRCExpressionsMenu rootMenu, string fromPath, string toPath)
        {
            if (rootMenu == null || string.IsNullOrEmpty(fromPath)) return;

            var fromSplit = fromPath.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (fromSplit.Count == 0) return;

            string fromName = fromSplit.Last();
            var parentMenu = FindParentMenu(rootMenu, fromSplit, 0);
            if (parentMenu == null || parentMenu.controls == null) return;

            var matchingControls = parentMenu.controls.Where(c => c != null && c.name == fromName).ToList();
            if (matchingControls.Count == 0) return;

            parentMenu.controls.RemoveAll(c => matchingControls.Contains(c));

            if (string.IsNullOrWhiteSpace(toPath)) return; // Move to empty string = delete

            var toSplit = toPath.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (toSplit.Count == 0) return;

            string targetName = toSplit.Last();
            var targetParentDir = toSplit.Take(toSplit.Count - 1).ToList();
            var targetMenu = GetOrCreateSubMenu(rootMenu, targetParentDir);

            foreach (var ctrl in matchingControls)
            {
                ctrl.name = targetName;
                targetMenu.controls.Add(ctrl);
            }
        }

        private static VRCExpressionsMenu FindParentMenu(VRCExpressionsMenu current, List<string> pathParts, int index)
        {
            if (current == null || current.controls == null) return null;
            if (index >= pathParts.Count - 1) return current;

            string part = pathParts[index];
            var ctrl = current.controls.FirstOrDefault(c => c != null && c.name == part && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.subMenu != null);
            if (ctrl == null) return null;

            return FindParentMenu(ctrl.subMenu, pathParts, index + 1);
        }

        private static VRCExpressionsMenu GetOrCreateSubMenu(VRCExpressionsMenu current, List<string> pathParts)
        {
            VRCExpressionsMenu menu = current;
            foreach (var part in pathParts)
            {
                var existingCtrl = menu.controls.FirstOrDefault(c => c != null && c.name == part && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.subMenu != null);
                if (existingCtrl != null)
                {
                    menu = existingCtrl.subMenu;
                }
                else
                {
                    var newSub = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    newSub.name = part;
                    var newCtrl = new VRCExpressionsMenu.Control
                    {
                        name = part,
                        type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                        subMenu = newSub
                    };
                    menu.controls.Add(newCtrl);
                    menu = newSub;
                }
            }
            return menu;
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
