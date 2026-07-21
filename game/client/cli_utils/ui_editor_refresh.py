# -*- coding: utf-8 -*-
"""Editor-only helper for rebuilding the foreground Dragon UI in Play Mode."""

import inspect
import sys


_CONTEXT_ATTR = "_dragon_editor_refresh_create_context"
_WRAPPER_ATTR = "_dragon_editor_refresh_wrapper"
_original_create = None
XSolution = None
ui_enum = None
game_mgr = None
ui_cache_mgr = None


def _bind_runtime_if_ready():
    """Bind game modules only after the normal startup path created UIManager."""
    global XSolution, ui_enum, game_mgr, ui_cache_mgr

    if game_mgr is not None and getattr(game_mgr, "ui_mgr", None) is not None:
        return True

    game_manager = sys.modules.get("game_manager")
    candidate_game_mgr = getattr(game_manager, "game_mgr", None)
    if candidate_game_mgr is None:
        return False
    if getattr(candidate_game_mgr, "ui_mgr", None) is None:
        return False

    # Importing game_manager before sgr_init.InitTimerMgr completes initializes
    # sgr_data with an empty clock and leaves several modules partially loaded.
    # Once UIManager exists, the regular sgr_main startup path has already passed
    # that unsafe phase and these supporting imports are safe.
    import XSolution as runtime_xsolution
    import xgui.ui_enum as runtime_ui_enum
    from xgui.ui_cache_mgr import ui_cache_mgr as runtime_ui_cache_mgr

    XSolution = runtime_xsolution
    ui_enum = runtime_ui_enum
    game_mgr = candidate_game_mgr
    ui_cache_mgr = runtime_ui_cache_mgr
    return True


def Install():
    """Capture subsequent UIManager.Create calls without changing gameplay APIs."""
    global _original_create

    if not _bind_runtime_if_ready():
        return False

    manager = getattr(game_mgr, "ui_mgr", None)
    if manager is None:
        return False

    manager_type = type(manager)
    current_create = getattr(manager_type, "Create", None)
    if current_create is None:
        return False

    if getattr(current_create, _WRAPPER_ATTR, False):
        _mark_existing_views(manager)
        return True

    _original_create = current_create

    def create_with_refresh_context(
            self, name, view_name=None, guid=None, callback=None,
            *args, **kwargs):
        control = _original_create(
            self, name, view_name, guid, callback, *args, **kwargs)
        _remember_create_context(
            self, name, view_name, guid, callback, args, kwargs, control)
        return control

    setattr(create_with_refresh_context, _WRAPPER_ATTR, True)
    manager_type.Create = create_with_refresh_context
    _mark_existing_views(manager)
    return True


def RefreshTopView():
    """Close, unload, and recreate the visible foreground UI from its saved prefab."""
    if not Install():
        return "运行时 UI 管理器尚未就绪"

    if not XSolution.AssetBundles.AssetSystem.SimulationOnEditor:
        return "当前不是编辑器资源模拟，无法读取刚保存的 Prefab"

    manager = game_mgr.ui_mgr
    view = _find_refresh_target(manager)
    if view is None:
        return "没有找到可刷新的前景界面"

    route_name = getattr(view, "_module_path", None)
    guid = getattr(view, "_guid", None)
    template = getattr(view, "TEMPLATE", None)
    if not route_name or not template:
        return "当前界面还在加载，请稍后再试"

    if getattr(view, "DontDestroyOnLoadAB", False):
        return "当前界面使用常驻资源，为避免影响游戏未执行刷新"

    prefab_op = getattr(view, "_prefab_op", None)
    if prefab_op is None:
        return "当前界面的 Prefab 资源句柄不可用"

    try:
        if prefab_op.GetRefCount() > 1:
            return "该 Prefab 正被多个界面使用，关闭其他实例后再刷新"
    except Exception:
        # Older handles may not expose GetRefCount. The provider check after Close
        # still prevents reopening from a provider that could not be discarded.
        pass

    context = getattr(view, _CONTEXT_ATTR, None)
    if not context or not context.get("captured", False):
        if _requires_create_arguments(manager, view, route_name, guid):
            return "此界面早于刷新工具打开，请先手动关闭并重开一次"
        context = _make_context(
            route_name, None, guid, None, (), {}, captured=False)

    view_name = context.get("view_name")
    create_guid = context.get("guid", guid)
    callback = context.get("callback")
    args = context.get("args", ())
    kwargs = dict(context.get("kwargs", {}))
    kwargs.pop("is_create_cache", None)
    kwargs.pop("user_identity", None)

    control = manager.GetControl(route_name, guid)
    if control is None:
        return "当前界面的控制器不可用，未执行刷新"

    # A refresh must destroy the live object instead of returning it to the UI
    # cache, otherwise Create would immediately reuse the stale prefab instance.
    control.check_ui_recyclable = False
    ui_cache_mgr.DelUIPool(route_name)

    real_asset_path = XSolution.AssetBundles.AssetSystem.GetAssetRealPatah(
        template)
    manager.Close(route_name, guid)
    XSolution.AssetBundles.AssetSystem.DestroyProvider(real_asset_path)

    if XSolution.AssetBundles.AssetSystem.TryGetProvider(real_asset_path):
        # Restore the UI even when another hidden reference prevented unloading.
        manager.Create(
            route_name, view_name, create_guid, callback, *args, **kwargs)
        return "旧 Prefab 仍被资源缓存占用，已恢复界面但未刷新"

    control = manager.Create(
        route_name, view_name, create_guid, callback, *args, **kwargs)
    if control is None:
        return "界面重开失败，请查看 Console"

    return "正在重新加载：{}".format(route_name)


