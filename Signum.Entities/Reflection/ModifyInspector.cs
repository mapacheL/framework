using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;
using Signum.Utilities;
using System.Linq;
using Signum.Utilities.Reflection;

namespace Signum.Entities.Reflection
{
    public class ModifyInspector
    {
        static readonly Dictionary<Type, Func<object, object?>[]> getterCache = new Dictionary<Type, Func<object, object?>[]>();
        static Func<object, object?>[] ModifiableFieldGetters(Type type)
        {
            lock (getterCache)
                return getterCache.GetOrCreate(type, () =>
                {
                    FieldInfo[] aux = Reflector.InstanceFieldsInOrder(type);
                    return aux.Where(fi => Reflector.IsModifiableIdentifiableOrLite(fi.FieldType) && !IsIgnored(fi))
                        .Select(fi => ReflectionTools.CreateGetterUntyped(type, fi)!).ToArray();
                });
        }

        static readonly Dictionary<Type, Func<object, object?>[]> getterVirtualCache = new Dictionary<Type, Func<object, object?>[]>();
        static Func<object, object?>[] ModifiableFieldGettersVirtual(Type type)
        {
            lock (getterVirtualCache)
                return getterVirtualCache.GetOrCreate(type, () =>
                {
                    FieldInfo[] aux = Reflector.InstanceFieldsInOrder(type);
                    return aux.Where(fi => Reflector.IsModifiableIdentifiableOrLite(fi.FieldType) && (!IsIgnored(fi) || IsQueryableProperty(fi)))
                        .Select(fi => ReflectionTools.CreateGetterUntyped(type, fi)!).ToArray();
                });
        }

        private static bool IsIgnored(FieldInfo fi)
        {
            return fi.HasAttribute<IgnoreAttribute>() ||
                (Reflector.FindPropertyInfo(fi).HasAttribute<IgnoreAttribute>());
        }

        private static bool IsQueryableProperty(FieldInfo fi)
        {
            return (Reflector.TryFindPropertyInfo(fi)?.HasAttribute<QueryablePropertyAttribute>() ?? false);
        }


        public static IEnumerable<Modifiable> FullExplore(Modifiable obj)
        {
            if (obj == null)
                yield break;

            if (Reflector.IsMList(obj.GetType()))
            {
                Type t = obj.GetType().ElementType()!;
                if (Reflector.IsModifiableIdentifiableOrLite(t))
                {
                    foreach (var item in (IEnumerable)obj)
                        if (item != null)
                            yield return (Modifiable)item;
                }
            }
            else
            {
                foreach (Func<object, object?> getter in ModifiableFieldGetters(obj.GetType()))
                {
                    object? field = getter(obj);

                    if (field == null)
                        continue;

                    yield return (Modifiable)field;
                }

                if (obj is ModifiableEntity mod)
                {
                    foreach (var mixin in mod.Mixins)
                    {
                        yield return mixin;
                    }
                }
            }
        }

        public static IEnumerable<Modifiable> FullExploreVirtual(Modifiable obj)
        {
            if (obj == null)
                yield break;

            if (Reflector.IsMList(obj.GetType()))
            {
                Type t = obj.GetType().ElementType()!;
                if (Reflector.IsModifiableIdentifiableOrLite(t))
                {
                    foreach (var item in (IEnumerable)obj!)
                        if (item != null)
                            yield return (Modifiable)item;
                }
            }
            else
            {
                foreach (Func<object, object?> getter in ModifiableFieldGettersVirtual(obj.GetType()))
                {
                    object? field = getter(obj);

                    if (field == null)
                        continue;

                    yield return (Modifiable)field;
                }

                if (obj is ModifiableEntity mod)
                {
                    foreach (var mixin in mod.Mixins)
                    {
                        yield return mixin;
                    }
                }
            }
        }

        public static IEnumerable<Modifiable> EntityExplore(Modifiable obj)
        {
            if (obj == null)//|| obj is Lite)
                yield break;

            if (Reflector.IsMList(obj.GetType()))
            {
                Type t = obj.GetType().ElementType()!;

                if (t.IsModifiable() && !t.IsEntity())
                {
                    foreach (var item in (IEnumerable)obj)
                        if (item != null)
                            yield return (Modifiable)item;
                }
            }
            else
            {
                foreach (Func<object, object?> getter in ModifiableFieldGetters(obj.GetType()))
                {
                    object? field = getter(obj);

                    if (field == null || field is Entity)
                        continue;

                    yield return (Modifiable)field;
                }

                if (obj is ModifiableEntity mod)
                {
                    foreach (var mixin in mod.Mixins)
                    {
                        yield return mixin;
                    }
                }
            }
        }
    }
}
