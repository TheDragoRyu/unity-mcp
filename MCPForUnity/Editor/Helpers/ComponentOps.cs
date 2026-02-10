using System;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Runtime.Serialization;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Low-level component operations extracted from ManageGameObject and ManageComponents.
    /// Provides pure C# operations without JSON parsing or response formatting.
    /// </summary>
    public static class ComponentOps
    {
        /// <summary>
        /// Adds a component to a GameObject with Undo support.
        /// </summary>
        /// <param name="target">The target GameObject</param>
        /// <param name="componentType">The type of component to add</param>
        /// <param name="error">Error message if operation fails</param>
        /// <returns>The added component, or null if failed</returns>
        public static Component AddComponent(GameObject target, Type componentType, out string error)
        {
            error = null;

            if (target == null)
            {
                error = "Target GameObject is null.";
                return null;
            }

            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                error = $"Type '{componentType?.Name ?? "null"}' is not a valid Component type.";
                return null;
            }

            // Prevent adding duplicate Transform
            if (componentType == typeof(Transform))
            {
                error = "Cannot add another Transform component.";
                return null;
            }

            // Check for 2D/3D physics conflicts
            string conflictError = CheckPhysicsConflict(target, componentType);
            if (conflictError != null)
            {
                error = conflictError;
                return null;
            }

            // Produce a clearer error when this component already exists and cannot be duplicated.
            Component existingComponent = target.GetComponent(componentType);
            if (existingComponent != null && !AllowsMultiple(target, componentType))
            {
                error = $"Component '{componentType.Name}' already exists on '{target.name}' and this type does not allow multiple instances.";
                return null;
            }

            try
            {
                Component newComponent = Undo.AddComponent(target, componentType);
                if (newComponent == null)
                {
                    if (target.GetComponent(componentType) != null && !AllowsMultiple(target, componentType))
                    {
                        error = $"Component '{componentType.Name}' already exists on '{target.name}' and this type does not allow multiple instances.";
                    }
                    else
                    {
                        error = $"Failed to add component '{componentType.Name}' to '{target.name}'. Unity may restrict this component on the current target.";
                    }
                    return null;
                }

                // Apply default values for specific component types
                ApplyDefaultValues(newComponent);

                return newComponent;
            }
            catch (Exception ex)
            {
                error = $"Error adding component '{componentType.Name}': {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// Removes a component from a GameObject with Undo support.
        /// </summary>
        /// <param name="target">The target GameObject</param>
        /// <param name="componentType">The type of component to remove</param>
        /// <param name="error">Error message if operation fails</param>
        /// <returns>True if component was removed successfully</returns>
        public static bool RemoveComponent(GameObject target, Type componentType, out string error)
        {
            error = null;

            if (target == null)
            {
                error = "Target GameObject is null.";
                return false;
            }

            if (componentType == null)
            {
                error = "Component type is null.";
                return false;
            }

            // Prevent removing Transform
            if (componentType == typeof(Transform))
            {
                error = "Cannot remove Transform component.";
                return false;
            }

            Component component = target.GetComponent(componentType);
            if (component == null)
            {
                error = $"Component '{componentType.Name}' not found on '{target.name}'.";
                return false;
            }

            try
            {
                Undo.DestroyObjectImmediate(component);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Error removing component '{componentType.Name}': {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Sets a property value on a component using reflection.
        /// </summary>
        /// <param name="component">The target component</param>
        /// <param name="propertyName">The property or field name</param>
        /// <param name="value">The value to set (JToken)</param>
        /// <param name="error">Error message if operation fails</param>
        /// <returns>True if property was set successfully</returns>
        public static bool SetProperty(Component component, string propertyName, JToken value, out string error)
        {
            error = null;

            if (component == null)
            {
                error = "Component is null.";
                return false;
            }

            if (string.IsNullOrEmpty(propertyName))
            {
                error = "Property name is null or empty.";
                return false;
            }

            var segments = ParseMemberPath(propertyName, out error);
            if (segments == null)
            {
                return false;
            }

            try
            {
                return SetNestedValue(component, component.GetType(), segments, 0, value, propertyName, out _, out error);
            }
            catch (Exception ex)
            {
                error = $"Failed to set '{propertyName}': {ex.Message}";
                return false;
            }
        }

        private static bool TryConvertForMember(JToken value, Type targetType, string memberLabel, out object convertedValue, out string error)
        {
            convertedValue = null;
            error = null;

            try
            {
                convertedValue = PropertyConversion.ConvertToType(value, targetType);
            }
            catch (Exception ex)
            {
                error = $"Failed to convert value for {memberLabel} to type '{targetType.Name}': {ex.Message}";
                return false;
            }

            bool isNullInput = value == null || value.Type == JTokenType.Null;
            if (convertedValue == null && !isNullInput)
            {
                if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
                {
                    if (UnityAssetReferenceResolver.TryResolveAssetReference(value, typeof(UnityEngine.Object), out UnityEngine.Object resolvedAsset, out string resolveError) && resolvedAsset != null)
                    {
                        error = $"Resolved asset '{resolvedAsset.name}' ({resolvedAsset.GetType().Name}) but it cannot be assigned to {memberLabel} of type '{targetType.Name}'.";
                        return false;
                    }

                    error = $"Failed to resolve asset for {memberLabel} as type '{targetType.Name}'. {resolveError}";
                    return false;
                }

                error = $"Failed to convert value for {memberLabel} to type '{targetType.Name}'.";
                return false;
            }

            if (convertedValue != null && !targetType.IsAssignableFrom(convertedValue.GetType()))
            {
                error = $"Converted value for {memberLabel} has type '{convertedValue.GetType().Name}', which is not assignable to '{targetType.Name}'.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets all public properties and fields from a component type.
        /// </summary>
        public static List<string> GetAccessibleMembers(Type componentType)
        {
            var members = new List<string>();
            if (componentType == null) return members;

            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            foreach (var prop in componentType.GetProperties(flags))
            {
                if (prop.CanWrite && prop.GetSetMethod() != null)
                {
                    members.Add(prop.Name);
                }
            }

            foreach (var field in componentType.GetFields(flags))
            {
                if (!field.IsInitOnly)
                {
                    members.Add(field.Name);
                }
            }

            // Include private [SerializeField] fields - traverse inheritance hierarchy
            // Type.GetFields with NonPublic only returns fields declared directly on that type,
            // so we need to walk up the chain to find inherited private serialized fields
            var seenFieldNames = new HashSet<string>(members); // Avoid duplicates with public fields
            Type currentType = componentType;
            while (currentType != null && currentType != typeof(object))
            {
                foreach (var field in currentType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (field.GetCustomAttribute<SerializeField>() != null && !seenFieldNames.Contains(field.Name))
                    {
                        members.Add(field.Name);
                        seenFieldNames.Add(field.Name);
                    }
                }
                currentType = currentType.BaseType;
            }

            members.Sort();
            return members;
        }

        // --- Private Helpers ---

        private sealed class MemberAccessor
        {
            private readonly PropertyInfo _property;
            private readonly FieldInfo _field;

            public MemberAccessor(PropertyInfo property) => _property = property;
            public MemberAccessor(FieldInfo field) => _field = field;

            public string Name => _property?.Name ?? _field?.Name;
            public Type MemberType => _property?.PropertyType ?? _field?.FieldType;
            public bool CanWrite => _property?.CanWrite == true || (_field != null && !_field.IsInitOnly);
            public bool IsProperty => _property != null;

            public object GetValue(object target) => _property != null ? _property.GetValue(target) : _field.GetValue(target);
            public void SetValue(object target, object value)
            {
                if (_property != null)
                {
                    _property.SetValue(target, value);
                }
                else
                {
                    _field.SetValue(target, value);
                }
            }
        }

        private enum CollectionIndexKind
        {
            None,
            Indexed,
            Append
        }

        private readonly struct MemberPathSegment
        {
            public MemberPathSegment(string name, CollectionIndexKind indexKind, int index)
            {
                Name = name;
                IndexKind = indexKind;
                Index = index;
            }

            public string Name { get; }
            public CollectionIndexKind IndexKind { get; }
            public int Index { get; }
        }

        private static List<MemberPathSegment> ParseMemberPath(string path, out string error)
        {
            error = null;
            var result = new List<MemberPathSegment>();

            string[] segments = path.Split('.');
            foreach (string rawSegment in segments)
            {
                if (string.IsNullOrWhiteSpace(rawSegment))
                {
                    error = $"Invalid member path '{path}'.";
                    return null;
                }

                int openBracket = rawSegment.IndexOf('[');
                if (openBracket < 0)
                {
                    result.Add(new MemberPathSegment(rawSegment, CollectionIndexKind.None, -1));
                    continue;
                }

                int closeBracket = rawSegment.IndexOf(']', openBracket + 1);
                if (closeBracket < 0 || closeBracket != rawSegment.Length - 1)
                {
                    error = $"Invalid collection path segment '{rawSegment}' in '{path}'.";
                    return null;
                }

                string memberName = rawSegment.Substring(0, openBracket);
                string indexToken = rawSegment.Substring(openBracket + 1, closeBracket - openBracket - 1).Trim();
                if (string.IsNullOrWhiteSpace(memberName))
                {
                    error = $"Invalid collection path segment '{rawSegment}' in '{path}'.";
                    return null;
                }

                if (indexToken == "+" || indexToken.Length == 0)
                {
                    result.Add(new MemberPathSegment(memberName, CollectionIndexKind.Append, -1));
                    continue;
                }

                if (!int.TryParse(indexToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 0)
                {
                    error = $"Invalid collection index '{indexToken}' in segment '{rawSegment}'.";
                    return null;
                }

                result.Add(new MemberPathSegment(memberName, CollectionIndexKind.Indexed, index));
            }

            return result;
        }

        private static bool SetNestedValue(object target, Type targetType, List<MemberPathSegment> segments, int segmentIndex, JToken input, string fullPath, out object updatedTarget, out string error)
        {
            updatedTarget = target;
            error = null;

            MemberPathSegment segment = segments[segmentIndex];
            if (!TryResolveMember(targetType, segment.Name, out MemberAccessor member))
            {
                error = $"Unknown member segment '{segment.Name}' while resolving '{fullPath}' on type '{targetType.Name}'.";
                return false;
            }

            if (!member.CanWrite && segmentIndex == segments.Count - 1)
            {
                error = $"Member '{member.Name}' on type '{targetType.Name}' is read-only.";
                return false;
            }

            object memberValue = member.GetValue(target);
            Type memberType = member.MemberType;
            bool isLeaf = segmentIndex == segments.Count - 1;

            if (segment.IndexKind != CollectionIndexKind.None)
            {
                if (!ApplyCollectionSegment(member, target, memberType, memberValue, segment, segments, segmentIndex, input, fullPath, out updatedTarget, out error))
                {
                    return false;
                }

                return true;
            }

            if (isLeaf)
            {
                if (!TryConvertValue(input, memberType, fullPath, out object converted, out error))
                {
                    return false;
                }

                member.SetValue(target, converted);
                updatedTarget = target;
                return true;
            }

            if (memberValue == null)
            {
                error = $"Member segment '{segment.Name}' is null while resolving '{fullPath}'.";
                return false;
            }

            if (!SetNestedValue(memberValue, memberType, segments, segmentIndex + 1, input, fullPath, out object updatedChild, out error))
            {
                return false;
            }

            if (memberType.IsValueType || !ReferenceEquals(memberValue, updatedChild))
            {
                member.SetValue(target, updatedChild);
            }

            updatedTarget = target;
            return true;
        }

        private static bool ApplyCollectionSegment(MemberAccessor member, object parentTarget, Type collectionType, object collectionValue, MemberPathSegment segment, List<MemberPathSegment> segments, int segmentIndex, JToken input, string fullPath, out object updatedTarget, out string error)
        {
            updatedTarget = parentTarget;
            error = null;

            if (!(collectionValue is IList list))
            {
                error = $"Member '{member.Name}' on type '{parentTarget.GetType().Name}' is not a list/array.";
                return false;
            }

            Type itemType = GetCollectionItemType(collectionType);
            bool isLeaf = segmentIndex == segments.Count - 1;

            if (segment.IndexKind == CollectionIndexKind.Append)
            {
                if (collectionType.IsArray)
                {
                    error = $"Append mode is not supported for array member '{member.Name}'.";
                    return false;
                }

                if (!isLeaf)
                {
                    error = $"Append mode can only be used at the final segment for '{fullPath}'.";
                    return false;
                }

                if (!TryConvertValue(input, itemType, fullPath, out object appendedItem, out error, true))
                {
                    return false;
                }

                list.Add(appendedItem);
                if (!TryWriteCollectionBack(member, parentTarget, list, fullPath, out error))
                {
                    return false;
                }

                updatedTarget = parentTarget;
                return true;
            }

            if (segment.Index >= list.Count)
            {
                error = $"Index {segment.Index} is out of range for member '{member.Name}' (count={list.Count}).";
                return false;
            }

            if (isLeaf)
            {
                if (!TryConvertValue(input, itemType, fullPath, out object convertedItem, out error, true))
                {
                    return false;
                }

                list[segment.Index] = convertedItem;
                if (!TryWriteCollectionBack(member, parentTarget, list, fullPath, out error))
                {
                    return false;
                }

                updatedTarget = parentTarget;
                return true;
            }

            object childValue = list[segment.Index];
            if (childValue == null)
            {
                error = $"Collection element at index {segment.Index} is null while resolving '{fullPath}'.";
                return false;
            }

            if (!SetNestedValue(childValue, itemType, segments, segmentIndex + 1, input, fullPath, out object updatedChild, out error))
            {
                return false;
            }

            if (itemType.IsValueType || !ReferenceEquals(childValue, updatedChild))
            {
                list[segment.Index] = updatedChild;
            }

            if (!TryWriteCollectionBack(member, parentTarget, list, fullPath, out error))
            {
                return false;
            }

            updatedTarget = parentTarget;
            return true;
        }

        private static bool TryWriteCollectionBack(MemberAccessor member, object parentTarget, IList list, string fullPath, out string error)
        {
            error = null;

            if (!member.IsProperty)
            {
                return true;
            }

            if (!member.CanWrite)
            {
                error = $"Member '{member.Name}' on type '{parentTarget.GetType().Name}' is read-only and cannot be updated for '{fullPath}'.";
                return false;
            }

            member.SetValue(parentTarget, list);
            return true;
        }

        private static bool TryResolveMember(Type type, string memberName, out MemberAccessor accessor)
        {
            accessor = null;
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
            string normalizedName = ParamCoercion.NormalizePropertyName(memberName);

            PropertyInfo propInfo = type.GetProperty(memberName, flags)
                                 ?? type.GetProperty(normalizedName, flags);
            if (propInfo != null)
            {
                accessor = new MemberAccessor(propInfo);
                return true;
            }

            FieldInfo fieldInfo = type.GetField(memberName, flags)
                               ?? type.GetField(normalizedName, flags)
                               ?? FindSerializedFieldInHierarchy(type, memberName)
                               ?? FindSerializedFieldInHierarchy(type, normalizedName);
            if (fieldInfo != null)
            {
                accessor = new MemberAccessor(fieldInfo);
                return true;
            }

            return false;
        }

        private static bool TryConvertValue(JToken input, Type targetType, string path, out object converted, out string error, bool isCollectionItem = false)
        {
            converted = null;
            error = null;

            if (typeof(IList).IsAssignableFrom(targetType) && input is JArray inputArray)
            {
                return TryConvertCollection(inputArray, targetType, path, out converted, out error);
            }

            if (targetType.IsEnum)
            {
                if (!TryConvertEnum(input, targetType, out converted, out error))
                {
                    return false;
                }

                return true;
            }

            string label = isCollectionItem
                ? $"collection item '{path}'"
                : $"member '{path}'";

            return TryConvertForMember(input, targetType, label, out converted, out error);
        }

        private static bool TryConvertEnum(JToken input, Type enumType, out object converted, out string error)
        {
            converted = null;
            error = null;

            Type underlyingType = Enum.GetUnderlyingType(enumType);
            if (input.Type == JTokenType.String)
            {
                string symbol = input.ToString();
                foreach (string enumName in Enum.GetNames(enumType))
                {
                    if (string.Equals(enumName, symbol, StringComparison.OrdinalIgnoreCase))
                    {
                        converted = Enum.Parse(enumType, enumName, ignoreCase: true);
                        return true;
                    }
                }

                if (long.TryParse(symbol, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numericValue))
                {
                    converted = Enum.ToObject(enumType, Convert.ChangeType(numericValue, underlyingType, CultureInfo.InvariantCulture));
                    return true;
                }

                error = $"Invalid enum symbol '{symbol}' for enum '{enumType.Name}'.";
                return false;
            }

            try
            {
                object rawNumeric = input.ToObject(underlyingType, Newtonsoft.Json.JsonSerializer.CreateDefault());
                converted = Enum.ToObject(enumType, rawNumeric);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to convert enum value to '{enumType.Name}': {ex.Message}";
                return false;
            }
        }

        private static bool TryConvertCollection(JArray inputArray, Type targetType, string path, out object converted, out string error)
        {
            converted = null;
            error = null;

            Type itemType = GetCollectionItemType(targetType);

            if (targetType.IsArray)
            {
                Array result = Array.CreateInstance(itemType, inputArray.Count);
                for (int i = 0; i < inputArray.Count; i++)
                {
                    if (!TryConvertValue(inputArray[i], itemType, path, out object item, out error, true))
                    {
                        return false;
                    }
                    result.SetValue(item, i);
                }

                converted = result;
                return true;
            }

            if (!typeof(IList).IsAssignableFrom(targetType))
            {
                return TryConvertValue(inputArray, targetType, path, out converted, out error);
            }

            IList list;
            if (targetType.IsInterface || targetType.IsAbstract)
            {
                Type listType = typeof(List<>).MakeGenericType(itemType);
                list = (IList)Activator.CreateInstance(listType);
            }
            else
            {
                list = (IList)Activator.CreateInstance(targetType);
            }

            foreach (var itemToken in inputArray)
            {
                if (!TryConvertValue(itemToken, itemType, path, out object item, out error, true))
                {
                    return false;
                }

                list.Add(item);
            }

            converted = list;
            return true;
        }

        private static Type GetCollectionItemType(Type collectionType)
        {
            if (collectionType.IsArray)
            {
                return collectionType.GetElementType();
            }

            if (collectionType.IsGenericType)
            {
                return collectionType.GetGenericArguments()[0];
            }

            return typeof(object);
        }

        /// <summary>
        /// Searches for a non-public [SerializeField] field through the entire inheritance hierarchy.
        /// Type.GetField() with NonPublic only returns fields declared directly on that type,
        /// so this method walks up the chain to find inherited private serialized fields.
        /// </summary>
        private static FieldInfo FindSerializedFieldInHierarchy(Type type, string fieldName)
        {
            if (type == null || string.IsNullOrEmpty(fieldName))
                return null;

            BindingFlags privateFlags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            Type currentType = type;

            // Walk up the inheritance chain
            while (currentType != null && currentType != typeof(object))
            {
                // Search for the field on this specific type (case-insensitive)
                foreach (var field in currentType.GetFields(privateFlags))
                {
                    if (string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase) &&
                        field.GetCustomAttribute<SerializeField>() != null)
                    {
                        return field;
                    }
                }
                currentType = currentType.BaseType;
            }

            return null;
        }

        private static string CheckPhysicsConflict(GameObject target, Type componentType)
        {
            bool isAdding2DPhysics =
                typeof(Rigidbody2D).IsAssignableFrom(componentType) ||
                typeof(Collider2D).IsAssignableFrom(componentType);

            bool isAdding3DPhysics =
                typeof(Rigidbody).IsAssignableFrom(componentType) ||
                typeof(Collider).IsAssignableFrom(componentType);

            if (isAdding2DPhysics)
            {
                if (target.GetComponent<Rigidbody>() != null || target.GetComponent<Collider>() != null)
                {
                    return $"Cannot add 2D physics component '{componentType.Name}' because the GameObject '{target.name}' already has a 3D Rigidbody or Collider.";
                }
            }
            else if (isAdding3DPhysics)
            {
                if (target.GetComponent<Rigidbody2D>() != null || target.GetComponent<Collider2D>() != null)
                {
                    return $"Cannot add 3D physics component '{componentType.Name}' because the GameObject '{target.name}' already has a 2D Rigidbody or Collider.";
                }
            }

            return null;
        }

        private static void ApplyDefaultValues(Component component)
        {
            // Default newly added Lights to Directional
            if (component is Light light)
            {
                light.type = LightType.Directional;
            }
        }

        private static bool AllowsMultiple(GameObject target, Type componentType)
        {
            if (target == null || componentType == null)
            {
                return false;
            }

            if (Attribute.IsDefined(componentType, typeof(DisallowMultipleComponent), inherit: true))
            {
                return false;
            }

            return true;
        }
    }
}
