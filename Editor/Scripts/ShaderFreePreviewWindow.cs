using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ShaderFreePreviewWindow : EditorWindow
{
    private Camera previewCamera;
    private RenderTexture renderTexture;
    private Texture2D displayTexture;
    private Scene? previewScene; // Make it nullable
    private List<GameObject> originalObjects = new List<GameObject>();
    private List<GameObject> previewInstances = new List<GameObject>();
    private Material standardMaterial;
    private float rotationSpeed = 50f;
    private float zoomSpeed = 2f;
    private float minZoomDistance = 2f;
    private float maxZoomDistance = 15f;
    private float currentZoomDistance = 5f;
    private Vector2 lastMousePosition;

    [MenuItem("Window/Shader-Free Preview")]
    static void OpenWindow()
    {
        GetWindow<ShaderFreePreviewWindow>("Shader-Free Preview");
    }

    private void OnEnable()
    {
        InitializePreview();
    }

    private void InitializePreview()
    {
        try
        {
            previewScene = EditorSceneManager.NewPreviewScene();
            GameObject cameraObj = new GameObject("Preview Camera");
            previewCamera = cameraObj.AddComponent<Camera>();
            previewCamera.transform.position = new Vector3(
                0,
                currentZoomDistance,
                -currentZoomDistance
            );
            previewCamera.transform.LookAt(Vector3.zero);
            previewCamera.cameraType = CameraType.Preview;
            previewCamera.scene = previewScene.Value;
            renderTexture = new RenderTexture(1024, 768, 16);
            displayTexture = new Texture2D(
                renderTexture.width,
                renderTexture.height,
                TextureFormat.RGBA32,
                false
            );
            Shader standardShader = Shader.Find("Standard");
            if (standardShader != null)
            {
                standardMaterial = new Material(standardShader);
            }
            else
            {
                Debug.LogWarning("Standard shader not found, using default material");
                standardMaterial = new Material(Shader.Find("Diffuse"));
            }
            previewInstances = new List<GameObject>();
            originalObjects = new List<GameObject>();
            if (Selection.objects != null && Selection.objects.Length > 0)
            {
                UpdatePreviewObjects();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error initializing preview: {e.Message}\n{e.StackTrace}");
        }
    }

    private void UpdatePreviewObjects()
    {
        if (previewInstances != null)
        {
            foreach (GameObject obj in previewInstances)
            {
                if (obj != null)
                {
                    GameObject.DestroyImmediate(obj);
                }
            }
            previewInstances.Clear();
        }
        if (Selection.objects.Length > 0 && previewScene.HasValue)
        {
            foreach (UnityEngine.Object selectedObj in Selection.objects)
            {
                if (selectedObj is GameObject gameObject)
                {
                    GameObject instance = null;
                    #if UNITY_2018_3_OR_NEWER
                    // Unity 2018.3+ uses InstantiatePrefab with scene parameter
                    instance = PrefabUtility.InstantiatePrefab(gameObject, previewScene.Value) as GameObject;
                    #else
                    // Older versions don't support scene parameter
                    instance = PrefabUtility.InstantiatePrefab(gameObject) as GameObject;
                    if (instance != null && previewScene.HasValue)
                    {
                        SceneManager.MoveGameObjectToScene(instance, previewScene.Value);
                    }
                    #endif
                    if (instance != null)
                    {
                        previewInstances.Add(instance);
                        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
                        if (renderers != null)
                        {
                            foreach (Renderer renderer in renderers)
                            {
                                if (renderer != null)
                                {
                                    renderer.material = standardMaterial;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private void OnSelectionChange()
    {
        UpdatePreviewObjects();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        rotationSpeed = EditorGUILayout.Slider(
            "Rotation Speed",
            rotationSpeed,
            10f,
            200f,
            GUILayout.Width(150)
        );
        zoomSpeed = EditorGUILayout.Slider("Zoom Speed", zoomSpeed, 0.5f, 5f, GUILayout.Width(150));
        EditorGUILayout.EndHorizontal();
        if (previewCamera != null && renderTexture != null)
        {
            HandleCameraControls(Event.current);

            previewCamera.targetTexture = renderTexture;
            previewCamera.Render();

            Graphics.CopyTexture(renderTexture, displayTexture);
            Rect texRect = EditorGUILayout.GetControlRect(
                GUILayout.ExpandWidth(true),
                GUILayout.Height(400)
            );
            EditorGUI.DrawPreviewTexture(texRect, displayTexture);
        }
    }

    private void HandleCameraControls(Event currentEvent)
    {
        if (currentEvent.type == EventType.MouseDown)
        {
            lastMousePosition = currentEvent.mousePosition;
        }
        else if (currentEvent.type == EventType.MouseDrag)
        {
            Vector2 delta = currentEvent.mousePosition - lastMousePosition;
            if (currentEvent.modifiers == EventModifiers.None)
            {
                float rotX = -delta.y * rotationSpeed * Time.deltaTime;
                float rotY = delta.x * rotationSpeed * Time.deltaTime;

                previewCamera.transform.RotateAround(
                    Vector3.zero,
                    previewCamera.transform.right,
                    rotX
                );
                previewCamera.transform.RotateAround(Vector3.zero, Vector3.up, rotY);
            }
            else if (currentEvent.modifiers == EventModifiers.Control)
            {
                currentZoomDistance -= delta.y * zoomSpeed * Time.deltaTime;
                currentZoomDistance = Mathf.Clamp(
                    currentZoomDistance,
                    minZoomDistance,
                    maxZoomDistance
                );

                Vector3 lookDirection = previewCamera.transform.rotation * Vector3.forward;
                previewCamera.transform.position =
                    Vector3.zero + lookDirection * currentZoomDistance;
            }

            lastMousePosition = currentEvent.mousePosition;
            currentEvent.Use();
        }
    }

    private void OnDisable()
    {
        Cleanup();
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        if (previewCamera != null && renderTexture != null)
        {
            previewCamera.targetTexture = null;
        }
        if (renderTexture != null)
        {
            renderTexture.Release();
            DestroyImmediate(renderTexture);
            renderTexture = null;
        }

        if (displayTexture != null)
        {
            DestroyImmediate(displayTexture);
            displayTexture = null;
        }

        if (standardMaterial != null)
        {
            DestroyImmediate(standardMaterial);
            standardMaterial = null;
        }
        if (previewInstances != null)
        {
            foreach (GameObject obj in previewInstances)
            {
                if (obj != null)
                {
                    GameObject.DestroyImmediate(obj);
                }
            }
            previewInstances.Clear();
        }
        if (previewScene.HasValue)
        {
            try
            {
                EditorSceneManager.ClosePreviewScene(previewScene.Value);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Error closing preview scene: {e.Message}");
            }
        }
        previewScene = null;
        previewCamera = null;
    }
}
