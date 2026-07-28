using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using UnityEngine;

namespace Bluscream
{
    /// <summary>
    /// Extension methods for System Enums.
    /// </summary>
    public static class EnumExtensions
    {
        /// <summary>
        /// Returns the [Description] attribute string for an enum value, or falls back to .ToString().
        /// </summary>
        public static string GetDescription<T>(this T value) where T : Enum
        {
            FieldInfo field = typeof(T).GetField(value.ToString());
            if (field == null) return value.ToString();
            DescriptionAttribute attr = (DescriptionAttribute)Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute));
            return attr != null ? attr.Description : value.ToString();
        }
    }

    /// <summary>
    /// Extension methods for Transform and GameObject hierarchy operations.
    /// </summary>
    public static class TransformExtensions
    {
        /// <summary>
        /// Returns how many ancestors this transform has (root = 0).
        /// </summary>
        public static int GetHierarchyDepth(this Transform t)
        {
            int depth = 0;
            while (t != null) { depth++; t = t.parent; }
            return depth;
        }

        /// <summary>
        /// Recursively counts all descendant transforms (including self).
        /// </summary>
        public static int CountDescendants(this Transform t)
        {
            if (t == null) return 0;
            int count = 1;
            for (int i = 0; i < t.childCount; i++)
                count += t.GetChild(i).CountDescendants();
            return count;
        }

        /// <summary>
        /// Collects all GameObjects in the hierarchy rooted at parent (including itself).
        /// </summary>
        public static List<GameObject> CollectAllGameObjects(this Transform parent)
        {
            var result = new List<GameObject>();
            CollectRecursive(parent, result);
            return result;
        }

        private static void CollectRecursive(Transform t, List<GameObject> list)
        {
            if (t == null) return;
            list.Add(t.gameObject);
            foreach (Transform child in t)
                CollectRecursive(child, list);
        }
    }
}
