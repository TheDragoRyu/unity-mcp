using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Tool for managing components on GameObjects.
    /// Actions: add, remove, set_property, set_enabled, set_anchor_preset,
    /// configure_layout_element, configure_content_size_fitter, configure_canvas_scaler
    /// 
    /// This is a focused tool for component lifecycle operations.
    /// For reading component data, use the unity://scene/gameobject/{id}/components resource.
    /// </summary>
    [McpForUnityTool("manage_components")]
    public static class ManageComponents
    {
        /// <summary>
        /// Handles the manage_components command.
        /// </summary>
        /// <param name="params">Command parameters</param>
        /// <returns>Result of the component operation</returns>
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            string action = ParamCoercion.CoerceString(@params["action"], null)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("'action' parameter is required (add, remove, set_property, set_enabled, set_anchor_preset, configure_layout_element, configure_content_size_fitter, configure_canvas_scaler).");
            }

            // Target resolution
            JToken targetToken = @params["target"];
            string searchMethod = ParamCoercion.CoerceString(@params["searchMethod"] ?? @params["search_method"], null);

            if (targetToken == null)
            {
                return new ErrorResponse("'target' parameter is required.");
            }

            try
            {
                return action switch
                {
                    "add" => AddComponent(@params, targetToken, searchMethod),
                    "remove" => RemoveComponent(@params, targetToken, searchMethod),
                    "set_property" => SetProperty(@params, targetToken, searchMethod),
                    "set_enabled" => SetEnabled(@params, targetToken, searchMethod),
                    "set_anchor_preset" => SetAnchorPreset(@params, targetToken, searchMethod),
                    "configure_layout_element" => ConfigureLayoutElement(@params, targetToken, searchMethod),
                    "configure_content_size_fitter" => ConfigureContentSizeFitter(@params, targetToken, searchMethod),
                    "configure_canvas_scaler" => ConfigureCanvasScaler(@params, targetToken, searchMethod),
                    _ => new ErrorResponse($"Unknown action: '{action}'. Supported actions: add, remove, set_property, set_enabled, set_anchor_preset, configure_layout_element, configure_content_size_fitter, configure_canvas_scaler")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageComponents] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        #region Action Implementations

        private static object AddComponent(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string componentTypeName = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentTypeName))
            {
                return new ErrorResponse("'componentType' parameter is required for 'add' action.");
            }

            // Resolve component type using unified type resolver
            Type type = UnityTypeResolver.ResolveComponent(componentTypeName);
            if (type == null)
            {
                return new ErrorResponse($"Component type '{componentTypeName}' not found. Use a fully-qualified name if needed.");
            }

            // Use ComponentOps for the actual operation
            Component newComponent = ComponentOps.AddComponent(targetGo, type, out string error);
            if (newComponent == null)
            {
                return new ErrorResponse(error ?? $"Failed to add component '{componentTypeName}'.");
            }

            // Set properties if provided
            JObject properties = @params["properties"] as JObject ?? @params["componentProperties"] as JObject;
            if (properties != null && properties.HasValues)
            {
                // Record for undo before modifying properties
                Undo.RecordObject(newComponent, "Modify Component Properties");
                SetPropertiesOnComponent(newComponent, properties);
            }

            EditorUtility.SetDirty(targetGo);
            MarkOwningSceneDirty(targetGo);

            return new
            {
                success = true,
                message = $"Component '{componentTypeName}' added to '{targetGo.name}'.",
                data = new
                {
                    instanceID = targetGo.GetInstanceID(),
                    componentType = type.FullName,
                    componentInstanceID = newComponent.GetInstanceID()
                }
            };
        }

        private static object RemoveComponent(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string componentTypeName = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentTypeName))
            {
                return new ErrorResponse("'componentType' parameter is required for 'remove' action.");
            }

            // Resolve component type using unified type resolver
            Type type = UnityTypeResolver.ResolveComponent(componentTypeName);
            if (type == null)
            {
                return new ErrorResponse($"Component type '{componentTypeName}' not found.");
            }

            // Use ComponentOps for the actual operation
            bool removed = ComponentOps.RemoveComponent(targetGo, type, out string error);
            if (!removed)
            {
                return new ErrorResponse(error ?? $"Failed to remove component '{componentTypeName}'.");
            }

            EditorUtility.SetDirty(targetGo);
            MarkOwningSceneDirty(targetGo);

            return new
            {
                success = true,
                message = $"Component '{componentTypeName}' removed from '{targetGo.name}'.",
                data = new
                {
                    instanceID = targetGo.GetInstanceID()
                }
            };
        }

        private static object SetProperty(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string componentType = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentType))
            {
                return new ErrorResponse("'componentType' parameter is required for 'set_property' action.");
            }

            // Resolve component type using unified type resolver
            Type type = UnityTypeResolver.ResolveComponent(componentType);
            if (type == null)
            {
                return new ErrorResponse($"Component type '{componentType}' not found.");
            }

            Component component = targetGo.GetComponent(type);
            if (component == null)
            {
                return new ErrorResponse($"Component '{componentType}' not found on '{targetGo.name}'.");
            }

            // Get property and value
            string propertyName = ParamCoercion.CoerceString(@params["property"], null);
            JToken valueToken = @params["value"];

            // Support both single property or properties object
            JObject properties = @params["properties"] as JObject;

            if (string.IsNullOrEmpty(propertyName) && (properties == null || !properties.HasValues))
            {
                return new ErrorResponse("Either 'property'+'value' or 'properties' object is required for 'set_property' action.");
            }

            var errors = new List<string>();

            try
            {
                Undo.RecordObject(component, $"Set property on {componentType}");

                if (!string.IsNullOrEmpty(propertyName) && valueToken != null)
                {
                    // Single property mode
                    var error = TrySetProperty(component, propertyName, valueToken);
                    if (error != null)
                    {
                        errors.Add(error);
                    }
                }

                if (properties != null && properties.HasValues)
                {
                    // Multiple properties mode
                    foreach (var prop in properties.Properties())
                    {
                        var error = TrySetProperty(component, prop.Name, prop.Value);
                        if (error != null)
                        {
                            errors.Add(error);
                        }
                    }
                }

                EditorUtility.SetDirty(component);
                MarkOwningSceneDirty(targetGo);

                if (errors.Count > 0)
                {
                    return new
                    {
                        success = false,
                        message = $"Some properties failed to set on '{componentType}'.",
                        data = new
                        {
                            instanceID = targetGo.GetInstanceID(),
                            errors = errors
                        }
                    };
                }

                return new
                {
                    success = true,
                    message = $"Properties set on component '{componentType}' on '{targetGo.name}'.",
                    data = new
                    {
                        instanceID = targetGo.GetInstanceID()
                    }
                };
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error setting properties on component '{componentType}': {e.Message}");
            }
        }

        private static object SetEnabled(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            bool? enabledState = ParamCoercion.CoerceBoolNullable(@params["value"] ?? @params["enabled"]);
            if (!enabledState.HasValue)
            {
                return new ErrorResponse("'value' (or 'enabled') boolean parameter is required for 'set_enabled' action.");
            }

            string componentTypeName = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            var targetComponents = new List<Component>();

            if (!string.IsNullOrEmpty(componentTypeName))
            {
                Type targetType = UnityTypeResolver.ResolveComponent(componentTypeName);
                if (targetType == null)
                {
                    return new ErrorResponse($"Component type '{componentTypeName}' not found.");
                }

                Component found = targetGo.GetComponent(targetType);
                if (found == null)
                {
                    return new ErrorResponse($"Component '{componentTypeName}' not found on '{targetGo.name}'.");
                }

                targetComponents.Add(found);
            }
            else
            {
                targetComponents.AddRange(targetGo.GetComponents<Component>());
            }

            var toggled = new List<string>();
            foreach (var component in targetComponents)
            {
                if (component is Behaviour behaviour)
                {
                    Undo.RecordObject(behaviour, $"Set enabled on {behaviour.GetType().Name}");
                    behaviour.enabled = enabledState.Value;
                    toggled.Add(behaviour.GetType().Name);
                }
                else if (component is Renderer renderer)
                {
                    Undo.RecordObject(renderer, $"Set enabled on {renderer.GetType().Name}");
                    renderer.enabled = enabledState.Value;
                    toggled.Add(renderer.GetType().Name);
                }
            }

            if (toggled.Count == 0)
            {
                return new ErrorResponse(!string.IsNullOrEmpty(componentTypeName)
                    ? $"Component '{componentTypeName}' does not support enabled toggling (Behaviour/Renderer only)."
                    : $"No components on '{targetGo.name}' support enabled toggling (Behaviour/Renderer only).");
            }

            EditorUtility.SetDirty(targetGo);
            MarkOwningSceneDirty(targetGo);

            return new
            {
                success = true,
                message = $"Set enabled={enabledState.Value} on {toggled.Count} component(s) on '{targetGo.name}'.",
                data = new
                {
                    instanceID = targetGo.GetInstanceID(),
                    components = toggled
                }
            };
        }

        private static object SetAnchorPreset(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            RectTransform rectTransform = targetGo.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                return new ErrorResponse($"GameObject '{targetGo.name}' does not have a RectTransform. set_anchor_preset only applies to UI objects.");
            }

            string presetName = ParamCoercion.CoerceString(@params["preset"] ?? @params["presetName"] ?? @params["name"], null);
            if (string.IsNullOrEmpty(presetName))
            {
                return new ErrorResponse("'preset' (or 'presetName') is required for 'set_anchor_preset' action.");
            }

            if (!TryGetAnchorPreset(presetName, out var preset))
            {
                return new ErrorResponse($"Unknown anchor preset '{presetName}'.");
            }

            Undo.RecordObject(rectTransform, $"Set anchor preset '{presetName}'");
            rectTransform.anchorMin = preset.AnchorMin;
            rectTransform.anchorMax = preset.AnchorMax;
            rectTransform.pivot = preset.Pivot;

            Vector2? sizeDelta = ParseVector2Token(@params["sizeDelta"] ?? @params["size_delta"]);
            if (sizeDelta.HasValue)
            {
                rectTransform.sizeDelta = sizeDelta.Value;
            }

            EditorUtility.SetDirty(rectTransform);
            MarkOwningSceneDirty(targetGo);

            return SuccessWithLayoutSummary(targetGo, $"Applied anchor preset '{presetName}' on '{targetGo.name}'.");
        }

        private static object ConfigureLayoutElement(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            bool autoAdd = ParamCoercion.CoerceBool(@params["autoAddComponent"] ?? @params["auto_add_component"], true);
            LayoutElement component = targetGo.GetComponent<LayoutElement>();
            if (component == null)
            {
                if (!autoAdd)
                {
                    return new ErrorResponse($"LayoutElement not found on '{targetGo.name}'. Set autoAddComponent=true to add it automatically.");
                }

                component = targetGo.AddComponent<LayoutElement>();
            }

            JObject properties = MergeProperties(@params, "ignoreLayout", "minWidth", "minHeight", "preferredWidth", "preferredHeight", "flexibleWidth", "flexibleHeight", "layoutPriority");
            if (!ApplyProperties(component, properties, out List<string> errors))
            {
                return new ErrorResponse($"Failed to configure LayoutElement: {string.Join("; ", errors)}");
            }

            EditorUtility.SetDirty(component);
            MarkOwningSceneDirty(targetGo);
            return SuccessWithLayoutSummary(targetGo, $"Configured LayoutElement on '{targetGo.name}'.");
        }

        private static object ConfigureContentSizeFitter(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            bool autoAdd = ParamCoercion.CoerceBool(@params["autoAddComponent"] ?? @params["auto_add_component"], true);
            ContentSizeFitter component = targetGo.GetComponent<ContentSizeFitter>();
            if (component == null)
            {
                if (!autoAdd)
                {
                    return new ErrorResponse($"ContentSizeFitter not found on '{targetGo.name}'. Set autoAddComponent=true to add it automatically.");
                }

                component = targetGo.AddComponent<ContentSizeFitter>();
            }

            JObject properties = MergeProperties(@params, "horizontalFit", "verticalFit");
            if (!ApplyProperties(component, properties, out List<string> errors))
            {
                return new ErrorResponse($"Failed to configure ContentSizeFitter: {string.Join("; ", errors)}");
            }

            EditorUtility.SetDirty(component);
            MarkOwningSceneDirty(targetGo);
            return SuccessWithLayoutSummary(targetGo, $"Configured ContentSizeFitter on '{targetGo.name}'.");
        }

        private static object ConfigureCanvasScaler(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            if (targetGo.GetComponent<Canvas>() == null)
            {
                return new ErrorResponse($"GameObject '{targetGo.name}' must have a Canvas component before configuring CanvasScaler.");
            }

            bool autoAdd = ParamCoercion.CoerceBool(@params["autoAddComponent"] ?? @params["auto_add_component"], true);
            CanvasScaler component = targetGo.GetComponent<CanvasScaler>();
            if (component == null)
            {
                if (!autoAdd)
                {
                    return new ErrorResponse($"CanvasScaler not found on '{targetGo.name}'. Set autoAddComponent=true to add it automatically.");
                }

                component = targetGo.AddComponent<CanvasScaler>();
            }

            JObject properties = MergeProperties(@params, "uiScaleMode", "referenceResolution", "screenMatchMode", "matchWidthOrHeight", "referencePixelsPerUnit", "scaleFactor", "physicalUnit", "fallbackScreenDPI", "defaultSpriteDPI", "dynamicPixelsPerUnit");
            if (!ApplyProperties(component, properties, out List<string> errors))
            {
                return new ErrorResponse($"Failed to configure CanvasScaler: {string.Join("; ", errors)}");
            }

            EditorUtility.SetDirty(component);
            MarkOwningSceneDirty(targetGo);
            return SuccessWithLayoutSummary(targetGo, $"Configured CanvasScaler on '{targetGo.name}'.");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Marks the appropriate scene as dirty for the given GameObject.
        /// Handles both regular scenes and prefab stages.
        /// </summary>
        private static void MarkOwningSceneDirty(GameObject targetGo)
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                EditorSceneManager.MarkSceneDirty(prefabStage.scene);
            }
            else
            {
                EditorSceneManager.MarkSceneDirty(targetGo.scene);
            }
        }

        private static GameObject FindTarget(JToken targetToken, string searchMethod)
        {
            if (targetToken == null)
                return null;

            // Try instance ID first
            if (targetToken.Type == JTokenType.Integer)
            {
                int instanceId = targetToken.Value<int>();
                return GameObjectLookup.FindById(instanceId);
            }

            string targetStr = targetToken.ToString();

            // Try parsing as instance ID
            if (int.TryParse(targetStr, out int parsedId))
            {
                var byId = GameObjectLookup.FindById(parsedId);
                if (byId != null)
                    return byId;
            }

            // Use GameObjectLookup for search
            return GameObjectLookup.FindByTarget(targetToken, searchMethod ?? "by_name", true);
        }

        private static void SetPropertiesOnComponent(Component component, JObject properties)
        {
            if (component == null || properties == null)
                return;

            var errors = new List<string>();
            foreach (var prop in properties.Properties())
            {
                var error = TrySetProperty(component, prop.Name, prop.Value);
                if (error != null)
                    errors.Add(error);
            }
            
            if (errors.Count > 0)
            {
                McpLog.Warn($"[ManageComponents] Some properties failed to set on {component.GetType().Name}: {string.Join(", ", errors)}");
            }
        }

        /// <summary>
        /// Attempts to set a property or field on a component.
        /// Delegates to ComponentOps.SetProperty for unified implementation.
        /// </summary>
        private static string TrySetProperty(Component component, string propertyName, JToken value)
        {
            if (component == null || string.IsNullOrEmpty(propertyName))
                return "Invalid component or property name";

            if (ComponentOps.SetProperty(component, propertyName, value, out string error))
            {
                return null; // Success
            }

            McpLog.Warn($"[ManageComponents] {error}");
            return error;
        }

        private static bool ApplyProperties(Component component, JObject properties, out List<string> errors)
        {
            errors = new List<string>();
            Undo.RecordObject(component, $"Configure {component.GetType().Name}");

            foreach (var prop in properties.Properties())
            {
                string error = TrySetProperty(component, prop.Name, prop.Value);
                if (error != null)
                {
                    errors.Add(error);
                }
            }

            return errors.Count == 0;
        }

        private static JObject MergeProperties(JObject @params, params string[] passthroughKeys)
        {
            var merged = @params["properties"] as JObject != null
                ? new JObject((JObject)@params["properties"])
                : new JObject();

            foreach (string key in passthroughKeys)
            {
                if (@params[key] != null)
                {
                    merged[key] = @params[key];
                }
            }

            return merged;
        }

        private static object SuccessWithLayoutSummary(GameObject targetGo, string message)
        {
            return new
            {
                success = true,
                message,
                data = new
                {
                    instanceID = targetGo.GetInstanceID(),
                    layout = BuildLayoutSummary(targetGo)
                }
            };
        }

        private static object BuildLayoutSummary(GameObject targetGo)
        {
            RectTransform rectTransform = targetGo.GetComponent<RectTransform>();
            LayoutElement layoutElement = targetGo.GetComponent<LayoutElement>();
            ContentSizeFitter contentSizeFitter = targetGo.GetComponent<ContentSizeFitter>();
            CanvasScaler canvasScaler = targetGo.GetComponent<CanvasScaler>();

            return new
            {
                rectTransform = rectTransform == null ? null : new
                {
                    anchorMin = new { x = rectTransform.anchorMin.x, y = rectTransform.anchorMin.y },
                    anchorMax = new { x = rectTransform.anchorMax.x, y = rectTransform.anchorMax.y },
                    pivot = new { x = rectTransform.pivot.x, y = rectTransform.pivot.y },
                    sizeDelta = new { x = rectTransform.sizeDelta.x, y = rectTransform.sizeDelta.y }
                },
                preferredSizes = layoutElement == null ? null : new
                {
                    minWidth = layoutElement.minWidth,
                    minHeight = layoutElement.minHeight,
                    preferredWidth = layoutElement.preferredWidth,
                    preferredHeight = layoutElement.preferredHeight,
                    flexibleWidth = layoutElement.flexibleWidth,
                    flexibleHeight = layoutElement.flexibleHeight,
                    layoutPriority = layoutElement.layoutPriority
                },
                contentSizeFitter = contentSizeFitter == null ? null : new
                {
                    horizontalFit = contentSizeFitter.horizontalFit.ToString(),
                    verticalFit = contentSizeFitter.verticalFit.ToString()
                },
                canvasScaler = canvasScaler == null ? null : new
                {
                    uiScaleMode = canvasScaler.uiScaleMode.ToString(),
                    referenceResolution = new { x = canvasScaler.referenceResolution.x, y = canvasScaler.referenceResolution.y },
                    screenMatchMode = canvasScaler.screenMatchMode.ToString(),
                    matchWidthOrHeight = canvasScaler.matchWidthOrHeight
                }
            };
        }

        private static Vector2? ParseVector2Token(JToken token)
        {
            if (token == null)
                return null;

            if (token is JArray array && array.Count >= 2)
            {
                return new Vector2(array[0].Value<float>(), array[1].Value<float>());
            }

            if (token is JObject obj && obj["x"] != null && obj["y"] != null)
            {
                return new Vector2(obj["x"].Value<float>(), obj["y"].Value<float>());
            }

            return null;
        }

        private readonly struct AnchorPreset
        {
            public AnchorPreset(Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
            {
                AnchorMin = anchorMin;
                AnchorMax = anchorMax;
                Pivot = pivot;
            }

            public Vector2 AnchorMin { get; }
            public Vector2 AnchorMax { get; }
            public Vector2 Pivot { get; }
        }

        private static readonly IReadOnlyDictionary<string, AnchorPreset> AnchorPresets =
            new Dictionary<string, AnchorPreset>(StringComparer.OrdinalIgnoreCase)
            {
                ["TopLeft"] = new AnchorPreset(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f)),
                ["TopCenter"] = new AnchorPreset(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f)),
                ["TopRight"] = new AnchorPreset(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f)),
                ["MiddleLeft"] = new AnchorPreset(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f)),
                ["MiddleCenter"] = new AnchorPreset(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)),
                ["MiddleRight"] = new AnchorPreset(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f)),
                ["BottomLeft"] = new AnchorPreset(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f)),
                ["BottomCenter"] = new AnchorPreset(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f)),
                ["BottomRight"] = new AnchorPreset(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f)),
                ["TopStretch"] = new AnchorPreset(new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f)),
                ["MiddleStretch"] = new AnchorPreset(new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f)),
                ["BottomStretch"] = new AnchorPreset(new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f)),
                ["LeftStretch"] = new AnchorPreset(new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f)),
                ["CenterStretch"] = new AnchorPreset(new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f)),
                ["RightStretch"] = new AnchorPreset(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f)),
                ["StretchAll"] = new AnchorPreset(new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f))
            };

        private static bool TryGetAnchorPreset(string presetName, out AnchorPreset preset)
        {
            return AnchorPresets.TryGetValue(presetName, out preset);
        }

        #endregion
    }
}
