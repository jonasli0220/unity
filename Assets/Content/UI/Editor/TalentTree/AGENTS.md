# Talent Tree Editor Rules

- Keep authoring-only C# and the tool README in this directory. Put `TalentTreeDefinition` graph assets in `Definitions/`, named for the tree, and `TalentTreeLayout` presentation assets in `Layouts/`, named `<definition-name>Layout`. Do not store generated prefabs or JSON here.
- A definition owns node keys, numeric table IDs, node types, and directed prerequisite edges. A layout owns node positions, icons, Bezier handles, board dimensions, and visual style. Link them by stable node keys and directed edge keys. The editor exposes all operations together and writes each change only to the asset that owns that field; do not gate editing by user role.
- Preserve legacy visual fields in definitions only for one-time migration into a layout. Do not use those fields for new edits or exports.
- Every graph edit must support Unity Undo and mark only the definition dirty; every presentation edit must support Undo and mark only the layout dirty. Keep serialized node/edge entry order stable so text merges can resolve edits to different records. Export is explicit and must validate before changing either generated output.
- Export reads and validates both assets without modifying either. Missing layout entries must produce an actionable error rather than silently writing to the UI-owned asset.
- Keep temporary previews, logs, and stress-test data in `Library/Dragon/TalentTree`; remove them after validation. Do not commit them.
- When a definition is retired, remove its paired layout and generated prefab/JSON together after checking ownership. Never clean unrelated files by wildcard.
