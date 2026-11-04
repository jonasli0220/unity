# Talent Tree Editor Rules

- Keep authoring-only C# and the tool README in this directory. Put designer-owned `TalentTreeDefinition` assets in `Definitions/`, named for the tree; do not store generated prefabs or JSON here.
- A definition is the only editable graph source. Store stable internal node keys separately from numeric table IDs so changing an ID never silently breaks editor connections.
- Every graph edit must support Unity Undo and mark the definition dirty. Export is explicit and must validate before changing either generated output.
- Keep temporary previews, logs, and stress-test data in `Library/Dragon/TalentTree`; remove them after validation. Do not commit them.
- When a definition is retired, remove its generated prefab and JSON together after checking they were produced by this tool. Never clean unrelated files by wildcard.
