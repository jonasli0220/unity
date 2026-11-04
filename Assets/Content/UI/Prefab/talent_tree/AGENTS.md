# Generated Talent Tree Output Rules

- Keep one `<definition-name>.prefab` and `<definition-name>.json` pair per exported tree in this directory, with Unity `.meta` files alongside them.
- Both files are generated from the matching definition under `Editor/TalentTree/Definitions/`. Do not hand-edit them; regenerate after graph or style changes.
- Before replacing an existing prefab, verify its source definition GUID matches. Never overwrite an unrelated prefab or JSON file.
- Remove obsolete output pairs explicitly when their source definition is retired; do not bulk-delete this directory.
