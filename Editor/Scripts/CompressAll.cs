using UnityEditor;
using UnityEngine;


public class TextureCompressionEditor: EditorWindow {
    public class CompressionSettings {
        public int maxTextureSize = 1024;
        public string resizeAlgorithm = "Mitchell";
        public TextureImporterFormat format = TextureImporterFormat.RGBA32;
        public TextureImporterCompression compression = TextureImporterCompression.Compressed;
        public bool useCrunchCompression = false;
        public int compressorQuality = 50;
        public string[] overrides = {};
    }
    private CompressionSettings normalMapsCompressionSettings = new CompressionSettings();
    private CompressionSettings remainingTexturesCompressionSettings = new CompressionSettings();
    // private TextureImporterType[] normalMapTypes = new { TextureImporterType.Bump, TextureImporterType.NormalMap };

    [MenuItem("Window/Texture Compression Editor")]
    public static void ShowWindow() {
    TextureCompressionEditor window = GetWindow < TextureCompressionEditor > ();
    window.titleContent = new GUIContent("Texture Compression Editor");
    window.Show();
    }

    private void CreateCompressionSettingsPanel(string name, CompressionSettings settings) {
    EditorGUILayout.LabelField($"{name} Settings", EditorStyles.boldLabel);
    settings.maxTextureSize = EditorGUILayout.IntField("Max Texture Size", settings.maxTextureSize);
    settings.resizeAlgorithm = EditorGUILayout.TextField("Resize Algorithm", settings.resizeAlgorithm);
    settings.format = (TextureImporterFormat) EditorGUILayout.EnumPopup("Format", settings.format);
    settings.compression = (TextureImporterCompression) EditorGUILayout.EnumPopup("Compression", settings.compression);
    settings.useCrunchCompression = EditorGUILayout.Toggle("Use Crunch Compression", settings.useCrunchCompression);
    settings.compressorQuality = EditorGUILayout.IntSlider("Compressor Quality", settings.compressorQuality, 0, 100);
    settings.resizeAlgorithm = EditorGUILayout.TextField("Resize Algorithm", settings.resizeAlgorithm);
    }

    private void OnGUI() {
    EditorGUILayout.LabelField("Compression Settings", EditorStyles.boldLabel);

    CreateCompressionSettingsPanel("Normal Maps Settings", normalMapsCompressionSettings);
    EditorGUILayout.Space();
    CreateCompressionSettingsPanel("Other Textures Settings", remainingTexturesCompressionSettings);

    if (GUILayout.Button("Apply Compression Settings")) {
        ApplyCompressionSettings();
    }
    }

    private void ApplyCompressionSettings() {
    string[] textureGUIDs = AssetDatabase.FindAssets("t:Texture");
    foreach(string textureGUID in textureGUIDs)
        {
            string texturePath = AssetDatabase.GUIDToAssetPath(textureGUID);
            TextureImporter textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            CompressionSettings settings;
            if (textureImporter != null)
            {
                Debug.Log(textureImporter.textureType);
                if (textureImporter.textureType == TextureImporterType.NormalMap)
                {
                    settings = normalMapsCompressionSettings;
                }
                else
                {
                    settings = remainingTexturesCompressionSettings;
                }
                textureImporter.textureCompression = settings.compression;
                textureImporter.maxTextureSize = settings.maxTextureSize;
                foreach(string _override in settings.overrides) {
                    textureImporter.SetPlatformTextureSettings(_override, settings.maxTextureSize, settings.format, settings.compressorQuality, settings.useCrunchCompression);
                }
                textureImporter.SaveAndReimport();
                }
           }
    }
}