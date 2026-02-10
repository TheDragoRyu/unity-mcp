using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor; // Required for AssetDatabase and EditorUtility
#endif

namespace MCPForUnity.Runtime.Serialization
{
    public static class UnityAssetReferenceResolver
    {
#if UNITY_EDITOR
        private const string CanonicalTypeName = "AssetReference";

        private readonly struct ParsedSubAsset
        {
            public readonly string Name;
            public readonly string TypeName;

            public ParsedSubAsset(string name, string typeName)
            {
                Name = name;
                TypeName = typeName;
            }
        }

        public static bool TryResolveAssetReference(JToken token, Type expectedType, out UnityEngine.Object resolved, out string error)
        {
            resolved = null;
            error = null;

            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            if (!TryExtractReference(token, out string path, out string guid, out string subAsset, out string extractError))
            {
                error = extractError;
                return false;
            }

            if (!TryResolvePathOrGuid(path, guid, out string resolvedPath, out error))
            {
                return false;
            }

            if (string.IsNullOrEmpty(subAsset))
            {
                resolved = AssetDatabase.LoadAssetAtPath(resolvedPath, expectedType);
                if (resolved == null)
                {
                    error = $"Could not load asset at path '{resolvedPath}' as type '{expectedType.Name}'.";
                    return false;
                }
                return true;
            }

            var allAssets = AssetDatabase.LoadAllAssetsAtPath(resolvedPath);
            if (allAssets == null || allAssets.Length == 0)
            {
                error = $"No assets found at path '{resolvedPath}' while resolving sub-asset '{subAsset}'.";
                return false;
            }

            ParsedSubAsset subAssetSpec = ParseSubAsset(subAsset);
            foreach (var candidate in allAssets)
            {
                if (candidate == null)
                    continue;

                if (!expectedType.IsAssignableFrom(candidate.GetType()))
                    continue;

                if (!MatchesSubAsset(candidate, subAssetSpec))
                    continue;

                resolved = candidate;
                return true;
            }

            error = $"Could not resolve sub-asset '{subAsset}' at path '{resolvedPath}' as assignable to '{expectedType.Name}'.";
            return false;
        }

        private static bool TryExtractReference(JToken token, out string path, out string guid, out string subAsset, out string error)
        {
            path = null;
            guid = null;
            subAsset = null;
            error = null;

            if (token.Type == JTokenType.String)
            {
                string strValue = token.Value<string>();
                if (IsValidGuid(strValue))
                {
                    guid = strValue;
                }
                else
                {
                    path = strValue;
                }
                return true;
            }

            if (token.Type != JTokenType.Object)
            {
                error = $"Unsupported asset reference format '{token.Type}'. Expected string path/guid or object.";
                return false;
            }

            var jo = (JObject)token;
            string typeMarker = jo["type"]?.Value<string>();
            if (!string.IsNullOrEmpty(typeMarker) && !string.Equals(typeMarker, CanonicalTypeName, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Unsupported typed reference '{typeMarker}'. Expected type '{CanonicalTypeName}'.";
                return false;
            }

            path = jo["path"]?.Value<string>();
            guid = jo["guid"]?.Value<string>();
            subAsset = jo["subAsset"]?.Value<string>();

            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(guid))
            {
                error = $"Asset reference object must include 'path' or 'guid'. Object: {jo.ToString(Formatting.None)}";
                return false;
            }

            return true;
        }

        private static bool TryResolvePathOrGuid(string path, string guid, out string resolvedPath, out string error)
        {
            resolvedPath = null;
            error = null;

            if (!string.IsNullOrEmpty(guid))
            {
                string normalizedGuid = guid.Replace("-", "").ToLowerInvariant();
                resolvedPath = AssetDatabase.GUIDToAssetPath(normalizedGuid);
                if (string.IsNullOrEmpty(resolvedPath))
                {
                    error = $"Could not find asset path for GUID '{guid}'.";
                    return false;
                }
                return true;
            }

            resolvedPath = NormalizeAssetPath(path);
            if (string.IsNullOrEmpty(resolvedPath))
            {
                error = $"Invalid asset path '{path}'.";
                return false;
            }
            return true;
        }


        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string normalized = path.Replace('\\', '/').Trim();
            if (normalized.Contains(".."))
            {
                return null;
            }

            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "Assets/" + normalized.TrimStart('/');
            }

