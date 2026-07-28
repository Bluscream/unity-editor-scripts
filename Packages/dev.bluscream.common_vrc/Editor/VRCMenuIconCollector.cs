using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRC
{
    /// <summary>
    /// Collects the icon textures referenced by an avatar's VRCExpressionsMenu tree.
    ///
    /// These never appear on a Renderer, so material-walking texture collectors miss them entirely —
    /// yet they ARE serialized into the avatar bundle. On a menu-heavy avatar they can be the single
    /// largest block of texture data (measured: 263 icons, 10.8 MB uncompressed, 23% of the bundle),
    /// usually left as uncompressed RGBA32 because nothing ever sets a platform override on them.
    ///
    /// Uses reflection throughout so this package keeps no hard dependency on the Avatars SDK.
    /// </summary>
    public static class VRCMenuIconCollector
    {
        /// <summary>Returns the TextureImporters for every icon reachable from the avatar's expressions menu.</summary>
        public static List<TextureImporter> CollectMenuIconImporters(GameObject avatarRoot)
        {
            var importers = new List<TextureImporter>();
            var seenPaths = new HashSet<string>();

            foreach (Texture tex in CollectMenuIcons(avatarRoot))
            {
                string path = AssetDatabase.GetAssetPath(tex);
                if (string.IsNullOrEmpty(path) || !seenPaths.Add(path)) continue;
                if (AssetImporter.GetAtPath(path) is TextureImporter imp) importers.Add(imp);
            }
            return importers;
        }

        public static List<Texture> CollectMenuIcons(GameObject avatarRoot)
        {
            var icons = new List<Texture>();
            if (avatarRoot == null) return icons;

            try
            {
                Component descriptor = avatarRoot.GetComponentsInChildren<Component>(true)
                    .FirstOrDefault(c => c != null && c.GetType().Name == "VRCAvatarDescriptor");
                if (descriptor == null) return icons;

                object menu = GetMemberValue(descriptor, "expressionsMenu");
                if (menu == null) return icons;

                var visited = new HashSet<object>();
                WalkMenu(menu, icons, visited, 0);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRCMenuIconCollector] Could not collect expression menu icons: {e.Message}");
            }
            return icons;
        }

        private static void WalkMenu(object menu, List<Texture> icons, HashSet<object> visited, int depth)
        {
            if (menu == null || depth > 12 || !visited.Add(menu)) return;

            if (!(GetMemberValue(menu, "controls") is IEnumerable controls)) return;

            foreach (object control in controls)
            {
                if (control == null) continue;

                if (GetMemberValue(control, "icon") is Texture icon && icon != null)
                    icons.Add(icon);

                // Puppet controls carry up to four labelled sub-icons
                if (GetMemberValue(control, "labels") is IEnumerable labels)
                {
                    foreach (object label in labels)
                    {
                        if (label == null) continue;
                        if (GetMemberValue(label, "icon") is Texture labelIcon && labelIcon != null)
                            icons.Add(labelIcon);
                    }
                }

                object sub = GetMemberValue(control, "subMenu");
                if (sub != null) WalkMenu(sub, icons, visited, depth + 1);
            }
        }

        private static object GetMemberValue(object target, string name)
        {
            if (target == null) return null;
            Type t = target.GetType();

            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) return f.GetValue(target);

            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p != null ? p.GetValue(target) : null;
        }
    }
}
