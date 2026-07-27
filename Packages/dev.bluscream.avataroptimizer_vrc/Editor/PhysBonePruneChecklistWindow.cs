using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static Bluscream.Utils;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Modal checklist shown by the InteractiveChecklist pruning strategy: lets the user pick
    /// which PhysBone components get removed to meet the profile limit. The deepest-first
    /// automatic suggestion comes pre-selected.
    /// </summary>
    internal class PhysBonePruneChecklistWindow : EditorWindow
    {
        private class Entry
        {
            public Component physBone;
            public string path;
            public int transforms;
            public bool remove;
        }

        private List<Entry> entries = new List<Entry>();
        private int requiredRemovals;
        private Vector2 scroll;
        private bool confirmed;

        /// <summary>
        /// Shows the checklist modally. <paramref name="orderedPhysBones"/> must be ordered by
        /// removal priority (deepest first) — the first <paramref name="requiredRemovals"/> come
        /// pre-selected. Returns the components to remove, or null if the user cancelled
        /// (callers should fall back to the automatic selection).
        /// </summary>
        public static List<Component> ShowChecklist(List<Component> orderedPhysBones, int requiredRemovals, Func<Component, int> transformCounter)
        {
            var window = CreateInstance<PhysBonePruneChecklistWindow>();
            window.titleContent = new GUIContent("PhysBone Pruning");
            window.requiredRemovals = requiredRemovals;
            window.entries = orderedPhysBones
                .Where(pb => pb != null)
                .Select((pb, i) => new Entry
                {
                    physBone = pb,
                    path = GetGameObjectPath(pb.gameObject),
                    transforms = transformCounter != null ? transformCounter(pb) : 0,
                    remove = i < requiredRemovals
                })
                .ToList();
            window.minSize = new Vector2(520, 380);
            window.ShowModalUtility();

            if (!window.confirmed) return null;
            return window.entries.Where(e => e.remove && e.physBone != null).Select(e => e.physBone).ToList();
        }

        private void OnGUI()
        {
            int selected = entries.Count(e => e.remove);

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                $"The avatar has {entries.Count} PhysBone components but the target rank allows {entries.Count - requiredRemovals}. " +
                $"Select at least {requiredRemovals} to remove. The deepest bones (usually accessory/detail bones) are pre-selected.",
                MessageType.Info);
            EditorGUILayout.Space(4);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Entry e in entries)
            {
                if (e.physBone == null) continue;
                EditorGUILayout.BeginHorizontal();
                e.remove = EditorGUILayout.ToggleLeft($"{e.path}  ({e.transforms} transforms)", e.remove);
                if (GUILayout.Button("Ping", GUILayout.Width(44)))
                {
                    EditorGUIUtility.PingObject(e.physBone.gameObject);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            if (selected < requiredRemovals)
            {
                EditorGUILayout.HelpBox($"Selected {selected} / {requiredRemovals} required.", MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto-Select Deepest"))
            {
                for (int i = 0; i < entries.Count; i++)
                    entries[i].remove = i < requiredRemovals;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel (use automatic)", GUILayout.Width(160)))
            {
                confirmed = false;
                Close();
            }
            EditorGUI.BeginDisabledGroup(selected < requiredRemovals);
            if (GUILayout.Button($"Remove Selected ({selected})", GUILayout.Width(160)))
            {
                confirmed = true;
                Close();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
        }
    }
}