            return normalized;
        }

        private static ParsedSubAsset ParseSubAsset(string subAsset)
        {
            if (string.IsNullOrWhiteSpace(subAsset))
            {
                return new ParsedSubAsset(null, null);
            }

            string trimmed = subAsset.Trim();
            int separatorIndex = trimmed.IndexOf(':');
            if (separatorIndex <= 0)
            {
                return new ParsedSubAsset(trimmed, null);
            }

            string typeName = trimmed.Substring(0, separatorIndex).Trim();
            string name = trimmed.Substring(separatorIndex + 1).Trim();
            return new ParsedSubAsset(string.IsNullOrEmpty(name) ? null : name, string.IsNullOrEmpty(typeName) ? null : typeName);
        }

        private static bool MatchesSubAsset(UnityEngine.Object candidate, ParsedSubAsset spec)
        {
            if (!string.IsNullOrEmpty(spec.TypeName) && !TypeMatches(candidate.GetType(), spec.TypeName))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(spec.Name) && !string.Equals(candidate.name, spec.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static bool TypeMatches(Type candidateType, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return true;
            }

            var current = candidateType;
            while (current != null)
            {
                if (string.Equals(current.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(current.FullName, typeName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                current = current.BaseType;
            }

            return false;
        }
#endif

        public static bool IsValidGuid(string str)
        {
            if (string.IsNullOrEmpty(str)) return false;
            string normalized = str.Replace("-", "");
            if (normalized.Length != 32) return false;
            foreach (char c in normalized)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }
    }

    public class Vector3Converter : JsonConverter<Vector3>
    {
        public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WritePropertyName("z");
            writer.WriteValue(value.z);
            writer.WriteEndObject();
        }

        public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            return new Vector3(
                (float)jo["x"],
                (float)jo["y"],
                (float)jo["z"]
            );
        }
    }

    public class Vector2Converter : JsonConverter<Vector2>
    {
        public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WriteEndObject();
        }

        public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            return new Vector2(
                (float)jo["x"],
                (float)jo["y"]
            );
        }
    }

    public class QuaternionConverter : JsonConverter<Quaternion>
    {
        public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WritePropertyName("z");
            writer.WriteValue(value.z);
            writer.WritePropertyName("w");
            writer.WriteValue(value.w);
            writer.WriteEndObject();
        }

        public override Quaternion ReadJson(JsonReader reader, Type objectType, Quaternion existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            return new Quaternion(
                (float)jo["x"],
                (float)jo["y"],
                (float)jo["z"],
                (float)jo["w"]
            );
        }
    }

    public class ColorConverter : JsonConverter<Color>
    {
        public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("r");
            writer.WriteValue(value.r);
            writer.WritePropertyName("g");
            writer.WriteValue(value.g);
            writer.WritePropertyName("b");
            writer.WriteValue(value.b);
            writer.WritePropertyName("a");
            writer.WriteValue(value.a);
            writer.WriteEndObject();
        }

        public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            return new Color(
                (float)jo["r"],
                (float)jo["g"],
                (float)jo["b"],
                (float)jo["a"]
            );
        }
    }

    public class RectConverter : JsonConverter<Rect>
    {
        public override void WriteJson(JsonWriter writer, Rect value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WritePropertyName("width");
            writer.WriteValue(value.width);
            writer.WritePropertyName("height");
            writer.WriteValue(value.height);
            writer.WriteEndObject();
        }

        public override Rect ReadJson(JsonReader reader, Type objectType, Rect existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            return new Rect(
                (float)jo["x"],
                (float)jo["y"],
                (float)jo["width"],
                (float)jo["height"]
            );
        }
    }

    public class BoundsConverter : JsonConverter<Bounds>
    {
        public override void WriteJson(JsonWriter writer, Bounds value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("center");
            serializer.Serialize(writer, value.center); // Use serializer to handle nested Vector3
            writer.WritePropertyName("size");
            serializer.Serialize(writer, value.size);   // Use serializer to handle nested Vector3
            writer.WriteEndObject();
        }

        public override Bounds ReadJson(JsonReader reader, Type objectType, Bounds existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            Vector3 center = jo["center"].ToObject<Vector3>(serializer); // Use serializer to handle nested Vector3
            Vector3 size = jo["size"].ToObject<Vector3>(serializer);     // Use serializer to handle nested Vector3
            return new Bounds(center, size);
        }
    }

    public class Vector4Converter : JsonConverter<Vector4>
    {
        public override void WriteJson(JsonWriter writer, Vector4 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WritePropertyName("z");
            writer.WriteValue(value.z);
            writer.WritePropertyName("w");
            writer.WriteValue(value.w);
            writer.WriteEndObject();
        }

        public override Vector4 ReadJson(JsonReader reader, Type objectType, Vector4 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            return new Vector4(
                (float)jo["x"],
                (float)jo["y"],
                (float)jo["z"],
                (float)jo["w"]
            );
        }
    }

    /// <summary>
    /// Safe converter for Matrix4x4 that only accesses raw matrix elements (m00-m33).
    /// Avoids computed properties (lossyScale, rotation, inverse) that call ValidTRS()
    /// and can crash Unity on non-TRS matrices (common in Cinemachine components).
    /// Fixes: https://github.com/CoplayDev/unity-mcp/issues/478
    /// </summary>
    public class Matrix4x4Converter : JsonConverter<Matrix4x4>
    {
        public override void WriteJson(JsonWriter writer, Matrix4x4 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            // Only access raw matrix elements - NEVER computed properties like lossyScale/rotation
            writer.WritePropertyName("m00"); writer.WriteValue(value.m00);
            writer.WritePropertyName("m01"); writer.WriteValue(value.m01);
            writer.WritePropertyName("m02"); writer.WriteValue(value.m02);
            writer.WritePropertyName("m03"); writer.WriteValue(value.m03);
            writer.WritePropertyName("m10"); writer.WriteValue(value.m10);
            writer.WritePropertyName("m11"); writer.WriteValue(value.m11);
            writer.WritePropertyName("m12"); writer.WriteValue(value.m12);
            writer.WritePropertyName("m13"); writer.WriteValue(value.m13);
            writer.WritePropertyName("m20"); writer.WriteValue(value.m20);
            writer.WritePropertyName("m21"); writer.WriteValue(value.m21);
            writer.WritePropertyName("m22"); writer.WriteValue(value.m22);
            writer.WritePropertyName("m23"); writer.WriteValue(value.m23);
            writer.WritePropertyName("m30"); writer.WriteValue(value.m30);
            writer.WritePropertyName("m31"); writer.WriteValue(value.m31);
            writer.WritePropertyName("m32"); writer.WriteValue(value.m32);
            writer.WritePropertyName("m33"); writer.WriteValue(value.m33);
            writer.WriteEndObject();
        }

        public override Matrix4x4 ReadJson(JsonReader reader, Type objectType, Matrix4x4 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return new Matrix4x4(); // Return zero matrix for null (consistent with missing field defaults)

            if (reader.TokenType != JsonToken.StartObject)
                throw new JsonSerializationException($"Expected JSON object or null when deserializing Matrix4x4, got '{reader.TokenType}'.");

            JObject jo = JObject.Load(reader);
            var matrix = new Matrix4x4();
            matrix.m00 = jo["m00"]?.Value<float>() ?? 0f;
            matrix.m01 = jo["m01"]?.Value<float>() ?? 0f;
            matrix.m02 = jo["m02"]?.Value<float>() ?? 0f;
            matrix.m03 = jo["m03"]?.Value<float>() ?? 0f;
            matrix.m10 = jo["m10"]?.Value<float>() ?? 0f;
            matrix.m11 = jo["m11"]?.Value<float>() ?? 0f;
            matrix.m12 = jo["m12"]?.Value<float>() ?? 0f;
            matrix.m13 = jo["m13"]?.Value<float>() ?? 0f;
            matrix.m20 = jo["m20"]?.Value<float>() ?? 0f;
            matrix.m21 = jo["m21"]?.Value<float>() ?? 0f;
            matrix.m22 = jo["m22"]?.Value<float>() ?? 0f;
            matrix.m23 = jo["m23"]?.Value<float>() ?? 0f;
            matrix.m30 = jo["m30"]?.Value<float>() ?? 0f;
            matrix.m31 = jo["m31"]?.Value<float>() ?? 0f;
            matrix.m32 = jo["m32"]?.Value<float>() ?? 0f;
            matrix.m33 = jo["m33"]?.Value<float>() ?? 0f;
            return matrix;
        }
    }

    // Converter for UnityEngine.Object references (GameObjects, Components, Materials, Textures, etc.)
    public class UnityEngineObjectConverter : JsonConverter<UnityEngine.Object>
    {
        public override bool CanRead => true; // We need to implement ReadJson
        public override bool CanWrite => true;

        public override void WriteJson(JsonWriter writer, UnityEngine.Object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

#if UNITY_EDITOR // AssetDatabase and EditorUtility are Editor-only
            if (UnityEditor.AssetDatabase.Contains(value))
            {
                // It's an asset (Material, Texture, Prefab, etc.)
                string path = UnityEditor.AssetDatabase.GetAssetPath(value);
                if (!string.IsNullOrEmpty(path))
                {
                    writer.WriteValue(path);
                }
                else
                {
                    // Asset exists but path couldn't be found? Write minimal info.
                    writer.WriteStartObject();
                    writer.WritePropertyName("name");
                    writer.WriteValue(value.name);
                    writer.WritePropertyName("instanceID");
                    writer.WriteValue(value.GetInstanceID());
                    writer.WritePropertyName("isAssetWithoutPath");
                    writer.WriteValue(true);
                    writer.WriteEndObject();
                }
            }
            else
            {
                // It's a scene object (GameObject, Component, etc.)
                writer.WriteStartObject();
                writer.WritePropertyName("name");
                writer.WriteValue(value.name);
                writer.WritePropertyName("instanceID");
                writer.WriteValue(value.GetInstanceID());
                writer.WriteEndObject();
            }
#else
            // Runtime fallback: Write basic info without AssetDatabase
            writer.WriteStartObject();
            writer.WritePropertyName("name");
            writer.WriteValue(value.name);
            writer.WritePropertyName("instanceID");
            writer.WriteValue(value.GetInstanceID());
             writer.WritePropertyName("warning");
            writer.WriteValue("UnityEngineObjectConverter running in non-Editor mode, asset path unavailable.");
            writer.WriteEndObject();
#endif
        }

        public override UnityEngine.Object ReadJson(JsonReader reader, Type objectType, UnityEngine.Object existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

#if UNITY_EDITOR
            if (reader.TokenType == JsonToken.String)
            {
                string strValue = reader.Value.ToString();
                if (UnityAssetReferenceResolver.TryResolveAssetReference(JToken.FromObject(strValue), objectType, out UnityEngine.Object loadedAsset, out string error))
                {
                    return loadedAsset;
                }

                UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] {error}");
                return null;
            }

            if (reader.TokenType == JsonToken.StartObject)
            {
                JObject jo = JObject.Load(reader);

                // Try to resolve as typed/legacy asset reference first
                if (jo.TryGetValue("type", out _) || jo.TryGetValue("guid", out _) || jo.TryGetValue("path", out _) || jo.TryGetValue("subAsset", out _))
                {
                    if (UnityAssetReferenceResolver.TryResolveAssetReference(jo, objectType, out UnityEngine.Object asset, out string error))
                    {
                        return asset;
                    }

                    UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] {error}");
                    return null;
                }

                // Try to resolve by instanceID
                if (jo.TryGetValue("instanceID", out JToken idToken) && idToken.Type == JTokenType.Integer)
                {
                    int instanceId = idToken.ToObject<int>();
                    UnityEngine.Object obj = UnityEditor.EditorUtility.InstanceIDToObject(instanceId);
                    if (obj != null)
                    {
                        // Direct type match
                        if (objectType.IsAssignableFrom(obj.GetType()))
                        {
                            return obj;
                        }

                        // Special case: expecting Transform but got GameObject - get its transform
                        if (objectType == typeof(Transform) && obj is GameObject go)
                        {
                            return go.transform;
                        }

                        // Special case: expecting a Component type but got GameObject - try to get the component
                        if (typeof(Component).IsAssignableFrom(objectType) && obj is GameObject gameObj)
                        {
                            var component = gameObj.GetComponent(objectType);
                            if (component != null)
                            {
                                return component;
                            }
                            UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] GameObject '{gameObj.name}' (ID: {instanceId}) does not have a '{objectType.Name}' component.");
                            return null;
                        }

                        // Type mismatch with no automatic conversion available
                        UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] Instance ID {instanceId} resolved to '{obj.GetType().Name}' but expected '{objectType.Name}'.");
                        return null;
                    }
                    // Instance ID lookup failed - this can happen if the object was destroyed or ID is stale
                    string objectName = jo.TryGetValue("name", out JToken nameToken) ? nameToken.ToString() : "unknown";
                    UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] Could not resolve instance ID {instanceId} (name: '{objectName}') to a valid {objectType.Name}. The object may have been destroyed or the ID is stale.");
                    return null;
                }

                // Object format not recognized
                UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] JSON object missing 'instanceID', 'guid', or 'path' field for {objectType.Name} deserialization. Object: {jo.ToString(Formatting.None)}");
                return null;
            }

            // Unexpected token type
            UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] Unexpected token type '{reader.TokenType}' when deserializing {objectType.Name}. Expected Null, String, or Object.");
            return null;
#else
            // Runtime deserialization is tricky without AssetDatabase/EditorUtility
            UnityEngine.Debug.LogWarning("UnityEngineObjectConverter cannot deserialize complex objects in non-Editor mode.");
            // Skip the current token to avoid breaking the reader state
            reader.Skip();
            // Return existing value since we can't deserialize without Editor APIs
            return existingValue;
#endif
        }

    }
}
