# Unity Editor Tools Repository

This repository stores reusable Unity editor tooling exported from the Dragon UI workspace.

## Structure

- Preserve each script's original Unity-relative path under `Assets/`.
- Keep Unity `.meta` files beside their corresponding assets.
- Put UI editor-only scripts under `Assets/Content/UI/Editor`.
- Keep durable UI workspace conventions in `Assets/Content/UI/AGENTS.md`.

## Change Rules

- Do not add unrelated game assets, prefabs, credentials, generated files, or local editor state.
- Preserve existing user changes and keep commits scoped to one tool or behavior.
- Validate Unity compilation before publishing editor-script changes when the project is available.
