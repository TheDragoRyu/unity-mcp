"""
Tool for searching GameObjects in Unity scenes.
Returns only instance IDs with pagination support for efficient searches.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from pydantic import Field
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import coerce_bool, coerce_int
from services.tools.preflight import preflight


@mcp_for_unity_tool(
    description=(
        "Search for GameObjects in the scene. Supports global search, parent-scoped search, strict hierarchy path lookup, "
        "and sibling traversal. Returns instance IDs only (paginated). "
        "Use mcpforunity://scene/gameobject/{id} resource to get full GameObject data."
    )
)
async def find_gameobjects(
    ctx: Context,
    search_term: Annotated[
        str | None,
        Field(description="The value to search for (name, tag, layer name, component type, or path)")
    ] = None,
    search_method: Annotated[
        Literal["by_name", "by_tag", "by_layer", "by_component", "by_path", "by_id"],
        Field(
            default="by_name",
            description="How to search for GameObjects"
        )
    ] = "by_name",
    include_inactive: Annotated[
        bool | str | None,
        Field(
            default=None,
            description="Include inactive GameObjects in search"
        )
    ] = None,
    parent_id: Annotated[
        int | str | None,
        Field(
            default=None,
            description="Optional parent/anchor instance ID for scoped search or sibling traversal"
        )
    ] = None,
    include_descendants: Annotated[
        bool | str | None,
        Field(
            default=None,
            description="When parent_id is set: include descendants (true) or only direct children (false)"
        )
    ] = None,
    exact_name: Annotated[
        bool | str | None,
        Field(
            default=None,
            description="For by_name searches: require exact name match when true, substring match when false"
        )
    ] = None,
    path: Annotated[
        str | None,
        Field(
            default=None,
            description="Strict hierarchy path from scene root or from parent_id"
        )
    ] = None,
    relationship: Annotated[
        Literal["children", "siblings", "next_sibling", "prev_sibling"] | None,
        Field(
            default=None,
            description="Hierarchy relationship traversal mode"
        )
    ] = None,
    page_size: Annotated[
        int | str | None,
        Field(
            default=None,
            description="Number of results per page (default: 50, max: 500)"
        )
    ] = None,
    cursor: Annotated[
        int | str | None,
        Field(
            default=None,
            description="Pagination cursor (offset for next page)"
        )
    ] = None,
) -> dict[str, Any]:
    """
    Search for GameObjects and return their instance IDs.

    Usage examples:
    - Find child by name under a parent:
      find_gameobjects(search_term="StartButton", parent_id=1001, relationship="children", include_descendants=False)
    - By-path selection relative to a parent:
      find_gameobjects(path="UI/ObjectivesPanel/Title", parent_id=1001)
    - All Image components under ObjectivesPanel:
      find_gameobjects(search_term="Image", search_method="by_component", parent_id=2002, relationship="children", include_descendants=True)

    For detailed GameObject information, use the returned IDs with:
    - mcpforunity://scene/gameobject/{id} - Get full GameObject data
    - mcpforunity://scene/gameobject/{id}/components - Get all components
    - mcpforunity://scene/gameobject/{id}/component/{name} - Get specific component
    """
    unity_instance = get_unity_instance_from_context(ctx)

    # Validate required parameters before preflight I/O
    if not search_term and not path and not relationship:
        return {
            "success": False,
            "message": "Missing one of required parameters: 'search_term', 'path', or 'relationship'."
        }

    gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
    if gate is not None:
        return gate.model_dump()

    # Coerce parameters
    include_inactive = coerce_bool(include_inactive, default=False)
    include_descendants = coerce_bool(include_descendants, default=True)
    exact_name = coerce_bool(exact_name, default=True)
    page_size = coerce_int(page_size, default=50)
    cursor = coerce_int(cursor, default=0)
    parent_id = coerce_int(parent_id, default=None)

    try:
        params = {
            "searchMethod": search_method,
            "searchTerm": search_term,
            "includeInactive": include_inactive,
            "parentId": parent_id,
            "includeDescendants": include_descendants,
            "exactName": exact_name,
            "path": path,
            "relationship": relationship,
            "pageSize": page_size,
            "cursor": cursor,
        }
        params = {k: v for k, v in params.items() if v is not None}

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "find_gameobjects",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Search completed."),
                "data": response.get("data")
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Error searching GameObjects: {e!s}"}
