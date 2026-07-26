using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static Bluscream.Utils;

namespace Bluscream.TextureCompressor
{
    /// <summary>
    /// Window for analyzing textures used on a GameObject and its children
    /// </summary>
    public class TextureUsageWindow : EditorWindow
    {
        private GameObject targetGameObject;
        private Vector2 scrollPosition;
        private List<TextureInfo> textureInfos = new List<TextureInfo>();
        private bool hasAnalyzed = false;
        private long totalTextureSize = 0;

        [System.Serializable]
        private class TextureInfo
        {
            public Texture2D texture;
            public string texturePath;
            public string materialName;
            public string gameObjectPath;
            public string propertyName;
            public int width;
            public int height;
            public long sizeInBytes;
            public TextureImporter importer;
            public string compressionInfo;
        }

        [MenuItem("Bluscream/Texture Compressor/Texture Usage Analyzer")]
        public static void ShowWindow()
        {
            TextureUsageWindow window = GetWindow<TextureUsageWindow>("Texture Usage Analyzer");
            window.minSize = new Vector2(700, 500);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Texture Usage Analyzer", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // GameObject selection
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Target GameObject", EditorStyles.boldLabel);
            
            // Drag and drop area
            Rect dropArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, targetGameObject != null ? $"Target: {targetGameObject.name}" : "Drag GameObject here or select in hierarchy", EditorStyles.helpBox);
            
            HandleDragAndDrop(dropArea);
            
