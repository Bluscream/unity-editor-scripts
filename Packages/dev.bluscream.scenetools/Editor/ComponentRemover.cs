using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Bluscream.Utils;

namespace Bluscream.ComponentRemover
{
    /// <summary>
    /// Unified component remover tool that combines all missing script removal functionality
    /// with multiple context menu entries for different operations.
    /// </summary>
    public static class ComponentRemover
    {
        #region Context Menu Entries - Remove Missing Scripts

        [MenuItem("Bluscream/Component Remover/Remove Missing Scripts from Selected (Recursive)", false, 1)]
        public static void RemoveMissingScriptsRecursively()
        {
            try
            {
                if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
                {
                    Debug.LogWarning("No GameObjects selected");
                    return;
                }

                // Create backup if BackupSystem is available
                string backupPath = null;
                if (Utils.IsBackupSystemAvailable())
                {
                    backupPath = BackupSystemHelper.CreateBackupForSelection("Remove Missing Scripts");
                }

                UnityEngine.Object[] deepSelection = null;
                
                // EditorUtility.CollectDeepHierarchy might be deprecated in newer versions
                #if UNITY_2020_1_OR_NEWER
                // Use alternative method for newer Unity versions
                var allObjects = new List<UnityEngine.Object>();
                foreach (var go in Selection.gameObjects)
                {
                    if (go != null)
                    {
                        allObjects.Add(go);
                        CollectChildren(go.transform, allObjects);
                    }
                }
                deepSelection = allObjects.ToArray();
                #else
                deepSelection = EditorUtility.CollectDeepHierarchy(Selection.gameObjects);
                #endif
                
                int compCount = 0;
                int goCount = 0;
                foreach (var o in deepSelection)
                {
                    if (o is GameObject go)
                    {
                        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                        if (count > 0)
                        {
                            Undo.RegisterCompleteObjectUndo(go, "Remove missing scripts");
                            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                            compCount += count;
                            goCount++;
                        }
                    }
                }
                Debug.Log($"Found and removed {compCount} missing scripts from {goCount} GameObjects" + 
                    (backupPath != null ? $". Backup created: {backupPath}" : ""));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error removing missing scripts: {e.Message}\n{e.StackTrace}");
            }
        }

        [MenuItem("Bluscream/Component Remover/Remove Missing Scripts from Selected (Visit Prefabs)", false, 2)]
        public static void RemoveMissingScriptsRecursivelyVisitPrefabs()
        {
            try
            {
                if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
                {
                    Debug.LogWarning("No GameObjects selected");
                    return;
                }

                // Create backup if BackupSystem is available
                string backupPath = null;
                if (Utils.IsBackupSystemAvailable())
                {
                    backupPath = BackupSystemHelper.CreateBackupForSelection("Remove Missing Scripts (Prefabs)");
                }

                var deeperSelection = Selection
                    .gameObjects.SelectMany(go => go != null ? go.GetComponentsInChildren<Transform>(true) : Enumerable.Empty<Transform>())
                    .Where(t => t != null)
                    .Select(t => t.gameObject);
                var prefabs = new HashSet<UnityEngine.Object>();
                int compCount = 0;
                int goCount = 0;
                foreach (var go in deeperSelection)
                {
                    if (go == null)
                        continue;

                    int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                    if (count > 0)
                    {
                        bool isPartOfPrefab = false;
                        #if UNITY_2018_3_OR_NEWER
                        // Unity 2018.3+ uses IsPartOfAnyPrefab
                        isPartOfPrefab = PrefabUtility.IsPartOfAnyPrefab(go);
                        #else
                        // Older versions use GetPrefabType
                        isPartOfPrefab = PrefabUtility.GetPrefabType(go) != PrefabType.None;
                        #endif

                        if (isPartOfPrefab)
                        {
                            RecursivePrefabSource(go, prefabs, ref compCount, ref goCount);
                            count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                            if (count == 0)
                                continue;
                        }

                        Undo.RegisterCompleteObjectUndo(go, "Remove missing scripts");
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                        compCount += count;
                        goCount++;
                    }
                }

                Debug.Log($"Found and removed {compCount} missing scripts from {goCount} GameObjects" + 
                    (backupPath != null ? $". Backup created: {backupPath}" : ""));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error removing missing scripts: {e.Message}\n{e.StackTrace}");
            }
        }

