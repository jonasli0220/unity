# Effect Preview Agent Context

## Scope

This directory contains editor-only controls for previewing UI effects without entering Play Mode.

## Placement

- Keep effect-preview editor code in this directory.
- Keep user-facing usage notes in `README.md`.
- Do not add runtime gameplay code here.

## Behavior Rules

- Preview is available only outside Play Mode and must never enter Play Mode automatically.
- The primary entry is a compact control in the existing `Prefab导航` Scene View overlay; keep a menu fallback under `Tools/UI/Effect Preview`.
- In Prefab Stage, preview the selected hierarchy subtree when it contains particles; otherwise preview the whole opened prefab.
- In a regular scene, preview the selected hierarchy subtree only. Never silently simulate every particle in a gameplay scene.
- Preview particle state only. Do not modify serialized particle settings, create Undo records, or mark scenes/prefabs dirty.
- Stop and clear preview state before assembly reload, Play Mode changes, editor quit, Prefab Stage changes, or preview-target destruction.
- Keep unsupported runtime-script, shader-time, Timeline, Animator, and Visual Effect Graph behavior explicit instead of pretending the preview matches Play Mode.
