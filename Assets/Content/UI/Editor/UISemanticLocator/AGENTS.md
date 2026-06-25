# UI Semantic Locator Agent Context

## Scope

This directory contains the Unity Editor tool that maps semantic UI descriptions to UI route names and prefab assets.

## Placement

- Keep all semantic locator editor code in this directory.
- Keep usage and verification notes in `README.md`.
- Do not place generated indexes, logs, screenshots, or search output under `Assets/`.

## Cache Rules

- Search indexes are local editor cache only.
- Cache output must live under the Unity project `Library/Dragon/UISemanticLocator`.
- Cache rebuilds may read generated Python data and client UI Python code, but must not modify them.

## Search Rules

- UI prefab results must point to assets under `Assets/Content/UI/Prefab`.
- Prefer deterministic project data sources such as `UIPanelViewMappingData.py` and explicit UI creation calls before heuristic filename matches.
- Show evidence for why a semantic description matched a prefab whenever possible.

## Validation

- After changing the tool, verify that searching `英雄之旅` can surface `season_all_common.event_subscribe` and `Assets/Content/UI/Prefab/season_all/a_event_subscribe.prefab`.
- After changing activity-name indexing, verify that searching `千锤百炼` can surface `activity.week_score`, `Assets/Content/UI/Prefab/event/a_event_week_score.prefab`, and related popup `common.multi_reward_window`.
