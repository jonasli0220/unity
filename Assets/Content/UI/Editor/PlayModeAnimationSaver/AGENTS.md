# Play Mode Animation Saver Rules

## Scope

- This directory contains the editor-only tool for persisting Animation Window curve edits made against Play Mode UI instances.
- Do not add runtime/player components or modify gameplay scene objects.

## Structure

- `PlayModeAnimationSaver.cs`: Animation Window context reading, source `.anim` resolution, curve copy, verification, Undo, and asset save.
- `README.md`: designer-facing workflow, supported cases, and limitations.

## Behavior Contract

- Operate only while Unity is in Play Mode and not transitioning between modes.
- Treat the Animation Window's active clip as the source snapshot; never infer values from the currently sampled GameObject state.
- If the active clip is already a writable standalone `.anim` asset, mark and save that exact asset without copying to another clip.
- For runtime-only clip instances, match only exact clip names and only writable standalone `.anim` assets.
- Prefer a unique candidate referenced by the current UI prefab or Animator Controller. If ambiguity remains, require an explicit target choice instead of guessing.
- Copy float curves, object-reference curves, Animation Events, clip settings, frame rate, and wrap mode. Remove target bindings that no longer exist in the runtime clip so the saved asset is an exact curve snapshot.
- Record the target asset with Unity Undo before mutation, verify all saved curves and object references by reading them back, and save only after verification succeeds.
- If verification fails, revert the Undo group and leave the source asset unchanged.
- Never save the active Scene or copy arbitrary runtime component values into a Prefab.

## Validation

- Verify Unity Editor compilation on Unity 2021.3.
- Test direct `.anim` saving, runtime-clone-to-source copying, duplicate-name target selection, object-reference curves, removed bindings, Undo, and failure rollback.
