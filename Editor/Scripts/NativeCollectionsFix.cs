using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.NativeCollectionsFix {
    /// <summary>
    /// Ensures ENABLE_UNITY_COLLECTIONS_CHECKS is defined for all build targets
    /// to fix NativeList compilation errors when IL2CPP is not installed.
    /// </summary>
    [InitializeOnLoad]
    public static class NativeCollectionsDefineSetter {
        private const string k_Define = "ENABLE_UNITY_COLLECTIONS_CHECKS";

        static NativeCollectionsDefineSetter() {
            var targets = Enum.GetValues(typeof(BuildTargetGroup))
                .Cast<BuildTargetGroup>()
                .Where(x => x != BuildTargetGroup.Unknown)
                .Where(x => !IsObsolete(x));

            bool anyChanges = false;
            foreach (var target in targets) {
#if UNITY_2021_3_OR_NEWER
                var namedTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(target);
                var defines = PlayerSettings.GetScriptingDefineSymbols(namedTarget).Trim();
#else
                var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(target).Trim();
#endif

                var list = defines.Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();

                if (list.Contains(k_Define))
                    continue;

                list.Add(k_Define);
                defines = string.Join(";", list);

#if UNITY_2021_3_OR_NEWER
                PlayerSettings.SetScriptingDefineSymbols(namedTarget, defines);
#else
                PlayerSettings.SetScriptingDefineSymbolsForGroup(target, defines);
#endif
                anyChanges = true;
            }

            if (anyChanges) {
                Debug.Log($"[NativeCollectionsFix] Added {k_Define} to all build target scripting define symbols.");
            }
        }

        static bool IsObsolete(BuildTargetGroup group) {
            var field = typeof(BuildTargetGroup).GetField(group.ToString());
            if (field == null) return false;
            var attrs = field.GetCustomAttributes(typeof(ObsoleteAttribute), false);
            return attrs != null && attrs.Length > 0;
        }
    }
}