def _remember_create_context(
        manager, name, view_name, guid, callback, args, kwargs, control):
    actual_guid = getattr(control, "_guid", guid) if control else guid
    view = manager.GetView(name, actual_guid)
    if view is None and control is not None:
        view = manager._ui_views.get(getattr(control, "_name", None))
    if view is None:
        return

    setattr(view, _CONTEXT_ATTR, _make_context(
        name,
        view_name,
        getattr(view, "_guid", actual_guid),
        callback,
        args,
        kwargs,
        captured=True))


def _make_context(
        name, view_name, guid, callback, args, kwargs, captured):
    return {
        "name": name,
        "view_name": view_name,
        "guid": guid,
        "callback": callback,
        "args": tuple(args),
        "kwargs": dict(kwargs),
        "captured": captured,
    }


def _mark_existing_views(manager):
    for view in list(manager._ui_views.values()):
        if hasattr(view, _CONTEXT_ATTR):
            continue
        setattr(view, _CONTEXT_ATTR, _make_context(
            getattr(view, "_module_path", None),
            None,
            getattr(view, "_guid", None),
            None,
            (),
            {},
            captured=False))


def _find_refresh_target(manager):
    allowed_layers = {
        getattr(ui_enum.LayerLevel, "MAIN", None),
        getattr(ui_enum.LayerLevel, "ACTIVITY", None),
        getattr(ui_enum.LayerLevel, "TIPS", None),
        getattr(ui_enum.LayerLevel, "FILTER", None),
    }
    allowed_layers.discard(None)

    ignored_routes = {
        "click_feedback",
        "common_click_feedback",
        "global_empty",
        "trace",
        "loading",
        "common.common_loading",
    }

    candidates = []
    for view in list(manager._ui_views.values()):
        try:
            if not view or not view.IsValid() or not view.visible:
                continue
            if getattr(view, "is_in_cache", False):
                continue
            if getattr(view, "LAYERLEVEL", None) not in allowed_layers:
                continue
            if getattr(view, "_module_path", None) in ignored_routes:
                continue

            transform = getattr(view, "transform", None)
            sibling_index = transform.GetSiblingIndex() if transform else -1
            candidates.append((view.LAYERLEVEL, sibling_index, view))
        except Exception:
            continue

    if not candidates:
        return None

    candidates.sort(key=lambda item: (item[0], item[1]), reverse=True)
    return candidates[0][2]


def _requires_create_arguments(manager, view, route_name, guid):
    control = manager.GetControl(route_name, guid)
    return (_method_has_required_arguments(control, "Awake") or
            _method_has_required_arguments(view, "Awake"))


def _method_has_required_arguments(instance, method_name):
    if instance is None:
        return False

    method = getattr(type(instance), method_name, None)
    if method is None:
        return False

    try:
        parameters = list(inspect.signature(method).parameters.values())[1:]
    except (TypeError, ValueError):
        return True

    for parameter in parameters:
        if parameter.kind not in (
                inspect.Parameter.POSITIONAL_ONLY,
                inspect.Parameter.POSITIONAL_OR_KEYWORD,
                inspect.Parameter.KEYWORD_ONLY):
            continue
        if parameter.default is inspect.Parameter.empty:
            return True
    return False
