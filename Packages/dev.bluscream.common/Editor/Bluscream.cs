using System;
using UnityEditor;
using UnityEngine;

namespace Bluscream
{
    /// <summary>
    /// Top-level utility functions for Bluscream packages
    /// </summary>
    public static class Utils
    {
        /// <summary>
        /// Gets the full path of a GameObject in the hierarchy
        /// </summary>
        public static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return "";
            
            string path = obj.name;
            Transform current = obj.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        /// <summary>
        /// Serializes a component to a list of property entries (dictionary-like structure)
        /// </summary>
        public static System.Collections.Generic.List<BackupSystem.ComponentPropertyEntry> SerializeComponentToPropertyList(SerializedObject so)
        {
            System.Collections.Generic.List<BackupSystem.ComponentPropertyEntry> entries = 
                new System.Collections.Generic.List<BackupSystem.ComponentPropertyEntry>();
            
            try
            {
                SerializedProperty prop = so.GetIterator();
                bool enterChildren = true;
                
                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    
                    // Skip default Unity properties we don't need
                    if (prop.name == "m_ObjectHideFlags" || prop.name == "m_CorrespondingSourceObject" || 
                        prop.name == "m_PrefabInstance" || prop.name == "m_PrefabAsset")
                        continue;
                    
                    // Skip properties that can't be serialized
                    bool canSerialize = true;
                    string value = null;
                    string typeName = prop.propertyType.ToString();
                    
                    switch (prop.propertyType)
                    {
                        case SerializedPropertyType.Integer:
                            value = prop.intValue.ToString();
                            break;
                        case SerializedPropertyType.Boolean:
                            value = prop.boolValue ? "true" : "false";
                            break;
                        case SerializedPropertyType.Float:
                            value = prop.floatValue.ToString();
                            break;
                        case SerializedPropertyType.String:
                            value = prop.stringValue;
                            break;
                        case SerializedPropertyType.Color:
                            Color c = prop.colorValue;
                            value = $"{c.r},{c.g},{c.b},{c.a}";
                            break;
                        case SerializedPropertyType.Vector2:
                            Vector2 v2 = prop.vector2Value;
                            value = $"{v2.x},{v2.y}";
                            break;
                        case SerializedPropertyType.Vector3:
                            Vector3 v3 = prop.vector3Value;
                            value = $"{v3.x},{v3.y},{v3.z}";
                            break;
                        case SerializedPropertyType.Vector4:
                            Vector4 v4 = prop.vector4Value;
                            value = $"{v4.x},{v4.y},{v4.z},{v4.w}";
                            break;
                        case SerializedPropertyType.Quaternion:
                            Quaternion q = prop.quaternionValue;
                            value = $"{q.x},{q.y},{q.z},{q.w}";
                            break;
                        case SerializedPropertyType.Rect:
                            Rect rect = prop.rectValue;
                            value = $"{rect.x},{rect.y},{rect.width},{rect.height}";
                            break;
                        case SerializedPropertyType.Bounds:
                            Bounds bounds = prop.boundsValue;
                            value = $"{bounds.center.x},{bounds.center.y},{bounds.center.z},{bounds.size.x},{bounds.size.y},{bounds.size.z}";
                            break;
                        case SerializedPropertyType.ObjectReference:
                            if (prop.objectReferenceValue != null)
                            {
                                string path = AssetDatabase.GetAssetPath(prop.objectReferenceValue);
                                if (string.IsNullOrEmpty(path))
                                {
                                    // Scene object reference - use instance ID
                                    path = prop.objectReferenceValue.GetInstanceID().ToString();
                                }
                                value = path;
                            }
                            else
                            {
                                value = "null";
                            }
                            break;
                        case SerializedPropertyType.Enum:
                            if (prop.enumNames != null && prop.enumNames.Length > prop.enumValueIndex && prop.enumValueIndex >= 0)
                            {
                                value = prop.enumNames[prop.enumValueIndex];
                            }
                            else
                            {
                                value = prop.enumValueIndex.ToString();
                            }
                            break;
                        case SerializedPropertyType.ArraySize:
                            value = prop.intValue.ToString();
                            break;
                        case SerializedPropertyType.LayerMask:
                            value = prop.intValue.ToString();
                            break;
                        case SerializedPropertyType.AnimationCurve:
                        case SerializedPropertyType.Gradient:
                        case SerializedPropertyType.Generic:
                            // Skip complex types that can't be easily serialized
                            canSerialize = false;
                            break;
                        default:
                            // For unknown types, try to get string value only if supported
                            try
                            {
                                if (prop.hasChildren)
                                {
                                    // Skip complex nested structures
                                    canSerialize = false;
                                }
                                else
                                {
                                    // Try to get string value, but catch if not supported
                                    value = prop.stringValue;
                                }
                            }
                            catch
                            {
                                // Property type doesn't support stringValue, skip it
                                canSerialize = false;
                            }
                            break;
                    }
                    
                    // Only add if we can serialize this property
                    if (canSerialize && value != null)
                    {
                        entries.Add(new BackupSystem.ComponentPropertyEntry(prop.name, value, typeName));
                    }
                }
            }
            catch
            {
                // Return empty list on error
            }
            
