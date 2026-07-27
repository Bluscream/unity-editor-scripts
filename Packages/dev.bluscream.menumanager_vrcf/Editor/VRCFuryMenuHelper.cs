using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using Bluscream.VRCFury;

namespace Bluscream.MenuManager
{
    /// <summary>
    /// Wrapper for VRCFury menu operations, delegating to dev.bluscream.common_vrcf core helpers.
    /// </summary>
    public static class VRCFuryMenuHelper
    {
        public static bool Initialize()
        {
            return VRCFuryHelper.Initialize();
        }

        public static VRCExpressionsMenu GetMergedMenu(GameObject avatarObj)
        {
            return VRCFuryMenuMapper.GetMergedMenu(avatarObj);
        }

        public static void ApplyMovesToAvatar(GameObject avatarObject, List<MenuMoveOperation> moves)
        {
            var commonMoves = moves.Select(m => new Bluscream.VRCFury.MenuMoveOperation(m.fromPath, m.toPath)).ToList();
            VRCFuryFeatureHelper.ApplyMenuMoves(avatarObject, commonMoves);
        }
    }
}
