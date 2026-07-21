#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityMeshDecimation.Internal;
using UnityMeshDecimation.Utility;
using UnityMesh = UnityEngine.Mesh;

namespace UnityMeshDecimation.UI {
    public class MeshDecimationEditor : EditorWindow {

        private static MeshDecimationEditor Window { get; set; }
        [MenuItem("Window/Unity Mesh Decimation")]
        static void Init() {
            if (Window == null) {
                Window = EditorWindow.GetWindow<MeshDecimationEditor>("Unity Mesh Decimation", true);
                Window.minSize = new Vector2(550, 400);
                Window.Show();
            }
            Window.Focus();
        }

        private Vector2 _scroll;
        private UnityMesh mesh;
        private int triangleCount;
        private int vertexCount;
        private TargetConditions targetConditions;
        private MeshDecimationProfile profile;
        private Editor profileEditor;
        private string outputPath;

        #region Editor Hooks
        private void OnEnable() {
            if (this.targetConditions == null) {
                targetConditions = new TargetConditions();
            }
            if (profile == null) {
                this.LoadDefaultProfile();
            }
        }

        private void OnGUI() {
            this._scroll = EditorGUILayout.BeginScrollView(this._scroll);

            EasyGUILayout.BeginRegion("Input");
            if(EasyGUILayout.ObjectField("Mesh", ref this.mesh)) {
                if (this.mesh) {
                    triangleCount = this.mesh.triangleCount();
                    vertexCount = this.mesh.vertexCount;
                }
                else {
                    triangleCount = 0;
                    vertexCount = 0;
                }
                if (targetConditions.faceCount > triangleCount) {
                    targetConditions.faceCount = triangleCount / 2;
                }
            }
            if (this.mesh) {
                EditorGUILayout.HelpBox($"triangles: {triangleCount}, vertices: {vertexCount}", MessageType.Info);
            }
            EasyGUILayout.EndRegion();

            EasyGUILayout.BeginRegion("End Condition");
            EasyGUILayout.IntField("Face Count", ref targetConditions.faceCount);
            EasyGUILayout.IntField("Vertex Count", ref targetConditions.vertexCount);
            EasyGUILayout.IntField("Max Operations", ref targetConditions.maxOperations);
            EasyGUILayout.FloatField("Max Error", ref targetConditions.maxMetrix);
            EasyGUILayout.FloatField("Max Time", ref targetConditions.maxTime);
            EasyGUILayout.EndRegion();

            EasyGUILayout.BeginRegion("Profile");
            EasyGUILayout.ObjectField(string.Empty, ref this.profile);
            if (this.profile) {
                Editor.CreateCachedEditor(this.profile, null, ref this.profileEditor);
                this.profileEditor.OnInspectorGUI();
            }
            EasyGUILayout.EndRegion();

            EasyGUILayout.BeginRegion("Output");
            string defaultPath = string.Empty;
            if (this.mesh != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(this.mesh);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    string directory = System.IO.Path.GetDirectoryName(assetPath);
                    string fileName = this.mesh.name;
                    defaultPath = System.IO.Path.Combine(directory, fileName + "_decimated").Replace("\\", "/");
                }
                else
                {
                    defaultPath = this.mesh.name + "_decimated";
                }
            }

            if(EasyGUILayout.FilePathField("Path", ref this.outputPath, "mesh", "Select", defaultPath)) {
                GUI.FocusControl(null);
            }
            EasyGUILayout.EndRegion();

            EditorGUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Execute")) {
                this.Execute();
            }
        }
        #endregion

        private void Execute() {
            if (this.mesh == null) {
                Debug.LogError("Mesh cannot be null");
                return;
            }
            if (string.IsNullOrEmpty(this.outputPath)) {
                Debug.LogError("Output path cannot be empty");
                return;
            }
            if (this.profile == null) {
                this.LoadDefaultProfile();
            }
            var oldMesh = AssetDatabase.LoadAssetAtPath<UnityMesh>(this.outputPath);
            var newMesh = this.profile.Optimize(this.mesh, this.targetConditions, oldMesh);
            if (this.profile.parameter.PreserveBlendShapes && this.mesh.blendShapeCount > 0)
            {
                MeshBlendShapeUtility.PreserveBlendShapes(this.mesh, newMesh);
            }
            if (oldMesh) {
                AssetDatabase.SaveAssets();
            }
            else {
                AssetDatabase.CreateAsset(newMesh, this.outputPath);
            }
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityMesh>(this.outputPath);
        }

        private void LoadDefaultProfile() {
            var profilePath = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("DefaultMeshDecimation t:MeshDecimationProfile")[0]);
            this.profile = AssetDatabase.LoadAssetAtPath<MeshDecimationProfile>(profilePath);
        }
	}
}
#endif