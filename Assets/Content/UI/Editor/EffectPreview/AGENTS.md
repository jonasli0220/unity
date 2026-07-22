# Effect Preview Agent Context

## Scope

This directory contains editor-only controls for previewing UI dynamic effects without entering Play Mode.

## Placement

- Keep effect-preview editor code in this directory.
- Keep the tool implemented as one independent editor script, `UIEffectPreview.cs`, unless the file becomes genuinely too large to maintain.
- Keep user-facing usage notes in `README.md`.
- Do not add runtime gameplay code here.

## Behavior Rules

- Preview is available only outside Play Mode and must never enter Play Mode automatically.
- The primary entry is a compact control in the existing `Prefab导航` Scene View overlay; keep a menu fallback under `Tools/UI/Effect Preview`.
- Scene-view notifications should be short action feedback only, such as `动态预览开始` and `动态预览已停止`; keep detailed counts in Console logs.
- In Prefab Stage, preview the whole opened prefab's active hierarchy; do not bind preview scope to the current selection.
- In a regular scene, preview the selected hierarchy subtree only. Never silently simulate every particle in a gameplay scene.
- Default preview types are Unity `ParticleSystem`, Spine `SkeletonGraphic` / `SkeletonAnimation` / `SkeletonMecanim`, and project UI shader material dynamics driven by `UIShaderParamHelper_vx_common_shader`.
- Shader material preview may temporarily drive the project `_UnscaleTime` global using the render pipeline's `(t/20, t, t*2, t*3)` convention, and must restore the previous value when preview stops.
- `Animator` and legacy `Animation` preview are optional and must be gated by the checked menu `UITools/动态预览包含animator`, defaulting to off.
- For project `UIPanelAnimation`, prefer the configured enter animation and automatically continue to the configured loop animation; otherwise use each component's configured default animation.
- For optional `Animator` components without `UIPanelAnimation`, rebind and update the Animator so its Controller starts from Entry/default state and follows its own transitions.
- Optional Animator and legacy Animation preview should stay temporary; use Unity Animation Mode where it helps, and rely on the preview-start snapshot to restore visible UI state on stop.
- Stopping preview must restore the visible hierarchy to the state captured before preview started, not leave Animator-driven objects paused on their current frame.
- Spine preview must reset the runtime skeleton state on stop. Do not add `EditorSkeletonPlayer` or other persistent components to source objects.
- Do not modify serialized effect settings, create Undo records, or mark scenes/prefabs dirty.
- Stop and clear preview state before assembly reload, Play Mode changes, editor quit, Prefab Stage changes, or preview-target destruction.
- Keep unsupported custom runtime-script orchestration, unknown shader-global time, Timeline, audio, and Visual Effect Graph behavior explicit instead of pretending the preview matches Play Mode.
