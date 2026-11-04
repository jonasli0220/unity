# Talent Tree Layout Assets

- Store only UI-owned `TalentTreeLayout` `.asset` files here. Name each asset `<definition-name>Layout.asset` and set its `definition` reference to the matching planner-owned asset in `../Definitions/`.
- Keep node positions, icon references, edge Bezier handles, and board style in the layout. Match nodes by stable `key` and edges by directed `fromKey`/`toKey`; never copy numeric table IDs here.
- Use the editor's layout sync action for newly added logic nodes and edges. Keep obsolete layout entries until the UI owner explicitly removes them; export ignores entries with no matching logic record.
- Do not place generated prefabs, JSON, temporary previews, or copied game art here. Remove a retired layout only after confirming its logic definition is retired.
