using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Bluscream
{
    /// <summary>
    /// Comprehensive generic reflection helper utilities shared across all Bluscream Unity packages.
    /// Provides safe, exception-free Try... methods for assembly loading, type discovery,
    /// field/property getting & setting, and method invocation.
    /// </summary>
    public static class ReflectionHelper
    {
        private const BindingFlags AnyMemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        #region Type Discovery

        /// <summary>
        /// Attempts to find a Type by name within a specific Assembly.
        /// </summary>
        public static bool TryFindType(Assembly assembly, string typeName, out Type type)
        {
            type = null;
            if (assembly == null || string.IsNullOrEmpty(typeName)) return false;
            try
            {
                type = assembly.GetType(typeName) ?? assembly.GetTypes().FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal) || string.Equals(t.Name, typeName, StringComparison.Ordinal));
            }
            catch { }
            return type != null;
        }

        /// <summary>
        /// Attempts to find a Type by name across all currently loaded assemblies in the AppDomain.
        /// </summary>
        public static bool TryFindType(string typeName, out Type type)
        {
            type = null;
            if (string.IsNullOrEmpty(typeName)) return false;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (TryFindType(asm, typeName, out type))
                        return true;
                }
            }
            catch { }
            return false;
        }

        #endregion

        #region Field Operations

        /// <summary>
        /// Attempts to retrieve an instance or static field value from a target object or type.
        /// </summary>
        public static bool TryGetFieldValue<T>(object target, string fieldName, out T value)
        {
            value = default;
            if (target == null || string.IsNullOrEmpty(fieldName)) return false;

            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;

            try
            {
                FieldInfo field = type.GetField(fieldName, AnyMemberFlags);
                if (field != null)
                {
                    object rawVal = field.GetValue(instance);
                    if (rawVal is T typedVal)
                    {
                        value = typedVal;
                        return true;
                    }
                    if (rawVal != null)
                    {
                        value = (T)Convert.ChangeType(rawVal, typeof(T));
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Attempts to set an instance or static field value on a target object or type.
        /// </summary>
        public static bool TrySetFieldValue(object target, string fieldName, object value)
        {
            if (target == null || string.IsNullOrEmpty(fieldName)) return false;

            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;

            try
            {
                FieldInfo field = type.GetField(fieldName, AnyMemberFlags);
                if (field != null)
                {
                    field.SetValue(instance, value);
                    return true;
                }
            }
            catch { }
            return false;
        }

        #endregion

        #region Property Operations

        /// <summary>
        /// Attempts to retrieve an instance or static property value from a target object or type.
        /// </summary>
        public static bool TryGetPropertyValue<T>(object target, string propName, out T value)
        {
            value = default;
            if (target == null || string.IsNullOrEmpty(propName)) return false;

            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;

            try
            {
                PropertyInfo prop = type.GetProperty(propName, AnyMemberFlags);
                if (prop != null && prop.CanRead)
                {
                    object rawVal = prop.GetValue(instance);
                    if (rawVal is T typedVal)
                    {
                        value = typedVal;
                        return true;
                    }
                    if (rawVal != null)
                    {
                        value = (T)Convert.ChangeType(rawVal, typeof(T));
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Attempts to set an instance or static property value on a target object or type.
        /// </summary>
        public static bool TrySetPropertyValue(object target, string propName, object value)
        {
            if (target == null || string.IsNullOrEmpty(propName)) return false;

            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;

            try
            {
                PropertyInfo prop = type.GetProperty(propName, AnyMemberFlags);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(instance, value);
                    return true;
                }
            }
            catch { }
            return false;
        }

        #endregion

        #region Method Invocation

        /// <summary>
        /// Safely invokes a MethodInfo instance without throwing exceptions.
        /// </summary>
        public static bool TryInvoke<T>(this MethodInfo method, object instance, out T result, params object[] parameters)
        {
            result = default;
            if (method == null) return false;
            try
            {
                object rawRes = method.Invoke(instance, parameters);
                if (rawRes is T typedRes)
                {
                    result = typedRes;
                    return true;
                }
                if (rawRes != null)
                {
                    result = (T)Convert.ChangeType(rawRes, typeof(T));
                    return true;
                }
                return typeof(T) == typeof(object);
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Safely invokes a MethodInfo instance without expecting a typed return value.
        /// </summary>
        public static bool TryInvoke(this MethodInfo method, object instance, params object[] parameters)
        {
            if (method == null) return false;
            try
            {
                method.Invoke(instance, parameters);
                return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Attempts to invoke an instance or static method by name on a target object or type and return its typed result.
        /// </summary>
        public static bool TryInvokeMethod<T>(object target, string methodName, out T result, params object[] parameters)
        {
            result = default;
            if (target == null || string.IsNullOrEmpty(methodName)) return false;

            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;

            try
            {
                MethodInfo method = type.GetMethod(methodName, AnyMemberFlags);
                if (method != null)
                {
                    return method.TryInvoke(instance, out result, parameters);
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Attempts to invoke a void or ignored-return method by name on a target object or type.
        /// </summary>
        public static bool TryInvokeMethod(object target, string methodName, params object[] parameters)
        {
            if (target == null || string.IsNullOrEmpty(methodName)) return false;

            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;

            try
            {
                MethodInfo method = type.GetMethod(methodName, AnyMemberFlags);
                if (method != null)
                {
                    return method.TryInvoke(instance, parameters);
                }
            }
            catch { }
            return false;
        }

        #endregion
    }
}
