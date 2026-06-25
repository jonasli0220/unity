# UI Semantic Locator

Unity Editor tool for finding a UI prefab from a natural-language clue, route name, or entry-point hint.

Open it from:

`Tools/UI/Semantic UI Locator/Open`

Smoke-test the validation case from:

`Tools/UI/Semantic UI Locator/Smoke Test - Hero Journey`

## What It Indexes

- `UIPanelViewMappingData.py`: UI route id to prefab path.
- UI client Python code under `game/client/ui`: calls such as `game_mgr.ui_mgr.AbbrCreate("...")`.
- Python enum comments such as `HERO_JOURNEY = 4 # 英雄之旅`, used as semantic aliases.
- Prefabs under `Assets/Content/UI/Prefab`, used as a fallback filename/path index.

The cache is written to `Library/Dragon/UISemanticLocator/index.json`.

## Expected Smoke Test

Search:

`英雄之旅`

Expected high-confidence result:

- Route: `season_all_common.event_subscribe`
- Prefab: `Assets/Content/UI/Prefab/season_all/a_event_subscribe.prefab`
- Evidence should mention `OnTrgHeroJourney` and `UIPanelViewMappingData.py`.
