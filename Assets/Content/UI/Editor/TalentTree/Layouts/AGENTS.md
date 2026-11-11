# Talent Tree Layout Assets

- Store `TalentTreeLayout` `.asset` files here. Name each asset `<definition-name>Layout.asset` and set its `definition` reference to the matching graph asset in `../Definitions/`.
- Keep node positions, icon references, edge Bezier handles, inactive and active line bake styles, and board style in the layout. Match nodes by stable `key` and edges by directed `fromKey`/`toKey`; never copy numeric table IDs here. Sprite images are generated outputs, not editable style sources.
- Keep `lineTextureResolution` here as the raster export setting. The full PoE sample uses Half so its next export is faster; this setting must not rescale authored node or curve coordinates.
- Keep `guideMode` here as an editor-only visual choice. New empty layouts use Horizontal1080; older or migrated layouts use SixSectors until a user switches them. Guide switching does not move nodes or curves and does not change export bounds. Every export uses a fixed 1080-unit content height and requires all visual geometry inside Y=±540; the lines at Y=±420 mark the recommended safe area.
- Use the editor's layout sync action for newly added graph nodes and edges. Keep existing entry order stable; append only missing records and retain obsolete entries until an explicit cleanup. Export ignores entries with no matching graph record. Anyone may edit node positions, icons, curves, and board style in the same editor window.
- Do not place generated prefabs, JSON, temporary previews, or copied game art here. Remove a retired layout only after confirming its logic definition is retired.
