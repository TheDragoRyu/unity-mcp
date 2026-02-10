"""
Tool for managing components on GameObjects in Unity.
Supports add/remove/set operations plus UI-focused layout helpers.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import normalize_properties
from services.tools.preflight import preflight


@mcp_for_unity_tool(
    description=(
        "Manages components on GameObjects (add, remove, set_property, set_enabled, "
        "set_anchor_preset, configure_layout_element, configure_content_size_fitter, "
        "configure_canvas_scaler). For reading component data, use the "
        "mcpforunity://scene/gameobject/{id}/components resource."
    )
)
async def manage_components(
    ctx: Context,
    action: Annotated[
        Literal[
            "add",
            "remove",
            "set_property",
            "set_enabled",
            "set_anchor_preset",
            "configure_layout_element",
            "configure_content_size_fitter",
            "configure_canvas_scaler",
        ],
        (
            "Action to perform: add/remove/set_property/set_enabled for generic component ops, "
            "plus set_anchor_preset/configure_layout_element/configure_content_size_fitter/"
            "configure_canvas_scaler for UI layout workflows"
        ),
    ],
    target: Annotated[
        str | int,
        "Target GameObject - instance ID (preferred) or name/path",
    ],
    component_type: Annotated[
        str,
        "Component type name (e.g., 'Rigidbody', 'BoxCollider', 'MyScript'). Required for add/remove/set_property/set_enabled.",
    ]
    | None = None,
    search_method: Annotated[
        Literal["by_id", "by_name", "by_path"],
        "How to find the target GameObject",
    ]
    | None = None,
    property: Annotated[str, "Property name to set (for set_property action)"] | None = None,
    value: Annotated[
        str | int | float | bool | dict | list,
        "Value to set (for set_property/set_enabled). For set_anchor_preset this can also be used for booleans if needed.",
    ]
    | None = None,
    properties: Annotated[
        dict[str, Any],
        "Dictionary of property names to values for add/set_property and UI config helpers.",
    ]
    | None = None,
    preset: Annotated[
        str,
        "Anchor preset name for set_anchor_preset (e.g., TopLeft, BottomStretch, StretchAll).",
    ]
    | None = None,
    auto_add_component: Annotated[
        bool,
        "For UI helper actions, auto-add missing required components when safe.",
    ]
    | None = None,
    size_delta: Annotated[
        dict[str, float] | list[float],
        "Optional RectTransform sizeDelta override for set_anchor_preset, e.g. {'x':240,'y':80} or [240,80].",
    ]
    | None = None,
) -> dict[str, Any]:
    """
    Manage components on GameObjects.

    Actions:
    - add/remove/set_property/set_enabled: Generic component lifecycle + property operations.
    - set_anchor_preset: Apply anchorMin/anchorMax/pivot presets on RectTransform.
    - configure_layout_element: Configure or auto-add LayoutElement with preferred/min/flexible sizes.
    - configure_content_size_fitter: Configure or auto-add ContentSizeFitter horizontal/vertical fit.
    - configure_canvas_scaler: Configure or auto-add CanvasScaler on Canvas roots.

    UI examples:
    - Dynamic chat bubble:
      1) set_anchor_preset, preset="BottomStretch", size_delta={"x": -32, "y": 80}
      2) configure_layout_element, properties={"preferredWidth": 640, "preferredHeight": 80, "flexibleHeight": 0}
      3) configure_content_size_fitter, properties={"horizontalFit": "Unconstrained", "verticalFit": "PreferredSize"}
    - Reward cell grid:
      1) set_anchor_preset, preset="TopLeft"
      2) configure_layout_element, properties={"preferredWidth": 160, "preferredHeight": 160, "flexibleWidth": 0, "flexibleHeight": 0}
      3) configure_content_size_fitter, properties={"horizontalFit": "PreferredSize", "verticalFit": "PreferredSize"}
    """
    unity_instance = get_unity_instance_from_context(ctx)

    gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
    if gate is not None:
        return gate.model_dump()

    if not action:
        return {
            "success": False,
            "message": (
                "Missing required parameter 'action'. Valid actions: add, remove, set_property, "
                "set_enabled, set_anchor_preset, configure_layout_element, "
                "configure_content_size_fitter, configure_canvas_scaler"
            ),
        }

    if not target:
        return {
            "success": False,
            "message": "Missing required parameter 'target'. Specify GameObject instance ID or name.",
        }

    requires_component_type = action in {"add", "remove", "set_property", "set_enabled"}
    if requires_component_type and not component_type:
        return {
            "success": False,
            "message": "Missing required parameter 'component_type' for this action.",
        }

    properties, props_error = normalize_properties(properties)
    if props_error:
        return {"success": False, "message": props_error}

    if value is not None and isinstance(value, str) and value in ("[object Object]", "undefined"):
        return {"success": False, "message": f"value received invalid input: '{value}'. Expected an actual value."}

    try:
        params: dict[str, Any] = {
            "action": action,
            "target": target,
        }

        if component_type:
            params["componentType"] = component_type

        if search_method:
            params["searchMethod"] = search_method

        if action == "set_property":
            if property and value is not None:
                params["property"] = property
                params["value"] = value
            if properties:
                params["properties"] = properties

        if action == "add" and properties:
            params["properties"] = properties

        if action == "set_enabled" and value is not None:
            params["value"] = value

        if action == "set_anchor_preset":
            if preset:
                params["preset"] = preset
            if size_delta is not None:
                params["sizeDelta"] = size_delta

        if action in {
            "configure_layout_element",
            "configure_content_size_fitter",
            "configure_canvas_scaler",
        }:
            if properties:
                params["properties"] = properties

        if auto_add_component is not None:
            params["autoAddComponent"] = auto_add_component

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "manage_components",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", f"Component {action} successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Error managing component: {e!s}"}
