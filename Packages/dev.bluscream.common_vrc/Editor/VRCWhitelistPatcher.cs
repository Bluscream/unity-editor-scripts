using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRC
{
    /// <summary>
    /// Utility to inspect and patch VRChat SDK component whitelists dynamically at Editor startup.
    /// </summary>
    [InitializeOnLoad]
    public static class VRCWhitelistPatcher
    {
        private static readonly HashSet<string> _pendingTypesToWhitelist = new HashSet<string>();

        static VRCWhitelistPatcher()
        {
            // Auto-patch on Editor load / script recompile
            PerformWhitelistPatch();
        }

        /// <summary>
        /// Registers a component Type to be added to VRChat SDK's component whitelist.
        /// </summary>
        public static bool RegisterWhitelistedType(Type type)
        {
            if (type == null) return false;
            return RegisterWhitelistedTypeName(type.FullName ?? type.Name);
        }

        /// <summary>
        /// Registers a full type name (e.g. "MyNamespace.MyComponent") to be added to VRChat SDK's component whitelist.
        /// </summary>
        public static bool RegisterWhitelistedTypeName(string fullTypeName)
        {
            if (string.IsNullOrWhiteSpace(fullTypeName)) return false;
            _pendingTypesToWhitelist.Add(fullTypeName);
            return PerformWhitelistPatch();
        }

        /// <summary>
        /// Patches VRChat SDK whitelist arrays via reflection.
        /// </summary>
        public static bool PerformWhitelistPatch()
        {
            try
            {
                Type vrcAvatarValType = Type.GetType("VRC.SDKBase.Validation.AvatarValidation, VRC.SDKBase.Editor")
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                        .FirstOrDefault(t => t.FullName == "VRC.SDKBase.Validation.AvatarValidation");

                if (vrcAvatarValType == null) return false;

                FieldInfo commonListField = vrcAvatarValType.GetField("ComponentTypeWhiteListCommon", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo sdk3ListField   = vrcAvatarValType.GetField("ComponentTypeWhiteListSdk3", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                bool modified = false;

                if (commonListField != null)
                {
                    modified |= AppendToWhitelistField(commonListField, vrcAvatarValType, _pendingTypesToWhitelist);
                }

                if (sdk3ListField != null)
                {
                    modified |= AppendToWhitelistField(sdk3ListField, vrcAvatarValType, _pendingTypesToWhitelist);
                }

                // Reset SDK3 cached merged list if present
                Type sdk3AvatarValType = Type.GetType("VRC.SDK3.Validation.AvatarValidation, com.vrchat.avatars.Editor")
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                        .FirstOrDefault(t => t.FullName == "VRC.SDK3.Validation.AvatarValidation");

                if (sdk3AvatarValType != null)
                {
                    FieldInfo combinedField = sdk3AvatarValType.GetField("CombinedComponentTypeWhiteList", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (combinedField != null)
                    {
                        combinedField.SetValue(null, null); // Force recalculation on next SDK check
                    }
                }

                if (modified)
                {
                    Debug.Log($"<color=lime><b>[VRCWhitelistPatcher]</b></color> Successfully patched VRChat SDK whitelist ({_pendingTypesToWhitelist.Count} custom types registered).");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VRCWhitelistPatcher] Exception while patching VRChat SDK whitelist: {ex.Message}");
                return false;
            }
        }

        private static bool AppendToWhitelistField(FieldInfo field, Type targetType, HashSet<string> typesToAdd)
        {
            if (typesToAdd == null || typesToAdd.Count == 0) return false;

            object value = field.GetValue(null);
            if (value is string[] array)
            {
                var list = new List<string>(array);
                int added = 0;
                foreach (var t in typesToAdd)
                {
                    if (!list.Contains(t))
                    {
                        list.Add(t);
                        added++;
                    }
                }
                if (added > 0)
                {
                    field.SetValue(null, list.ToArray());
                    return true;
                }
            }
            else if (value is List<string> list)
            {
                int added = 0;
                foreach (var t in typesToAdd)
                {
                    if (!list.Contains(t))
                    {
                        list.Add(t);
                        added++;
                    }
                }
                return added > 0;
            }

            return false;
        }
    }
}
