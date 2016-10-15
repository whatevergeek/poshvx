﻿//
//    Copyright (C) Microsoft.  All rights reserved.
//

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Management.Automation
{
    internal static class ExtensionMethods
    {
        public static void SafeInvoke(this EventHandler eventHandler, object sender, EventArgs eventArgs)
        {
            if (eventHandler != null)
            {
                eventHandler(sender, eventArgs);
            }
        }

        public static void SafeInvoke<T>(this EventHandler<T> eventHandler, object sender, T eventArgs) where T : EventArgs
        {
            if (eventHandler != null)
            {
                eventHandler(sender, eventArgs);
            }
        }
    }

    internal static class EnumerableExtensions
    {
        // CORECLR has an implementation of Append built-in.
#if !CORECLR
        internal static IEnumerable<T> Append<T>(this IEnumerable<T> collection, T element)
        {
            foreach (T t in collection)
                yield return t;
            yield return element;
        }
#endif
        internal static IEnumerable<T> Prepend<T>(this IEnumerable<T> collection, T element)
        {
            yield return element;
            foreach (T t in collection)
                yield return t;
        }

        internal static int SequenceGetHashCode<T>(this IEnumerable<T> xs) where T : class
        {
            // algorithm based on http://stackoverflow.com/questions/263400/what-is-the-best-algorithm-for-an-overridden-system-object-gethashcode
            if (xs == null)
            {
                return 82460653; // random number
            }
            unchecked
            {
                int hash = 41; // 41 is a random prime number
                foreach (T x in xs)
                {
                    hash = hash * 59; // 59 is a random prime number
                    if (x != null)
                    {
                        hash = hash + x.GetHashCode();
                    }
                }
                return hash;
            }
        }
    }

    /// <summary>
    /// The type extension methods within this partial class are used/shared by both FullCLR and CoreCLR powershell.
    /// 
    /// * If you want to add an extension method that will be used by both FullCLR and CoreCLR powershell, please add it here.
    /// * If you want to add an extension method that will be used only by CoreCLR powershell, please add it to the partial
    ///   'PSTypeExtensions' class in 'CorePsExtensions.cs'.
    /// </summary>
    internal static partial class PSTypeExtensions
    {
        /// <summary>
        /// Type.EmptyTypes is not in CoreCLR. Use this one to replace it.
        /// </summary>
        internal static Type[] EmptyTypes = new Type[0];
        private static readonly Type s_comObjectType = Type.GetType("System.__ComObject");

        /// <summary>
        /// Check does the type have an instance default constructor with visibility that allows calling it from subclass.
        /// </summary>
        /// <param name="type">type</param>
        /// <returns>true when type has a default ctor.</returns>
        internal static bool HasDefaultCtor(this Type type)
        {
            var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (ctor != null)
            {
                if (ctor.IsPublic || ctor.IsFamily || ctor.IsFamilyOrAssembly)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsNumeric(this Type type)
        {
            return LanguagePrimitives.IsNumeric(LanguagePrimitives.GetTypeCode(type));
        }

        internal static bool IsNumericOrPrimitive(this Type type)
        {
            return type.GetTypeInfo().IsPrimitive || LanguagePrimitives.IsNumeric(LanguagePrimitives.GetTypeCode(type));
        }

        internal static bool IsSafePrimitive(this Type type)
        {
            return type.GetTypeInfo().IsPrimitive && (type != typeof(IntPtr)) && (type != typeof(UIntPtr));
        }

        internal static bool IsFloating(this Type type)
        {
            return LanguagePrimitives.IsFloating(LanguagePrimitives.GetTypeCode(type));
        }

        internal static bool IsInteger(this Type type)
        {
            return LanguagePrimitives.IsInteger(LanguagePrimitives.GetTypeCode(type));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsComObject(this Type type)
        {
#if UNIX
            return false;
#elif CORECLR // Type.IsComObject(Type) is not in CoreCLR
            return s_comObjectType.IsAssignableFrom(type);
#else
            return type.IsCOMObject;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TypeCode GetTypeCode(this Type type)
        {
#if CORECLR // Type.GetTypeCode(Type) is not in CoreCLR
            return GetTypeCodeInCoreClr(type);
#else
            return Type.GetTypeCode(type);
#endif
        }

        internal static IEnumerable<T> GetCustomAttributes<T>(this Type type, bool inherit)
            where T : Attribute
        {
            return from attr in type.GetTypeInfo().GetCustomAttributes(typeof(T), inherit)
                   where attr is T
                   select (T)attr;
        }
    }

    internal static class WeakReferenceExtensions
    {
        internal static bool TryGetTarget<T>(this WeakReference weakReference, out T target) where T : class
        {
            var t = weakReference.Target;
            target = t as T;
            return (target != null);
        }
    }
}
