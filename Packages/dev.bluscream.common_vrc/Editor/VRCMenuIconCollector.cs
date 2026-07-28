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
        /// <summary>A texture that ships with the avatar but is not reachable through any Renderer.</summary>
        public class CollectedTexture
        {
            public TextureImporter Importer;
            /// <summary>True for menu thumbnails — safe to cap hard. False for unclassified references.</summary>
            public bool IsMenuIcon;
            public string Source;
        }

        /// <summary>Returns the TextureImporters for every icon reachable from the avatar's expressions menu.</summary>
        public static List<TextureImporter> CollectMenuIconImporters(GameObject avatarRoot)
        {
            return CollectNonRendererTextures(avatarRoot).Where(c => c.IsMenuIcon).Select(c => c.Importer).ToList();
        }

        /// <summary>
        /// Every texture that ships with the avatar but hangs off no Renderer:
        ///   • icons in the descriptor's VRCExpressionsMenu tree
        ///   • textures referenced by ANY component on the avatar — this is what catches menu entries
        ///     that non-destructive tools (VRCFury toggles, "Override Menu Icon", menu-item context
        ///     options) only merge into the menu at build time, long after we scan.
        ///   • textures reachable through materials those components reference (material swaps)
        ///
        /// Component references are scanned generically via SerializedObject rather than by targeting
        /// specific VRCFury types, so tools we do not know about are covered too. Anything whose
        /// property name looks like an icon is treated as one; the rest are budgeted at neutral
        /// importance and left uncapped, since we cannot tell what they are used for.
        /// </summary>
        public static List<CollectedTexture> CollectNonRendererTextures(GameObject avatarRoot)
        {
            var result = new List<CollectedTexture>();
            var seenPaths = new HashSet<string>();
            if (avatarRoot == null) return result;

            void Add(Texture tex, bool isIcon, string source)
            {
                if (tex == null) return;
                string path = AssetDatabase.GetAssetPath(tex);
                if (string.IsNullOrEmpty(path) || !seenPaths.Add(path)) return;
                if (AssetImporter.GetAtPath(path) is TextureImporter imp)
                    result.Add(new CollectedTexture { Importer = imp, IsMenuIcon = isIcon, Source = source });
            }

            // 1. Icons declared in the expressions menu asset tree
            foreach (Texture tex in CollectMenuIcons(avatarRoot))
                Add(tex, true, "expressions menu");

            // 2. Textures (and materials) referenced by components — catches build-time menu injection
            try
            {
                foreach (Component comp in avatarRoot.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null || comp is Renderer || comp is Transform) continue;

                    SerializedObject so;
                    try { so = new SerializedObject(comp); } catch { continue; }

                    SerializedProperty prop = so.GetIterator();
                    while (prop.NextVisible(true))
                    {
                        if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                        UnityEngine.Object obj = prop.objectReferenceValue;
                        if (obj == null) continue;

                        bool looksLikeIcon = prop.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0
                                          || prop.propertyPath.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (obj is Texture tex)
                        {
                            Add(tex, looksLikeIcon, $"{comp.GetType().Name}.{prop.name}");
                        }
                        else if (obj is Material mat && mat.shader != null)
                        {
                            // Material swaps reference materials whose textures may be on no renderer yet
                            int count = ShaderUtil.GetPropertyCount(mat.shader);
                            for (int i = 0; i < count; i++)
                            {
                                if (ShaderUtil.GetPropertyType(mat.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                                Add(mat.GetTexture(ShaderUtil.GetPropertyName(mat.shader, i)), false, $"{comp.GetType().Name} → {mat.name}");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRCMenuIconCollector] Component texture scan failed: {e.Message}");
            }

            return result;
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
