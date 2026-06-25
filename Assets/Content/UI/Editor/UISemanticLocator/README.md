# UI Semantic Locator

Unity Editor tool for finding a UI prefab from a natural-language clue, route name, or entry-point hint.

Open it from:

`Tools/UI/Semantic UI Locator/Open`

Smoke-test the validation case from:

`Tools/UI/Semantic UI Locator/Smoke Test - Hero Journey`

`Tools/UI/Semantic UI Locator/Smoke Test - Hard Training`

## What It Indexes

- `UIPanelViewMappingData.py`: UI route id to prefab path.
- `ActData.py`: activity display names such as `千锤百炼 I` to their `show_tab` route.
- UI client Python code under `game/client/ui`: calls such as `game_mgr.ui_mgr.AbbrCreate("...")`.
- Python enum comments such as `HERO_JOURNEY = 4 # 英雄之旅`, used as semantic aliases.
- Prefabs under `Assets/Content/UI/Prefab`, used as a fallback filename/path index.

Generic UI words such as `tips`, `rule`, `window`, `panel`, `common`, and `item` are downweighted when they come from an inferred alias instead of the user's direct query.

The cache is written to `Library/Dragon/UISemanticLocator/index.json`.

## Result Preview

Search results show a prefab thumbnail beside each route. Double-click a thumbnail to open that prefab. The window reuses the UI prefab preview generator when it is available in the project, and falls back to Unity's built-in `AssetPreview` otherwise.

Preview textures are editor-only and in-memory. They are not written under `Assets/` and do not change the semantic index cache.

## Expected Smoke Test

Search:

`英雄之旅`

Expected high-confidence result:

- Route: `season_all_common.event_subscribe`
- Prefab: `Assets/Content/UI/Prefab/season_all/a_event_subscribe.prefab`
- Evidence should mention `OnTrgHeroJourney` and `UIPanelViewMappingData.py`.

Search:

`千锤百炼`

Expected high-confidence result:

- Route: `activity.week_score`
- Prefab: `Assets/Content/UI/Prefab/event/a_event_week_score.prefab`
- Related popup route: `common.multi_reward_window`
- Related popup prefab: `Assets/Content/UI/Prefab/common_window/multi_reward_window.prefab`
