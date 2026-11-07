# Talent Tree Layout Assets

- Store `TalentTreeLayout` `.asset` files here. Name each asset `<definition-name>Layout.asset` and set its `definition` reference to the matching graph asset in `../Definitions/`.
- Keep node positions, icon references, edge Bezier handles, inactive and active line bake styles, and board style in the layout. Match nodes by stable `key` and edges by directed `fromKey`/`toKey`; never copy numeric table IDs here. Sprite images are generated outputs, not editable style sources.
- Use the editor's layout sync action for newly added graph nodes and edges. Keep existing entry order stable; append only missing records and retain obsolete entries until an explicit cleanup. Export ignores entries with no matching graph record. Anyone may edit node positions, icons, curves, and board style in the same editor window.
- Do not place generated prefabs, JSON, temporary previews, or copied game art here. Remove a retired layout only after confirming its logic definition is retired.
