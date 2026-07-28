using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Bluscream.VRC
{
    /// <summary>
    /// Utility methods for querying the VRChat SDK at runtime via Reflection without hard compile dependencies.
    /// Shared across Bluscream VRChat editor packages.
    /// </summary>
    public static class VRCSDKReflectionHelper
    {
        private static long? _sdkMobileBundleLimit;
        private static long? _sdkPcBundleLimit;

        /// <summary>
        /// Attempts to read the compressed avatar bundle size limit from the VRChat SDK (VRC.ValidationHelpers.GetAssetBundleSizeLimit).
        /// Returns true if successfully extracted from the SDK.
        /// </summary>
        public static bool TryGetAssetBundleSizeLimit(bool isMobilePlatform, out long limitBytes)
        {
            limitBytes = 0;
            long? cached = isMobilePlatform ? _sdkMobileBundleLimit : _sdkPcBundleLimit;
            if (cached.HasValue)
            {
                limitBytes = cached.Value;
                return true;
            }

            try
            {
                Type helpers = Type.GetType("VRC.ValidationHelpers, VRCSDKBase");
                Type contentType = Type.GetType("VRC.ContentType, VRCSDKBase");
                if (helpers != null && contentType != null)
                {
                    var method = helpers.GetMethod("GetAssetBundleSizeLimit");
                    if (method != null)
                    {
                        object avatar = Enum.Parse(contentType, "Avatar");
                        var args = method.GetParameters().Length == 3
                            ? new object[] { avatar, isMobilePlatform, true }
                            : new object[] { avatar, isMobilePlatform };
                        limitBytes = Convert.ToInt64(method.Invoke(null, args));
                        if (isMobilePlatform) _sdkMobileBundleLimit = limitBytes;
                        else _sdkPcBundleLimit = limitBytes;
                        return true;
                    }
                }
            }
            catch { /* SDK unavailable or API changed */ }

            return false;
        }

        /// <summary>
        /// Attempts to extract performance rank stats object for a target platform and rank name from the VRChat SDK.
        /// </summary>
        public static bool TryGetPerformanceRatingStats(string platformName, string rankName, out object ratingStatsObj)
        {
            ratingStatsObj = null;
            try
            {
                Type statsType = null;
                Type platformEnumType = null;
                Type ratingEnumType = null;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (statsType == null)
                    {
                        if (!ReflectionHelper.TryFindType(asm, "VRC.SDK3.Validation.Performance.AvatarPerformanceStats", out statsType))
                            ReflectionHelper.TryFindType(asm, "VRC.SDKBase.Validation.Performance.AvatarPerformanceStats", out statsType);
                    }

                    if (platformEnumType == null)
                    {
                        if (!ReflectionHelper.TryFindType(asm, "VRC.SDK3.Validation.Performance.PerformanceSDKPlatform", out platformEnumType))
                            ReflectionHelper.TryFindType(asm, "VRC.SDKBase.Validation.Performance.PerformanceSDKPlatform", out platformEnumType);
                    }

                    if (ratingEnumType == null)
                    {
                        if (!ReflectionHelper.TryFindType(asm, "VRC.SDK3.Validation.Performance.PerformanceRating", out ratingEnumType))
                            ReflectionHelper.TryFindType(asm, "VRC.SDKBase.Validation.Performance.PerformanceRating", out ratingEnumType);
                    }
                }

                if (statsType == null || ratingEnumType == null) return false;

                object sdkPlatformVal = platformEnumType != null && Enum.IsDefined(platformEnumType, platformName)
                    ? Enum.Parse(platformEnumType, platformName)
                    : null;

                if (!Enum.IsDefined(ratingEnumType, rankName)) return false;
                object sdkRankVal = Enum.Parse(ratingEnumType, rankName);

                var method = statsType.GetMethod("GetPerformanceRatingStats", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (method != null)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 2 && sdkPlatformVal != null)
                        ratingStatsObj = method.Invoke(null, new object[] { sdkPlatformVal, sdkRankVal });
                    else if (parameters.Length == 1)
                        ratingStatsObj = method.Invoke(null, new object[] { sdkRankVal });
                }

                return ratingStatsObj != null;
            }
            catch
            {
                ratingStatsObj = null;
                return false;
            }
        }

        /// <summary>
        /// Attempts to extract forbidden component names for mobile platforms from VRC.SDKBase.Validation.AvatarValidation.ForbiddenComponents.
        /// </summary>
        public static bool TryGetForbiddenComponents(out List<string> forbiddenComponents)
        {
            forbiddenComponents = null;
            try
            {
                Type validationType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    validationType = asm.GetType("VRC.SDKBase.Validation.AvatarValidation")
                                  ?? asm.GetType("VRC.SDK3.Validation.AvatarValidation");
                    if (validationType != null) break;
                }

                if (validationType != null)
                {
                    var member = validationType.GetField("ForbiddenComponents", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                              ?? (MemberInfo)validationType.GetProperty("ForbiddenComponents", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    IEnumerable forbiddenList = null;
                    if (member is FieldInfo fi) forbiddenList = fi.GetValue(null) as IEnumerable;
                    else if (member is PropertyInfo pi) forbiddenList = pi.GetValue(null) as IEnumerable;

                    if (forbiddenList != null)
                    {
                        var blackList = new List<string>();
                        foreach (var item in forbiddenList)
                        {
                            if (item == null) continue;
                            if (item is Type t) blackList.Add(t.Name);
                            else if (item is string s) blackList.Add(s);
                        }
                        if (blackList.Count > 0)
                        {
                            forbiddenComponents = blackList;
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool TryGetIntStat(object targetObj, string name, out int value)
        {
            value = 0;
            if (targetObj == null) return false;
            Type t = targetObj.GetType();
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

            foreach (var f in fields) if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) { value = Convert.ToInt32(f.GetValue(targetObj)); return true; }
            foreach (var p in props) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.CanRead) { value = Convert.ToInt32(p.GetValue(targetObj)); return true; }
            return false;
        }

        public static bool TryGetLongStat(object targetObj, string name, out long value)
        {
            value = 0L;
            if (targetObj == null) return false;
            Type t = targetObj.GetType();
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

            foreach (var f in fields) if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) { value = Convert.ToInt64(f.GetValue(targetObj)); return true; }
            foreach (var p in props) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.CanRead) { value = Convert.ToInt64(p.GetValue(targetObj)); return true; }
            return false;
        }

        public static bool TryGetBoolStat(object targetObj, string name, out bool value)
        {
            value = false;
            if (targetObj == null) return false;
            Type t = targetObj.GetType();
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

            foreach (var f in fields) if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) { value = Convert.ToBoolean(f.GetValue(targetObj)); return true; }
            foreach (var p in props) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.CanRead) { value = Convert.ToBoolean(p.GetValue(targetObj)); return true; }
            return false;
        }
    }
}
