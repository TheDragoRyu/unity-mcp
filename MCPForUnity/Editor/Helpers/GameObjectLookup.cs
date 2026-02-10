using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Utility class for finding and looking up GameObjects in the scene.
    /// Provides search functionality by name, tag, layer, component, path, and instance ID.
    /// </summary>
    public static class GameObjectLookup
    {
        /// <summary>
        /// Supported search methods for finding GameObjects.
        /// </summary>
        public enum SearchMethod
        {
            ByName,
            ByTag,
            ByLayer,
            ByComponent,
            ByPath,
            ById
        }

        /// <summary>
        /// Parses a search method string into the enum value.
        /// </summary>
        public static SearchMethod ParseSearchMethod(string method)
        {
            if (string.IsNullOrEmpty(method))
                return SearchMethod.ByName;

            return method.ToLowerInvariant() switch
            {
                "by_name" => SearchMethod.ByName,
                "by_tag" => SearchMethod.ByTag,
                "by_layer" => SearchMethod.ByLayer,
                "by_component" => SearchMethod.ByComponent,
                "by_path" => SearchMethod.ByPath,
                "by_id" => SearchMethod.ById,
                _ => SearchMethod.ByName
            };
        }

        /// <summary>
        /// Finds a single GameObject based on the target and search method.
        /// </summary>
        /// <param name="target">The target identifier (name, ID, path, etc.)</param>
        /// <param name="searchMethod">The search method to use</param>
        /// <param name="includeInactive">Whether to include inactive objects</param>
        /// <returns>The found GameObject or null</returns>
        public static GameObject FindByTarget(JToken target, string searchMethod, bool includeInactive = false)
        {
            if (target == null)
                return null;

            var results = SearchGameObjects(searchMethod, target.ToString(), includeInactive, 1);
            return results.Count > 0 ? FindById(results[0]) : null;
        }

        /// <summary>
        /// Finds a GameObject by its instance ID.
        /// </summary>
        public static GameObject FindById(int instanceId)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return EditorUtility.InstanceIDToObject(instanceId) as GameObject;
#pragma warning restore CS0618
        }

        /// <summary>
        /// Searches for GameObjects and returns their instance IDs.
        /// </summary>
        /// <param name="searchMethod">The search method string (by_name, by_tag, etc.)</param>
        /// <param name="searchTerm">The term to search for</param>
        /// <param name="includeInactive">Whether to include inactive objects</param>
        /// <param name="maxResults">Maximum number of results to return (0 = unlimited)</param>
        /// <returns>List of instance IDs</returns>
        public static List<int> SearchGameObjects(string searchMethod, string searchTerm, bool includeInactive = false, int maxResults = 0)
        {
            var method = ParseSearchMethod(searchMethod);
            return SearchGameObjects(method, searchTerm, includeInactive, maxResults);
        }

        /// <summary>
        /// Searches for GameObjects and returns their instance IDs.
        /// </summary>
        /// <param name="method">The search method</param>
        /// <param name="searchTerm">The term to search for</param>
        /// <param name="includeInactive">Whether to include inactive objects</param>
        /// <param name="maxResults">Maximum number of results to return (0 = unlimited)</param>
        /// <returns>List of instance IDs</returns>
        public static List<int> SearchGameObjects(SearchMethod method, string searchTerm, bool includeInactive = false, int maxResults = 0)
        {
            var results = new List<int>();

            switch (method)
            {
                case SearchMethod.ById:
                    if (int.TryParse(searchTerm, out int instanceId))
                    {
#pragma warning disable CS0618 // Type or member is obsolete
                        var obj = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
#pragma warning restore CS0618
                        if (obj != null && (includeInactive || obj.activeInHierarchy))
                        {
                            results.Add(instanceId);
                        }
                    }
                    break;

                case SearchMethod.ByName:
                    results.AddRange(SearchByName(searchTerm, includeInactive, maxResults));
                    break;

                case SearchMethod.ByPath:
                    results.AddRange(SearchByPath(searchTerm, includeInactive));
                    break;

                case SearchMethod.ByTag:
                    results.AddRange(SearchByTag(searchTerm, includeInactive, maxResults));
                    break;

                case SearchMethod.ByLayer:
                    results.AddRange(SearchByLayer(searchTerm, includeInactive, maxResults));
                    break;

                case SearchMethod.ByComponent:
                    results.AddRange(SearchByComponent(searchTerm, includeInactive, maxResults));
                    break;
            }

            return results;
        }

        /// <summary>
        /// Searches for GameObjects scoped to a parent object.
        /// </summary>
        /// <param name="method">The search method.</param>
        /// <param name="searchTerm">The term to search for. Optional for relationship-only traversal.</param>
        /// <param name="parentId">Parent instance ID.</param>
        /// <param name="includeDescendants">Whether to search all descendants (true) or direct children only (false).</param>
        /// <param name="includeInactive">Whether to include inactive objects.</param>
        /// <param name="exactName">When searching by name, require exact match if true; otherwise substring match.</param>
        /// <param name="maxResults">Maximum number of results to return (0 = unlimited).</param>
        public static List<int> SearchWithinParent(
            SearchMethod method,
            string searchTerm,
            int parentId,
            bool includeDescendants,
            bool includeInactive,
            bool exactName,
            int maxResults = 0)
        {
            var parent = FindById(parentId);
            if (parent == null)
            {
                return new List<int>();
            }

            if (!includeInactive && !parent.activeInHierarchy)
            {
                return new List<int>();
            }

            IEnumerable<GameObject> scope = includeDescendants
                ? GetDescendantsInHierarchyOrder(parent, includeInactive)
                : GetChildrenInHierarchyOrder(parent, includeInactive);

            var normalizedSearchTerm = searchTerm ?? string.Empty;
            var matched = scope.Where(go => MatchesSearch(go, method, normalizedSearchTerm, exactName));

            if (maxResults > 0)
            {
                matched = matched.Take(maxResults);
            }

            return matched.Select(go => go.GetInstanceID()).ToList();
        }

        /// <summary>
        /// Resolves a strict hierarchy path from scene root, or from a parent when supplied.
        /// </summary>
        public static List<int> FindByHierarchyPath(string path, int? parentId = null, bool includeInactive = false)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new List<int>();
            }

            var segments = path
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            if (segments.Length == 0)
            {
                return new List<int>();
            }

            IEnumerable<GameObject> currentLevel;
            if (parentId.HasValue)
            {
                var parent = FindById(parentId.Value);
                if (parent == null || (!includeInactive && !parent.activeInHierarchy))
                {
                    return new List<int>();
                }

                currentLevel = GetChildrenInHierarchyOrder(parent, includeInactive);
            }
            else
            {
                currentLevel = GetRootObjectsInHierarchyOrder(includeInactive);
            }

            List<GameObject> matches = null;
            foreach (var segment in segments)
            {
                matches = currentLevel.Where(go => go.name == segment).ToList();
                if (matches.Count == 0)
                {
                    return new List<int>();
                }

                currentLevel = matches.SelectMany(go => GetChildrenInHierarchyOrder(go, includeInactive));
            }

            return matches == null
                ? new List<int>()
                : matches.Select(go => go.GetInstanceID()).ToList();
        }

        /// <summary>
        /// Gets all siblings for a GameObject in hierarchy order. Excludes the object itself.
        /// </summary>
        public static List<int> GetSiblings(int instanceId, bool includeInactive = false)
        {
            var go = FindById(instanceId);
            if (go == null)
            {
                return new List<int>();
            }

            var siblings = GetSiblingObjects(go, includeInactive)
                .Where(s => s.GetInstanceID() != instanceId)
                .Select(s => s.GetInstanceID())
                .ToList();

            return siblings;
        }

        /// <summary>
        /// Gets the adjacent sibling for a GameObject.
        /// </summary>
        /// <param name="instanceId">The object instance ID.</param>
        /// <param name="direction">Positive for next sibling, negative for previous sibling.</param>
        /// <param name="includeInactive">Whether to include inactive siblings.</param>
        /// <returns>Adjacent sibling instance ID, or null if none exists.</returns>
        public static int? GetAdjacentSibling(int instanceId, int direction, bool includeInactive = false)
        {
            var go = FindById(instanceId);
            if (go == null)
            {
                return null;
            }

            var siblings = GetSiblingObjects(go, includeInactive).ToList();
            var selfIndex = siblings.FindIndex(s => s.GetInstanceID() == instanceId);
            if (selfIndex < 0)
            {
                return null;
            }

            var targetIndex = selfIndex + (direction >= 0 ? 1 : -1);
            if (targetIndex < 0 || targetIndex >= siblings.Count)
            {
                return null;
            }

            return siblings[targetIndex].GetInstanceID();
        }

        private static IEnumerable<int> SearchByName(string name, bool includeInactive, int maxResults)
        {
            var allObjects = GetAllSceneObjects(includeInactive);
            var matching = allObjects.Where(go => go.name == name);

            if (maxResults > 0)
                matching = matching.Take(maxResults);

            return matching.Select(go => go.GetInstanceID());
        }

        private static IEnumerable<int> SearchByPath(string path, bool includeInactive)
        {
            var allObjects = GetAllSceneObjects(includeInactive);
            foreach (var go in allObjects)
            {
                if (MatchesPath(go, path))
                {
                    yield return go.GetInstanceID();
                }
            }
        }

        private static IEnumerable<int> SearchByTag(string tag, bool includeInactive, int maxResults)
        {
            try
            {
                var allObjects = GetAllSceneObjects(includeInactive);
                var taggedObjects = allObjects.Where(go => go.CompareTag(tag));

                if (maxResults > 0)
                    taggedObjects = taggedObjects.Take(maxResults);

                foreach (var go in taggedObjects)
                {
                    yield return go.GetInstanceID();
                }
            }
            catch (UnityException)
            {
                // Tag doesn't exist
                yield break;
            }
        }

        /// <summary>
        /// Evaluates whether a GameObject identified by instance ID matches a search query.
        /// </summary>
        public static bool MatchesSearchById(int instanceId, SearchMethod method, string searchTerm, bool exactName)
        {
            var go = FindById(instanceId);
            return MatchesSearch(go, method, searchTerm ?? string.Empty, exactName);
        }

        private static bool MatchesSearch(GameObject go, SearchMethod method, string searchTerm, bool exactName)
        {
            if (go == null)
                return false;

            if (string.IsNullOrEmpty(searchTerm))
                return method != SearchMethod.ById;

            switch (method)
            {
                case SearchMethod.ById:
                    return int.TryParse(searchTerm, out var instanceId) && go.GetInstanceID() == instanceId;

                case SearchMethod.ByName:
                    return exactName
                        ? string.Equals(go.name, searchTerm, StringComparison.Ordinal)
                        : go.name.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;

                case SearchMethod.ByTag:
                    try
                    {
                        return go.CompareTag(searchTerm);
                    }
                    catch (UnityException)
                    {
                        return false;
                    }

                case SearchMethod.ByLayer:
                    var layer = LayerMask.NameToLayer(searchTerm);
                    if (layer == -1 && (!int.TryParse(searchTerm, out layer) || layer < 0 || layer > 31))
                    {
                        return false;
                    }
                    return go.layer == layer;

                case SearchMethod.ByComponent:
                    var componentType = FindComponentType(searchTerm);
                    return componentType != null && go.GetComponent(componentType) != null;

                case SearchMethod.ByPath:
                    return MatchesPath(go, searchTerm);

                default:
                    return false;
            }
        }

        private static IEnumerable<GameObject> GetRootObjectsInHierarchyOrder(bool includeInactive)
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.prefabContentsRoot != null)
            {
                if (includeInactive || prefabStage.prefabContentsRoot.activeInHierarchy)
                {
                    yield return prefabStage.prefabContentsRoot;
                }
                yield break;
            }

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                yield break;

            foreach (var root in scene.GetRootGameObjects())
            {
                if (includeInactive || root.activeInHierarchy)
                {
                    yield return root;
                }
            }
        }

        private static IEnumerable<GameObject> GetChildrenInHierarchyOrder(GameObject parent, bool includeInactive)
        {
            if (parent == null)
                yield break;

            for (var i = 0; i < parent.transform.childCount; i++)
            {
                var child = parent.transform.GetChild(i).gameObject;
                if (includeInactive || child.activeInHierarchy)
                {
                    yield return child;
                }
            }
        }

        private static IEnumerable<GameObject> GetDescendantsInHierarchyOrder(GameObject parent, bool includeInactive)
        {
            foreach (var child in GetChildrenInHierarchyOrder(parent, includeInactive))
            {
                yield return child;

                foreach (var descendant in GetDescendantsInHierarchyOrder(child, includeInactive))
                {
                    yield return descendant;
                }
            }
        }

        private static IEnumerable<GameObject> GetSiblingObjects(GameObject go, bool includeInactive)
        {
            if (go == null)
                yield break;

            var parent = go.transform.parent;
            if (parent == null)
            {
                foreach (var root in GetRootObjectsInHierarchyOrder(includeInactive))
                {
                    yield return root;
                }
                yield break;
            }

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i).gameObject;
                if (includeInactive || child.activeInHierarchy)
                {
                    yield return child;
                }
            }
        }

        private static IEnumerable<int> SearchByLayer(string layerName, bool includeInactive, int maxResults)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer == -1)
            {
                // Try parsing as layer number
                if (!int.TryParse(layerName, out layer) || layer < 0 || layer > 31)
                {
                    yield break;
                }
            }

            var allObjects = GetAllSceneObjects(includeInactive);
            var matching = allObjects.Where(go => go.layer == layer);

            if (maxResults > 0)
                matching = matching.Take(maxResults);

            foreach (var go in matching)
            {
                yield return go.GetInstanceID();
            }
        }

        private static IEnumerable<int> SearchByComponent(string componentTypeName, bool includeInactive, int maxResults)
        {
            Type componentType = FindComponentType(componentTypeName);
            if (componentType == null)
            {
                McpLog.Warn($"[GameObjectLookup] Component type '{componentTypeName}' not found.");
                yield break;
            }

            var allObjects = GetAllSceneObjects(includeInactive);
            var count = 0;

            foreach (var go in allObjects)
            {
                if (go.GetComponent(componentType) != null)
                {
                    yield return go.GetInstanceID();
                    count++;

                    if (maxResults > 0 && count >= maxResults)
                        yield break;
                }
            }
        }

        /// <summary>
        /// Gets all GameObjects in the current scene.
        /// </summary>
        public static IEnumerable<GameObject> GetAllSceneObjects(bool includeInactive)
        {
            // Check Prefab Stage first
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.prefabContentsRoot != null)
            {
                // Use Prefab Stage's prefabContentsRoot
                foreach (var go in GetObjectAndDescendants(prefabStage.prefabContentsRoot, includeInactive))
                {
                    yield return go;
                }
                yield break;
            }

            // Normal scene mode
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                yield break;

            var rootObjects = scene.GetRootGameObjects();
            foreach (var root in rootObjects)
            {
                foreach (var go in GetObjectAndDescendants(root, includeInactive))
                {
                    yield return go;
                }
            }
        }

        private static IEnumerable<GameObject> GetObjectAndDescendants(GameObject obj, bool includeInactive)
        {
            if (!includeInactive && !obj.activeInHierarchy)
                yield break;

            yield return obj;

            foreach (Transform child in obj.transform)
            {
                foreach (var descendant in GetObjectAndDescendants(child.gameObject, includeInactive))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>
        /// Finds a component type by name, searching loaded assemblies.
        /// </summary>
        /// <remarks>
        /// Delegates to UnityTypeResolver.ResolveComponent() for unified type resolution.
        /// </remarks>
        public static Type FindComponentType(string typeName)
        {
            return UnityTypeResolver.ResolveComponent(typeName);
        }

        /// <summary>
        /// Checks whether a GameObject matches a path or trailing path segment.
        /// </summary>
        internal static bool MatchesPath(GameObject go, string path)
        {
            if (go == null || string.IsNullOrEmpty(path))
                return false;

            var goPath = GetGameObjectPath(go);
            return goPath == path || goPath.EndsWith("/" + path);
        }

        /// <summary>
        /// Gets the hierarchical path of a GameObject.
        /// </summary>
        public static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null)
                return string.Empty;

            var path = obj.name;
            var parent = obj.transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }
    }
}
