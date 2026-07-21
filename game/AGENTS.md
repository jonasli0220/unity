# Dragon Python Tool Integrations

## Scope

- This subtree stores small project-relative Python helpers required by reusable Unity editor tools in this repository.
- Preserve each helper's path relative to the Dragon Unity project so it can be synchronized without guessing import locations.

## Structure

- `client/cli_utils/` contains editor-invoked client helpers importable from Dragon's embedded Python runtime.
- Keep user-facing documentation with the matching Unity tool under `Assets/Content/UI/Editor`.

## Safety

- Do not publish generated data, gameplay content, credentials, logs, caches, or unrelated client code.
- Keep commits scoped to the helper files required by the corresponding editor tool.