        [MenuItem("Bluscream/Component Remover/Remove Missing Scripts from Scene", false, 3)]
        public static void RemoveMissingScriptsFromScene()
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                GameObject[] rootObjects;
                #if UNITY_2019_3_OR_NEWER
                var rootList = new List<GameObject>();
                scene.GetRootGameObjects(rootList);
                rootObjects = rootList.ToArray();
                #else
                rootObjects = scene.GetRootGameObjects();
                #endif

                // Create backup if BackupSystem is available
                if (Utils.IsBackupSystemAvailable())
                {
                    BackupSystemHelper.CreateBackupForGameObjects(rootObjects, "Remove Missing Scripts from Scene");
                }

                RemoveMissingScripts(rootObjects);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error removing missing scripts from scene: {e.Message}\n{e.StackTrace}");
            }
        }

        #endregion

        #region Context Menu Entries - Log Missing Scripts

        [MenuItem("Bluscream/Component Remover/Log Missing Scripts in Scene", false, 11)]
        public static void LogMissingScriptsInScene()
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                GameObject[] rootObjects;
                #if UNITY_2019_3_OR_NEWER
                var rootList = new List<GameObject>();
                scene.GetRootGameObjects(rootList);
                rootObjects = rootList.ToArray();
                #else
                rootObjects = scene.GetRootGameObjects();
                #endif
                LogMissingScripts(rootObjects);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error logging missing scripts: {e.Message}\n{e.StackTrace}");
            }
        }

        [MenuItem("Bluscream/Component Remover/Log Missing Scripts in Selected", false, 12)]
        public static void LogMissingScriptsInSelected()
        {
            try
            {
                if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
                {
                    Debug.LogWarning("No GameObjects selected");
                    return;
                }
                LogMissingScripts(Selection.gameObjects);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error logging missing scripts from selection: {e.Message}\n{e.StackTrace}");
            }
        }

        #endregion

        #region Context Menu Entries - Select Missing Scripts

        [MenuItem("Bluscream/Component Remover/Select GameObjects with Missing Scripts", false, 21)]
        public static void SelectGameObjectsWithMissingScriptsMenu()
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                GameObject[] rootObjects;
                #if UNITY_2019_3_OR_NEWER
                var rootList = new List<GameObject>();
                scene.GetRootGameObjects(rootList);
                rootObjects = rootList.ToArray();
                #else
                rootObjects = scene.GetRootGameObjects();
                #endif
                SelectGameObjectsWithMissingScripts(rootObjects);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error selecting GameObjects with missing scripts: {e.Message}\n{e.StackTrace}");
            }
        }

        #endregion

        #region Context Menu Entries - Windows

        [MenuItem("Bluscream/Component Remover/Find Missing Scripts Window", false, 31)]
        public static void ShowFindMissingScriptsWindow()
        {
            EditorWindow.GetWindow<FindMissingScriptsWindow>("Missing Script Finder").Show();
        }

        [MenuItem("Bluscream/Component Remover/Missing Script Utility Window", false, 32)]
        public static void ShowMissingScriptUtilityWindow()
        {
            EditorWindow.GetWindow<MissingScriptUtility>("Missing Script Utility").Show();
        }

        #endregion

        #region Core Utility Methods

        private static void CollectChildren(Transform parent, List<UnityEngine.Object> collection)
        {
            if (parent == null)
                return;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != null)
                {
                    collection.Add(child.gameObject);
                    CollectChildren(child, collection);
                }
            }
        }

        private static void RecursivePrefabSource(
            GameObject instance,
            HashSet<UnityEngine.Object> prefabs,
            ref int compCount,
            ref int goCount
        )
        {
            try
            {
                if (instance == null)
                    return;

                var source = Utils.GetPrefabAsset(instance);
                if (source == null || !prefabs.Add(source))
                    return;
                RecursivePrefabSource(source, prefabs, ref compCount, ref goCount);

                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(source);
                if (count > 0)
                {
                    Undo.RegisterCompleteObjectUndo(source, "Remove missing scripts");
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(source);
                    compCount += count;
                    goCount++;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Error processing prefab source: {e.Message}");
            }
        }

        public static void LogMissingScripts(GameObject[] gameObjects)
        {
            try
            {
                if (gameObjects == null)
                {
                    Debug.LogWarning("GameObjects array is null");
                    return;
                }

                int gameObjectCount = 0;
                int missingScriptCount = 0;
                foreach (GameObject gameObject in gameObjects)
                {
                    if (gameObject != null)
                    {
                        missingScriptCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                            gameObject
                        );
                        ++gameObjectCount;
                    }
                }

                Debug.Log(
                    string.Format(
                        "Searched {0} GameObjects and found {1} missing scripts.",
                        gameObjectCount,
                        missingScriptCount
                    )
                );
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error logging missing scripts: {e.Message}\n{e.StackTrace}");
            }
        }

        public static void SelectGameObjectsWithMissingScripts(GameObject[] gameObjects)
        {
            try
            {
                if (gameObjects == null)
                {
                    Debug.LogWarning("GameObjects array is null");
                    return;
                }

                List<GameObject> selections = new List<GameObject>();

                foreach (GameObject gameObject in gameObjects)
                {
                    if (gameObject != null && GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject) > 0)
                        selections.Add(gameObject);
                }

                Selection.objects = selections.ToArray();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error selecting GameObjects with missing scripts: {e.Message}\n{e.StackTrace}");
            }
        }

        public static void RemoveMissingScripts(GameObject[] gameObjects)
        {
            try
            {
                if (gameObjects == null)
                {
                    Debug.LogWarning("GameObjects array is null");
                    return;
                }

                int missingScriptCount = 0;
                foreach (GameObject gameObject in gameObjects)
                {
                    if (gameObject != null)
                    {
                        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                        if (count > 0)
                        {
                            Undo.RegisterCompleteObjectUndo(gameObject, "Remove missing scripts");
                            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
                            missingScriptCount += count;
                        }
                    }
                }

                Debug.Log(
                    string.Format(
                        "Searched {0} GameObjects and removed {1} missing scripts.",
                        gameObjects.Length,
                        missingScriptCount
                    )
                );
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error removing missing scripts: {e.Message}\n{e.StackTrace}");
            }
        }

        #endregion
    }

    /// <summary>
    /// Window for finding missing scripts with search capabilities
    /// </summary>
    public class FindMissingScriptsWindow : EditorWindow
    {
        public List<GameObject> results = new List<GameObject>();

        private void OnGUI()
        {
            if (GUILayout.Button("Search Project"))
                SearchProject();
            if (GUILayout.Button("Search scene"))
                SearchScene();
            if (GUILayout.Button("Search Selected Objects"))
                SearchSelected();
            if (GUILayout.Button("Remove Selected Objects"))
                RemoveScripts();
            var so = new SerializedObject(this);
            var resultsProperty = so.FindProperty(nameof(results));
            EditorGUILayout.PropertyField(resultsProperty, true);
            so.ApplyModifiedProperties();
        }

        private void SearchProject()
        {
            try
            {
                results = AssetDatabase
                    .FindAssets("t:Prefab")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                    .Where(x => x != null && IsMissing(x, true))
                    .Distinct()
                    .ToList();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error searching project: {e.Message}\n{e.StackTrace}");
                results = new List<GameObject>();
            }
        }

        private void SearchScene()
        {
            try
            {
                // FindObjectsOfType is deprecated, use Object.FindObjectsOfType instead
                #if UNITY_2023_1_OR_NEWER
                results = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(x => x != null && IsMissing(x, false))
                    .Distinct()
                    .ToList();
                #else
                results = UnityEngine.Object.FindObjectsOfType<GameObject>(true)
                    .Where(x => x != null && IsMissing(x, false))
                    .Distinct()
                    .ToList();
                #endif
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error searching scene: {e.Message}\n{e.StackTrace}");
                results = new List<GameObject>();
            }
        }

        private void SearchSelected()
        {
            try
            {
                results = Selection.gameObjects
                    .Where(x => x != null && IsMissing(x, false))
                    .Distinct()
                    .ToList();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error searching selected objects: {e.Message}\n{e.StackTrace}");
                results = new List<GameObject>();
            }
        }

        private void RemoveScripts()
        {
            try
            {
                // Create backup if BackupSystem is available
                string backupPath = null;
                if (Utils.IsBackupSystemAvailable() && results.Count > 0)
                {
                    backupPath = BackupSystemHelper.CreateBackupForGameObjects(results.ToArray(), "Remove Missing Scripts from Window");
                }

                for (int i = 0; i < results.Count; i++)
                {
                    if (results[i] != null)
                    {
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(results[i]);
                    }
                }

                if (backupPath != null)
                {
                    Debug.Log($"Removed missing scripts. Backup created: {backupPath}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error removing scripts: {e.Message}\n{e.StackTrace}");
            }
        }

        private static bool IsMissing(GameObject go, bool includeChildren)
        {
            try
            {
                if (go == null)
                    return false;

                var components = includeChildren
                    ? go.GetComponentsInChildren<Component>()
                    : go.GetComponents<Component>();

                return components != null && components.Any(x => x == null);
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Window for missing script utility with advanced options
    /// </summary>
    public class MissingScriptUtility : EditorWindow
    {
        bool includeInactive = true;
        bool includePrefabs = true;

        public void OnGUI()
        {
            string includeInactiveTooltip = "Whether to include inactive GameObjects in the search.";
            includeInactive = EditorGUILayout.Toggle(
                new GUIContent("Include Inactive", includeInactiveTooltip),
                includeInactive
            );

            string includePrefabsTooltip = "Whether to include prefab GameObjects in the search.";
            includePrefabs = EditorGUILayout.Toggle(
                new GUIContent("Include Prefabs", includePrefabsTooltip),
                includePrefabs
            );

            if (GUILayout.Button("Log Missing Scripts"))
            {
                try
                {
                    var scene = SceneManager.GetActiveScene();
                    GameObject[] rootObjects;
                    #if UNITY_2019_3_OR_NEWER
                    var rootList = new List<GameObject>();
                    scene.GetRootGameObjects(rootList);
                    rootObjects = rootList.ToArray();
                    #else
                    rootObjects = scene.GetRootGameObjects();
                    #endif
                    ComponentRemover.LogMissingScripts(rootObjects);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error logging missing scripts: {e.Message}\n{e.StackTrace}");
                }
            }
            if (GUILayout.Button("Log Missing Scripts from Selected GameObjects"))
            {
                try
                {
                    ComponentRemover.LogMissingScripts(SelectedGameObjects(includeInactive, includePrefabs));
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error logging missing scripts from selection: {e.Message}\n{e.StackTrace}");
                }
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Select GameObjects with Missing Scripts"))
            {
                try
                {
                    var scene = SceneManager.GetActiveScene();
                    GameObject[] rootObjects;
                    #if UNITY_2019_3_OR_NEWER
                    var rootList = new List<GameObject>();
                    scene.GetRootGameObjects(rootList);
                    rootObjects = rootList.ToArray();
                    #else
                    rootObjects = scene.GetRootGameObjects();
                    #endif
                    ComponentRemover.SelectGameObjectsWithMissingScripts(rootObjects);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error selecting GameObjects with missing scripts: {e.Message}\n{e.StackTrace}");
                }
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Remove Missing Scripts"))
            {
                try
                {
                    var scene = SceneManager.GetActiveScene();
                    GameObject[] rootObjects;
                    #if UNITY_2019_3_OR_NEWER
                    var rootList = new List<GameObject>();
                    scene.GetRootGameObjects(rootList);
                    rootObjects = rootList.ToArray();
                    #else
                    rootObjects = scene.GetRootGameObjects();
                    #endif

                    // Create backup if BackupSystem is available
                    if (Utils.IsBackupSystemAvailable())
                    {
                        BackupSystemHelper.CreateBackupForGameObjects(rootObjects, "Remove Missing Scripts");
                    }

                    ComponentRemover.RemoveMissingScripts(rootObjects);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error removing missing scripts: {e.Message}\n{e.StackTrace}");
                }
            }
            if (GUILayout.Button("Remove Missing Scripts from Selected GameObjects"))
            {
                try
                {
                    GameObject[] selected = SelectedGameObjects(includeInactive, includePrefabs);
                    
                    // Create backup if BackupSystem is available
                    if (Utils.IsBackupSystemAvailable() && selected.Length > 0)
                    {
                        BackupSystemHelper.CreateBackupForGameObjects(selected, "Remove Missing Scripts from Selected");
                    }

                    ComponentRemover.RemoveMissingScripts(selected);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error removing missing scripts from selection: {e.Message}\n{e.StackTrace}");
                }
            }
        }

        public static GameObject[] SelectedGameObjects(
            bool includingInactive = true,
            bool includingPrefabs = true
        )
        {
            List<GameObject> selectedGameObjects = new List<GameObject>(Selection.gameObjects);
            foreach (GameObject selectedGameObject in Selection.gameObjects)
            {
                Transform[] childTransforms = selectedGameObject.GetComponentsInChildren<Transform>(
                    includingInactive
                );
                foreach (Transform childTransform in childTransforms)
                    selectedGameObjects.Add(childTransform.gameObject);
                if (includingPrefabs)
                {
                    HashSet<GameObject> prefabs = new HashSet<GameObject>();
                    PrefabInstances(selectedGameObject, prefabs);
                    selectedGameObjects.AddRange(prefabs);
                }
            }

            return selectedGameObjects.ToArray();
        }

        public static int RecursiveMissingScriptCount(GameObject gameObject)
        {
            int missingScriptCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                gameObject
            );

            Transform[] childTransforms = gameObject.GetComponentsInChildren<Transform>(true);
            foreach (Transform childTransform in childTransforms)
                missingScriptCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    childTransform.gameObject
                );

            return missingScriptCount;
        }

        private static void PrefabInstances(GameObject instance, HashSet<GameObject> prefabs)
        {
            GameObject source = Utils.GetPrefabAsset(instance);
            if (source == null || !prefabs.Add(source))
                return;

            PrefabInstances(source, prefabs);
        }

        /// <summary>
        /// Public API to remove all Quest-incompatible components from a GameObject hierarchy
        /// </summary>
        public static List<Component> RemoveQuestIncompatibleComponents(GameObject root, System.Action<string> progressCallback = null)
        {
            List<Component> removed = new List<Component>();
            if (root == null) return removed;

            Component[] components = root.GetComponentsInChildren<Component>(true);
            int total = components.Length;
            int removedCount = 0;

            for (int i = 0; i < total; i++)
            {
                Component comp = components[i];
                if (comp == null) continue;

                string typeName = comp.GetType().FullName;
                string lowerType = typeName.ToLowerInvariant();

                bool isIncompatible = lowerType.Contains("dynamicbone") ||
                                       comp is Cloth ||
                                       comp is Camera ||
                                       comp is Light ||
                                       comp is AudioSource ||
                                       comp is Rigidbody ||
                                       comp is Collider ||
                                       comp is Joint ||
                                       comp is ParticleSystem ||
                                       (lowerType.Contains("constraint") && !lowerType.Contains("vrc")) ||
                                       lowerType.Contains("finalik");

                if (isIncompatible)
                {
                    progressCallback?.Invoke($"Removing {comp.GetType().Name} from {comp.gameObject.name}...");
                    Undo.DestroyObjectImmediate(comp);
                    removed.Add(comp);
                    removedCount++;
                }
            }

            Debug.Log($"[ComponentRemover] Removed {removedCount} Quest-incompatible components from {root.name}.");
            return removed;
        }
    }

    /// <summary>
    /// Helper class to use BackupSystem for creating backups before removing scripts
    /// </summary>
    internal static class BackupSystemHelper
    {
        public static string CreateBackupForSelection(string backupName)
        {
            if (Selection.gameObjects == null || Selection.gameObjects.Length == 0) return null;
            return CreateBackupForGameObjects(Selection.gameObjects, backupName);
        }

        public static string CreateBackupForGameObjects(GameObject[] gameObjects, string backupName)
        {
            if (gameObjects == null || gameObjects.Length == 0) return null;

            try
            {
                Bluscream.BackupConfig config = new Bluscream.BackupConfig
                {
                    backupMaterials = true,
                    backupComponents = true,
                    backupTextures = false,
                    backupGameObjectHierarchy = false,
                    includeMaterialProperties = false,
                    includeComponentData = true,
                    backupLocation = "Assets/ComponentRemoverBackups",
                    backupName = backupName
                };

                string lastBackupPath = null;
                foreach (GameObject go in gameObjects)
                {
                    if (go != null)
                    {
                        lastBackupPath = Bluscream.BackupSystem.CreateBackup(config, Bluscream.BackupScope.GameObjectRecursive, go, null);
                    }
                }
                return lastBackupPath;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to create backup: {e.Message}");
                return null;
            }
        }
    }
}
