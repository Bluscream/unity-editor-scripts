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

    // Camera control variables
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
        // Create a new preview scene
        previewScene = EditorSceneManager.NewPreviewScene();
        
        // Create a camera for rendering
        GameObject cameraObj = new GameObject("Preview Camera");
        previewCamera = cameraObj.AddComponent<Camera>();
        previewCamera.transform.position = new Vector3(0, currentZoomDistance, -currentZoomDistance);
        previewCamera.transform.LookAt(Vector3.zero);
        previewCamera.cameraType = CameraType.Preview;
        previewCamera.scene = previewScene.Value;
        
        // Set up rendering textures
        renderTexture = new RenderTexture(1024, 768, 16);
        displayTexture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
        
        // Create Standard material instance
        standardMaterial = new Material(Shader.Find("Standard"));
        
        // Initialize empty lists
        previewInstances = new List<GameObject>();
        originalObjects = new List<GameObject>();
        
        // Call UpdatePreviewObjects only if there are selections
        if (Selection.objects.Length > 0)
        {
            UpdatePreviewObjects();
        }
    }

    private void UpdatePreviewObjects()
    {
        // Clear previous instances
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

        // Only proceed if there are selected objects
        if (Selection.objects.Length > 0 && previewScene.HasValue)
        {
            // Clone selected objects
            foreach (UnityEngine.Object selectedObj in Selection.objects)
            {
                if (selectedObj is GameObject gameObject)
                {
                    GameObject instance = PrefabUtility.InstantiatePrefab(gameObject, previewScene.Value) as GameObject;
                    if (instance != null)
                    {
                        previewInstances.Add(instance);

                        // Apply Standard shader to all renderers
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

        // Controls section
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

        // Render preview
        if (previewCamera != null && renderTexture != null)
        {
            HandleCameraControls(Event.current);

            previewCamera.targetTexture = renderTexture;
            previewCamera.Render();

            Graphics.CopyTexture(renderTexture, displayTexture);

            // Display the rendered texture
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

            // Rotate camera
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
            // Zoom camera
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
        // Remove the render texture from the camera
        if (previewCamera != null && renderTexture != null)
        {
            previewCamera.targetTexture = null;
        }
        
        // Release and destroy resources
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
        
        // Clean up preview objects
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
        
        // Close the preview scene
        if (previewScene.HasValue)
        {
            EditorSceneManager.ClosePreviewScene(previewScene.Value);
        }
        
        // Reset all references
        previewScene = null;
        previewCamera = null;
    }
}
