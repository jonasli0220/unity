# Effect Preview Agent Context

## Scope

This directory contains editor-only controls for previewing UI dynamic effects without entering Play Mode.

## Placement

- Keep effect-preview editor code in this directory.
- Keep user-facing usage notes in `README.md`.
- Do not add runtime gameplay code here.

## Behavior Rules

- Preview is available only outside Play Mode and must never enter Play Mode automatically.
- The primary entry is a compact control in the existing `Prefab导航` Scene View overlay; keep a menu fallback under `Tools/UI/Effect Preview`.
- In Prefab Stage, preview the selected hierarchy subtree when it contains supported dynamic effects; otherwise preview the whole opened prefab.
- In Prefab Stage, include all active and enabled `Animator` components under the currently opened prefab when preview starts, so controller Entry states can be triggered together even when they are spread across many active nodes.
- In a regular scene, preview the selected hierarchy subtree only. Never silently simulate every particle in a gameplay scene.
- Supported preview types are Unity `ParticleSystem`, Spine `SkeletonGraphic` / `SkeletonAnimation` / `SkeletonMecanim`, `Animator`, and legacy `Animation`.
- For project `UIPanelAnimation`, prefer the configured enter animation and automatically continue to the configured loop animation; otherwise use each component's configured default animation.
- For `Animator` components without `UIPanelAnimation`, prefer the first layer's Animator Controller Entry/default state; if that state transitions to another state, prefer a loop-looking destination state before falling back to the first transition.
- Animator and legacy Animation preview must use Unity Animation Mode so stopping restores sampled transforms and properties.
- Spine preview must reset the runtime skeleton state on stop. Do not add `EditorSkeletonPlayer` or other persistent components to source objects.
- Do not modify serialized effect settings, create Undo records, or mark scenes/prefabs dirty.
- Stop and clear preview state before assembly reload, Play Mode changes, editor quit, Prefab Stage changes, or preview-target destruction.
- Keep unsupported custom runtime-script orchestration, shader-global time, Timeline, audio, and Visual Effect Graph behavior explicit instead of pretending the preview matches Play Mode.
