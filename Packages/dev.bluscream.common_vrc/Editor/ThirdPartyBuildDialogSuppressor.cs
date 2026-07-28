using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRC
{
    /// <summary>
    /// Temporarily disables third-party build-time features that pop up modal dialogs, so automated
    /// dry-run builds (size probes) don't block on prompts meant for real uploads.
    ///
    /// Only affects the scope of a using-block: settings are restored in Dispose, including on
    /// exception, so builds started by any other means (SDK panel, manual upload) behave normally.
    /// </summary>
    public sealed class ThirdPartyBuildDialogSuppressor : IDisposable
    {
        private readonly List<(string key, bool previous)> _restoreBools = new List<(string, bool)>();
        private readonly bool _verbose;

        /// <summary>
        /// VRCFury's mobile parameter alignment. During a mobile build it compares the avatar against
        /// the desktop upload's saved parameter data and shows blocking "VRCFury Mobile Sync" dialogs
        /// when the desktop version is missing, built with another VRCFury version, or has different
        /// parameters. All three are gated behind this single EditorPref
        /// (AlignMobileParamsMenuItem.Get), and parameter alignment has no meaningful effect on
        /// bundle size, so it is safe to skip while measuring.
        /// </summary>
        private const string VRCFuryAlignMobileParams = "com.vrcfury.alignMobile";

        public ThirdPartyBuildDialogSuppressor(bool verbose = true)
        {
            _verbose = verbose;
            SuppressBool(VRCFuryAlignMobileParams, "VRCFury mobile parameter alignment");
        }

        private void SuppressBool(string key, string description)
        {
            try
            {
                // Only touch the pref if the feature exists / is currently on
                bool current = EditorPrefs.GetBool(key, true);
                _restoreBools.Add((key, current));
                if (current)
                {
                    EditorPrefs.SetBool(key, false);
                    if (_verbose) Debug.Log($"[BuildDialogSuppressor] Temporarily disabled {description} for the dry-run build (restored afterwards).");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BuildDialogSuppressor] Could not suppress {description}: {e.Message}");
            }
        }

        public void Dispose()
        {
            foreach (var (key, previous) in _restoreBools)
            {
                try { EditorPrefs.SetBool(key, previous); }
                catch (Exception e) { Debug.LogWarning($"[BuildDialogSuppressor] Failed to restore '{key}' to {previous}: {e.Message}"); }
            }
            _restoreBools.Clear();
        }
    }
}
