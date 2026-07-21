# Play Mode UI Refresh Rules

## Scope

- This directory contains the editor-only Game-view refresh button for Dragon runtime UI previews.
- Keep the Unity editor integration here; keep the matching Python runtime bridge at `game/client/cli_utils/ui_editor_refresh.py`.
- Do not add runtime components to scenes or prefabs.

## Behavior

- Show the button only while the Editor is in Play Mode.
- Keep the Python bridge safe to import during early XPython startup: top-level imports must not load `game_manager`, `sgr_data`, `xgui`, or other game modules.
- Treat an already-loaded `game_manager.game_mgr.ui_mgr` as the runtime-ready signal. Before that signal, return `False` without importing game modules and let the Editor retry later.
- Refresh the foreground UI through `game_mgr.ui_mgr`; never replace runtime hierarchy objects directly from C#.
- Preserve the original UI creation arguments captured during the current Play Mode session.
- Release the current view and its cached prefab provider before reopening so saved prefab changes are reloaded from `AssetDatabase` simulation.
- Refuse safely, with an actionable Game-view message, when Python is unavailable, the UI lacks a safe recreation context, or asset-bundle simulation is disabled.

## Placement And Feedback

- Place one compact `刷新界面` button in the Game-view toolbar gap before Unity's right-side controls.
- Style the enabled state like an active Unity toolbar action: dark filled background, high-contrast bold light text, subtle rounding, and no disabled-looking opacity. Reserve dim text and background for genuinely unavailable states.
- Keep the UI designer-facing: one click, no configuration window, concise success/failure feedback in the Game view.

## Validation

- Verify C# editor compilation and Python syntax after changes.
- In Play Mode with asset simulation enabled, open a UI after the helper installs, save its prefab, click `刷新界面`, and confirm the view closes and reopens from the updated prefab.
- Confirm Edit Mode and normal Game-view input remain unaffected.