            if (targetGameObject != null)
            {
                EditorGUILayout.BeginHorizontal();
                GameObject newTarget = (GameObject)EditorGUILayout.ObjectField("GameObject", targetGameObject, typeof(GameObject), true);
                if (newTarget != targetGameObject)
                {
                    targetGameObject = newTarget;
                    hasAnalyzed = false;
                }
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    targetGameObject = null;
                    hasAnalyzed = false;
                    textureInfos.Clear();
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                // Try to get from selection
                if (Selection.activeGameObject != null)
                {
                    if (GUILayout.Button($"Use Selected: {Selection.activeGameObject.name}"))
                    {
                        targetGameObject = Selection.activeGameObject;
                        hasAnalyzed = false;
                    }
                }
            }
            
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Analyze button
            EditorGUI.BeginDisabledGroup(targetGameObject == null);
            if (GUILayout.Button("Analyze Textures", GUILayout.Height(30)))
            {
                AnalyzeTextures();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(5);

            // Results
            if (hasAnalyzed)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Found {textureInfos.Count} texture(s)", EditorStyles.boldLabel);
                
                if (textureInfos.Count > 0)
                {
                    EditorGUILayout.LabelField($"Total Size: {FormatBytes(totalTextureSize)}");
                    EditorGUILayout.Space(5);

                    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

                    // Header
                    EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                    EditorGUILayout.LabelField("Texture", GUILayout.Width(200));
                    EditorGUILayout.LabelField("Material", GUILayout.Width(150));
                    EditorGUILayout.LabelField("Property", GUILayout.Width(100));
                    EditorGUILayout.LabelField("Size", GUILayout.Width(80));
                    EditorGUILayout.LabelField("Dimensions", GUILayout.Width(100));
                    EditorGUILayout.LabelField("Compression", GUILayout.ExpandWidth(true));
                    EditorGUILayout.EndHorizontal();

                    foreach (TextureInfo info in textureInfos)
                    {
                        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                        
                        // Texture name with ping button
                        EditorGUILayout.BeginHorizontal(GUILayout.Width(200));
                        EditorGUILayout.LabelField(info.texture != null ? info.texture.name : "Unknown", GUILayout.Width(170));
                        if (GUILayout.Button("...", GUILayout.Width(30)))
                        {
                            if (info.texture != null)
                            {
                                EditorGUIUtility.PingObject(info.texture);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                        
                        // Material name
                        EditorGUILayout.LabelField(info.materialName, GUILayout.Width(150));
                        
                        // Property name
                        EditorGUILayout.LabelField(info.propertyName, GUILayout.Width(100));
                        
                        // Size
                        EditorGUILayout.LabelField(FormatBytes(info.sizeInBytes), GUILayout.Width(80));
                        
                        // Dimensions
                        EditorGUILayout.LabelField($"{info.width}x{info.height}", GUILayout.Width(100));
                        
                        // Compression info
                        EditorGUILayout.LabelField(info.compressionInfo, EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(true));
                        
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    EditorGUILayout.HelpBox("No textures found on the selected GameObject.", MessageType.Info);
                }

                EditorGUILayout.EndVertical();
            }
        }

        private void HandleDragAndDrop(Rect dropArea)
        {
            Event currentEvent = Event.current;

            if (currentEvent.type == EventType.DragUpdated || currentEvent.type == EventType.DragPerform)
            {
                if (dropArea.Contains(currentEvent.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDrop.objectReferences.Length > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

                    if (currentEvent.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        if (DragAndDrop.objectReferences.Length > 0)
                        {
                            GameObject draggedObject = DragAndDrop.objectReferences[0] as GameObject;
                            if (draggedObject != null)
                            {
                                targetGameObject = draggedObject;
                                hasAnalyzed = false;
                            }
                        }

                        currentEvent.Use();
                    }
                }
            }
        }

        private void AnalyzeTextures()
        {
            if (targetGameObject == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a GameObject to analyze.", "OK");
                return;
            }

            textureInfos.Clear();
            hasAnalyzed = true;
            totalTextureSize = 0;

            try
            {
                EditorUtility.DisplayProgressBar("Analyzing Textures", "Collecting renderers...", 0f);

                // Get all renderers recursively
                Renderer[] renderers = targetGameObject.GetComponentsInChildren<Renderer>(true);
                HashSet<Texture2D> processedTextures = new HashSet<Texture2D>();

                int rendererCount = renderers.Length;
                for (int i = 0; i < rendererCount; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null) continue;

                    EditorUtility.DisplayProgressBar("Analyzing Textures", $"Processing {renderer.name}...", (float)i / rendererCount);

                    // Get all materials from this renderer
                    Material[] materials = renderer.sharedMaterials;
                    string gameObjectPath = GetGameObjectPath(renderer.gameObject);

                    foreach (Material material in materials)
                    {
                        if (material == null) continue;

                        // Get all texture properties from the material
                        Shader shader = material.shader;
                        if (shader == null) continue;

                        int propertyCount = UnityEditor.ShaderUtil.GetPropertyCount(shader);
                        for (int propIdx = 0; propIdx < propertyCount; propIdx++)
                        {
                            if (UnityEditor.ShaderUtil.GetPropertyType(shader, propIdx) == UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv)
                            {
                                string propertyName = UnityEditor.ShaderUtil.GetPropertyName(shader, propIdx);
                                Texture texture = material.GetTexture(propertyName);

                                if (texture is Texture2D texture2D && texture2D != null && !processedTextures.Contains(texture2D))
                                {
                                    processedTextures.Add(texture2D);
                                    AddTextureInfo(texture2D, material.name, gameObjectPath, propertyName);
                                }
                            }
                        }
                    }
                }

                // Sort by size (largest first)
                textureInfos = textureInfos.OrderByDescending(t => t.sizeInBytes).ToList();

                EditorUtility.ClearProgressBar();
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", $"Error analyzing textures: {e.Message}", "OK");
                Debug.LogError($"Texture usage analysis error: {e}\n{e.StackTrace}");
            }
        }

        private void AddTextureInfo(Texture2D texture, string materialName, string gameObjectPath, string propertyName)
        {
            try
            {
                string texturePath = AssetDatabase.GetAssetPath(texture);
                TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;

                int width = texture.width;
                int height = texture.height;
                
                // Calculate approximate size in bytes
                long sizeInBytes = CalculateTextureSize(texture, importer);

                // Get compression info
                string compressionInfo = GetCompressionInfo(importer);

                textureInfos.Add(new TextureInfo
                {
                    texture = texture,
                    texturePath = texturePath,
                    materialName = materialName,
                    gameObjectPath = gameObjectPath,
                    propertyName = propertyName,
                    width = width,
                    height = height,
                    sizeInBytes = sizeInBytes,
                    importer = importer,
                    compressionInfo = compressionInfo
                });

                totalTextureSize += sizeInBytes;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error processing texture {texture.name}: {e.Message}");
            }
        }

        private long CalculateTextureSize(Texture2D texture, TextureImporter importer)
        {
            if (importer == null)
            {
                // Fallback: estimate based on uncompressed size
                return texture.width * texture.height * 4; // RGBA32 = 4 bytes per pixel
            }

            try
            {
                // Get platform-specific settings
                #if UNITY_2018_1_OR_NEWER
                TextureImporterPlatformSettings platformSettings = importer.GetDefaultPlatformTextureSettings();
                #else
                TextureImporterPlatformSettings platformSettings = new TextureImporterPlatformSettings();
                platformSettings.name = "DefaultTexturePlatform";
                #endif

                int width = texture.width;
                int height = texture.height;
                int maxSize = platformSettings.maxTextureSize > 0 ? platformSettings.maxTextureSize : importer.maxTextureSize;
                
                // Clamp to max size
                if (width > maxSize) width = maxSize;
                if (height > maxSize) height = maxSize;

                // Calculate bytes based on format
                int bytesPerPixel = GetBytesPerPixel(platformSettings.format, importer.textureType);
                long pixelCount = (long)width * height;
                
                return pixelCount * bytesPerPixel;
            }
            catch
            {
                // Fallback
                return texture.width * texture.height * 4;
            }
        }

        private int GetBytesPerPixel(TextureImporterFormat format, TextureImporterType textureType)
        {
            // Approximate bytes per pixel for common formats
            switch (format)
            {
                case TextureImporterFormat.RGB24:
                case TextureImporterFormat.Alpha8:
                    return 3;
                case TextureImporterFormat.RGBA32:
                case TextureImporterFormat.ARGB32:
                    return 4;
                case TextureImporterFormat.RGB16:
                    return 2;
                case TextureImporterFormat.RGBA16:
                    return 2;
                case TextureImporterFormat.R8:
                    return 1;
                case TextureImporterFormat.RG16:
                    return 2;
                case TextureImporterFormat.RGB48:
                    return 6;
                case TextureImporterFormat.RGBA64:
                    return 8;
                case TextureImporterFormat.DXT1:
                case TextureImporterFormat.BC4:
                    return 1; // 4 bits per pixel compressed
                case TextureImporterFormat.DXT5:
                case TextureImporterFormat.BC5:
                case TextureImporterFormat.BC7:
                    return 1; // 8 bits per pixel compressed
                case TextureImporterFormat.PVRTC_RGB2:
                case TextureImporterFormat.PVRTC_RGBA2:
                    return 1; // 2 bits per pixel compressed
                case TextureImporterFormat.PVRTC_RGB4:
                case TextureImporterFormat.PVRTC_RGBA4:
                    return 1; // 4 bits per pixel compressed
                case TextureImporterFormat.ETC_RGB4:
                case TextureImporterFormat.ETC2_RGB4:
                    return 1; // 4 bits per pixel compressed
                case TextureImporterFormat.ETC2_RGBA8:
                    return 1; // 8 bits per pixel compressed
                case TextureImporterFormat.ASTC_4x4:
                case TextureImporterFormat.ASTC_5x5:
                case TextureImporterFormat.ASTC_6x6:
                case TextureImporterFormat.ASTC_8x8:
                case TextureImporterFormat.ASTC_10x10:
                case TextureImporterFormat.ASTC_12x12:
                    return 1; // Variable bits per pixel compressed
                default:
                    // Default to 4 bytes per pixel (RGBA32)
                    return 4;
            }
        }

        private string GetCompressionInfo(TextureImporter importer)
        {
            if (importer == null)
                return "No importer";

            try
            {
                List<string> infoParts = new List<string>();

                // Compression type
                infoParts.Add($"Compression: {importer.textureCompression}");

                // Max size
                infoParts.Add($"Max Size: {importer.maxTextureSize}");

                // Platform settings
                #if UNITY_2018_1_OR_NEWER
                TextureImporterPlatformSettings platformSettings = importer.GetDefaultPlatformTextureSettings();
                infoParts.Add($"Format: {platformSettings.format}");
                if (platformSettings.crunchedCompression)
                {
                    infoParts.Add($"Crunch: {platformSettings.compressionQuality}%");
                }
                #else
                infoParts.Add("Format: (Check manually)");
                #endif

                return string.Join(" | ", infoParts);
            }
            catch
            {
                return "Error reading compression info";
            }
        }
    }
}