            return entries;
        }

        /// <summary>
        /// Manually serializes a component by iterating through its SerializedProperties
        /// </summary>
        public static string SerializeComponentManually(SerializedObject so)
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("{");
                bool first = true;
                
                SerializedProperty prop = so.GetIterator();
                bool enterChildren = true;
                
                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    
                    // Skip default Unity properties we don't need
                    if (prop.name == "m_ObjectHideFlags" || prop.name == "m_CorrespondingSourceObject" || 
                        prop.name == "m_PrefabInstance" || prop.name == "m_PrefabAsset")
                        continue;
                    
                    // Skip properties that can't be serialized
                    bool canSerialize = true;
                    string value = null;
                    
                    switch (prop.propertyType)
                    {
                        case SerializedPropertyType.Integer:
                            value = prop.intValue.ToString();
                            break;
                        case SerializedPropertyType.Boolean:
                            value = prop.boolValue ? "true" : "false";
                            break;
                        case SerializedPropertyType.Float:
                            value = prop.floatValue.ToString();
                            break;
                        case SerializedPropertyType.String:
                            value = $"\"{EscapeJsonString(prop.stringValue)}\"";
                            break;
                        case SerializedPropertyType.Color:
                            Color c = prop.colorValue;
                            value = $"\"{c.r},{c.g},{c.b},{c.a}\"";
                            break;
                        case SerializedPropertyType.Vector2:
                            Vector2 v2 = prop.vector2Value;
                            value = $"\"{v2.x},{v2.y}\"";
                            break;
                        case SerializedPropertyType.Vector3:
                            Vector3 v3 = prop.vector3Value;
                            value = $"\"{v3.x},{v3.y},{v3.z}\"";
                            break;
                        case SerializedPropertyType.Vector4:
                            Vector4 v4 = prop.vector4Value;
                            value = $"\"{v4.x},{v4.y},{v4.z},{v4.w}\"";
                            break;
                        case SerializedPropertyType.Quaternion:
                            Quaternion q = prop.quaternionValue;
                            value = $"\"{q.x},{q.y},{q.z},{q.w}\"";
                            break;
                        case SerializedPropertyType.Rect:
                            Rect rect = prop.rectValue;
                            value = $"\"{rect.x},{rect.y},{rect.width},{rect.height}\"";
                            break;
                        case SerializedPropertyType.Bounds:
                            Bounds bounds = prop.boundsValue;
                            value = $"\"{bounds.center.x},{bounds.center.y},{bounds.center.z},{bounds.size.x},{bounds.size.y},{bounds.size.z}\"";
                            break;
                        case SerializedPropertyType.ObjectReference:
                            if (prop.objectReferenceValue != null)
                            {
                                string path = AssetDatabase.GetAssetPath(prop.objectReferenceValue);
                                if (string.IsNullOrEmpty(path))
                                {
                                    // Scene object reference - use instance ID
                                    path = prop.objectReferenceValue.GetInstanceID().ToString();
                                }
                                value = $"\"{EscapeJsonString(path)}\"";
                            }
                            else
                            {
                                value = "null";
                            }
                            break;
                        case SerializedPropertyType.Enum:
                            if (prop.enumNames != null && prop.enumNames.Length > prop.enumValueIndex && prop.enumValueIndex >= 0)
                            {
                                value = $"\"{EscapeJsonString(prop.enumNames[prop.enumValueIndex])}\"";
                            }
                            else
                            {
                                value = prop.enumValueIndex.ToString();
                            }
                            break;
                        case SerializedPropertyType.ArraySize:
                            value = prop.intValue.ToString();
                            break;
                        case SerializedPropertyType.LayerMask:
                            value = prop.intValue.ToString();
                            break;
                        case SerializedPropertyType.AnimationCurve:
                        case SerializedPropertyType.Gradient:
                        case SerializedPropertyType.Generic:
                            // Skip complex types that can't be easily serialized
                            canSerialize = false;
                            break;
                        default:
                            // For unknown types, try to get string value only if supported
                            try
                            {
                                if (prop.hasChildren)
                                {
                                    // Skip complex nested structures
                                    canSerialize = false;
                                }
                                else
                                {
                                    // Try to get string value, but catch if not supported
                                    string strVal = prop.stringValue;
                                    value = $"\"{EscapeJsonString(strVal)}\"";
                                }
                            }
                            catch
                            {
                                // Property type doesn't support stringValue, skip it
                                canSerialize = false;
                            }
                            break;
                    }
                    
