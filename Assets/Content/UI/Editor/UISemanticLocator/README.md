# UI Semantic Locator

Unity Editor tool for finding a UI prefab from a natural-language clue, route name, or entry-point hint.

Open it from:

`Tools/UI/Semantic UI Locator`

The search field restores the last successful query. Press `Enter` in the search field to search again.

## 工作原理

这个工具会把项目里可确定的 UI 入口信息整理成本地索引：包括 route 到 prefab 的映射、活动配置里的展示名和入口、客户端代码里的 UI 创建调用、枚举注释里的语义别名，以及 UI prefab 文件名。搜索时，它会把你输入的活动名、界面名或入口描述转成可匹配的关键词，并按“明确映射优先、代码入口其次、语义别名辅助、文件名兜底”的方式排序，同时展示匹配证据和 prefab 预览图。

索引只写入 `Library/Dragon/UISemanticLocator`，不会修改 prefab、配置表或客户端代码。

## 应用场景

- 只知道活动或界面的中文名，想快速找到对应 route 和 prefab。
- 从 Figma、飞书说明、需求口语描述反查 Unity 工程里的具体界面资源。
- 接手陌生 UI 或排查弹窗来源时，先通过证据和预览图判断候选 prefab。
- 搜索活动名时，区分主界面、共用弹窗、tips、奖励弹窗等相关但不同用途的结果。

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
