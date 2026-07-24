# Project Folder History Rules

## Scope

- This directory contains the editor-only navigation history extension for Unity's native Project window.
- Keep the implementation self-contained here; do not add runtime components, scenes, prefabs, or project assets outside this tool directory.
- Keep user-facing behavior and setup notes in `README.md`.

## Native Project Window Integration

- Extend the existing `UnityEditor.ProjectBrowser`; do not replace Unity's Project window with a custom asset browser.
- Keep history isolated per Project window.
- Record only valid folder paths and consecutive path changes. Navigating through history must not create duplicate history entries.
- Opening a new folder after going back must discard the old forward branch, matching file-browser behavior.
- Treat Project search/filter state as transient. Do not record folder paths reported while a search is active.
- When a folder is opened from search results, record the final destination directly after the pre-search folder; suppress Unity's temporary `Assets` root during the search-exit transition.
- Skip deleted or otherwise invalid folders when resolving the next back/forward destination.
- Reflection is version-specific. If Unity internals cannot be resolved, fail safely, leave the native Project window usable, and emit at most one actionable warning per assembly reload.

## Interaction

- Put compact back and forward controls in unused space on the native Project toolbar without covering search, filters, or other built-in actions.
- Keep `Alt+Left Arrow` and `Alt+Right Arrow` shortcuts available while a Project window is focused.
- Disable unavailable directions and expose the target folder through the button tooltip.
- Preserve native folder selection, search, drag/drop, rename, and context-menu behavior.

## Persistence And Validation

- Preserve the current Editor-session history across script/domain reloads through `SessionState`; do not persist browsing history as a project asset or team setting.
- Keep the stored history bounded.
- Validate editor compilation against the project's Unity version and manually confirm two-column and one-column Project layouts when Unity is available.