                    // Only append if we can serialize this property
                    if (canSerialize && value != null)
                    {
                        if (!first) sb.Append(",");
                        first = false;
                        sb.Append($"\"{prop.name}\":{value}");
                    }
                }
                
                sb.Append("}");
                return sb.ToString();
            }
            catch
            {
                return "{}";
            }
        }
        
        /// <summary>
        /// Escapes special characters in JSON strings
        /// </summary>
        public static string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
        
        /// <summary>
        /// Parses a color string to Color
        /// </summary>
        public static bool TryParseColor(string str, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(str)) return false;
            
            // Format: RGBA(0.5, 0.5, 0.5, 1.0) or (0.5, 0.5, 0.5, 1.0)
            str = str.Trim();
            if (str.StartsWith("RGBA(")) str = str.Substring(5);
            if (str.StartsWith("(")) str = str.Substring(1);
            if (str.EndsWith(")")) str = str.Substring(0, str.Length - 1);
            
            string[] parts = str.Split(',');
            if (parts.Length >= 3)
            {
                if (float.TryParse(parts[0].Trim(), out float r) &&
                    float.TryParse(parts[1].Trim(), out float g) &&
                    float.TryParse(parts[2].Trim(), out float b))
                {
                    float a = 1.0f;
                    if (parts.Length >= 4)
                        float.TryParse(parts[3].Trim(), out a);
                    
                    color = new Color(r, g, b, a);
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// Parses a vector string to Vector4
        /// </summary>
        public static bool TryParseVector(string str, out Vector4 vector)
        {
            vector = Vector4.zero;
            if (string.IsNullOrEmpty(str)) return false;
            
            // Format: (0.5, 0.5, 0.5, 1.0)
            str = str.Trim();
            if (str.StartsWith("(")) str = str.Substring(1);
            if (str.EndsWith(")")) str = str.Substring(0, str.Length - 1);
            
            string[] parts = str.Split(',');
            if (parts.Length >= 2)
            {
                if (float.TryParse(parts[0].Trim(), out float x) &&
                    float.TryParse(parts[1].Trim(), out float y))
                {
                    float z = 0f;
                    float w = 0f;
                    if (parts.Length >= 3)
                        float.TryParse(parts[2].Trim(), out z);
                    if (parts.Length >= 4)
                        float.TryParse(parts[3].Trim(), out w);
                    
                    vector = new Vector4(x, y, z, w);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if the BackupSystem package (dev.bluscream.backupsystem) is available
        /// </summary>
        private static bool? _isBackupSystemAvailable = null;

        public static bool IsBackupSystemAvailable()
        {
            if (_isBackupSystemAvailable.HasValue)
                return _isBackupSystemAvailable.Value;

            try
            {
                // Try to find the BackupSystem namespace/class
                System.Type backupSystemType = System.Type.GetType("Bluscream.BackupSystem.BackupSystem, Assembly-CSharp-Editor");
                if (backupSystemType == null)
                {
                    // Try alternative assembly name
                    backupSystemType = System.Type.GetType("Bluscream.BackupSystem.BackupSystem");
                }
                
                _isBackupSystemAvailable = backupSystemType != null;
                return _isBackupSystemAvailable.Value;
            }
            catch
            {
                _isBackupSystemAvailable = false;
                return false;
            }
        }
    }
}
