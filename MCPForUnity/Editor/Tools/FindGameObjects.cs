using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Tool for searching GameObjects in the scene.
    /// Returns only instance IDs with pagination support.
    ///
    /// This is a focused search tool that returns lightweight results (IDs only).
    /// For detailed GameObject data, use the unity://scene/gameobject/{id} resource.
    /// </summary>
    [McpForUnityTool("find_gameobjects")]
    public static class FindGameObjects
    {
        /// <summary>
        /// Handles the find_gameobjects command.
        /// </summary>
        /// <param name="params">Command parameters</param>
        /// <returns>Paginated list of instance IDs</returns>
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            var p = new ToolParams(@params);

            string searchMethod = p.Get("searchMethod", "by_name");
            string searchTerm = p.Get("searchTerm") ?? p.Get("target") ?? string.Empty;
            string hierarchyPath = p.Get("path");
            string relationship = p.Get("relationship", string.Empty).ToLowerInvariant();

            var parentId = p.GetInt("parentId");
            var hasParentId = parentId.HasValue;
            var includeDescendants = p.GetBool("includeDescendants", true);
            var exactName = p.GetBool("exactName", true);

            // Pagination parameters using standard PaginationRequest
            var pagination = PaginationRequest.FromParams(@params, defaultPageSize: 50);
            pagination.PageSize = Mathf.Clamp(pagination.PageSize, 1, 500);

            // Search options (supports multiple parameter name variants)
            bool includeInactive = p.GetBool("includeInactive", false) ||
                                   p.GetBool("searchInactive", false);

            // Relationship traversal operations can be term-less.
            if (string.IsNullOrEmpty(searchTerm) && string.IsNullOrEmpty(hierarchyPath) && string.IsNullOrEmpty(relationship))
            {
                return new ErrorResponse("'searchTerm', 'path', or 'relationship' parameter is required.");
            }

            try
            {
                List<int> allIds;

                switch (relationship)
                {
                    case "children":
                        if (!hasParentId)
                        {
                            return new ErrorResponse("'parentId' is required when relationship is 'children'.");
                        }

                        allIds = GameObjectLookup.SearchWithinParent(
                            GameObjectLookup.ParseSearchMethod(searchMethod),
                            searchTerm,
                            parentId.Value,
                            includeDescendants,
                            includeInactive,
                            exactName,
                            0);
                        break;

                    case "siblings":
                        if (!hasParentId)
                        {
                            return new ErrorResponse("'parentId' is required when relationship is 'siblings'.");
                        }

                        allIds = GameObjectLookup.GetSiblings(parentId.Value, includeInactive);
                        break;

                    case "next_sibling":
                        if (!hasParentId)
                        {
                            return new ErrorResponse("'parentId' is required when relationship is 'next_sibling'.");
                        }

                        var next = GameObjectLookup.GetAdjacentSibling(parentId.Value, 1, includeInactive);
                        allIds = next.HasValue ? new List<int> { next.Value } : new List<int>();
                        break;

                    case "prev_sibling":
                        if (!hasParentId)
                        {
                            return new ErrorResponse("'parentId' is required when relationship is 'prev_sibling'.");
                        }

                        var prev = GameObjectLookup.GetAdjacentSibling(parentId.Value, -1, includeInactive);
                        allIds = prev.HasValue ? new List<int> { prev.Value } : new List<int>();
                        break;

                    default:
                        if (!string.IsNullOrEmpty(hierarchyPath))
                        {
                            allIds = GameObjectLookup.FindByHierarchyPath(hierarchyPath, parentId, includeInactive);
                        }
                        else if (hasParentId)
                        {
                            allIds = GameObjectLookup.SearchWithinParent(
                                GameObjectLookup.ParseSearchMethod(searchMethod),
                                searchTerm,
                                parentId.Value,
                                includeDescendants,
                                includeInactive,
                                exactName,
                                0);
                        }
                        else
                        {
                            allIds = GameObjectLookup.SearchGameObjects(searchMethod, searchTerm, includeInactive, 0);
                        }
                        break;
                }

                // Use standard pagination response
                var paginatedResult = PaginationResponse<int>.Create(allIds, pagination);

                return new SuccessResponse("Found GameObjects", new
                {
                    instanceIDs = paginatedResult.Items,
                    pageSize = paginatedResult.PageSize,
                    cursor = paginatedResult.Cursor,
                    nextCursor = paginatedResult.NextCursor,
                    totalCount = paginatedResult.TotalCount,
                    hasMore = paginatedResult.HasMore
                });
            }
            catch (System.Exception ex)
            {
                McpLog.Error($"[FindGameObjects] Error searching GameObjects: {ex.Message}");
                return new ErrorResponse($"Error searching GameObjects: {ex.Message}");
            }
        }
    }
}
