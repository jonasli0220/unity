# Talent Tree Editor

Open **Tools > UI > Talent Tree > Open**. Each tree now has two source assets:

- **策划逻辑**: `Definitions/<name>.asset` owns stable node keys, table IDs, Small/Core types, and directed prerequisite connections. In **策划：逻辑** mode, add nodes with the left buttons, edit IDs and types, and right-click a source node to connect it to a target. Every incoming edge is a mandatory direct prerequisite; cycles and duplicate IDs are rejected.
- **UI 表现**: `Layouts/<name>Layout.asset` references the logic asset and owns the six-sector board size, node positions and icons, Bezier handles, line and node styles. In **UI：表现** mode, drag new nodes from the left **待摆放节点** list into the canvas, or click **同步新增节点/连线** to seed every missing layout entry. Drag on empty canvas space to box-select; drag any selected node to move the whole group. Shift-drag another box to add nodes; Shift/Ctrl-click a node to add or remove it. Click a connection to edit its two yellow Bezier handles. Right-drag or middle-drag to pan; use the wheel to zoom around the pointer.

The first time an existing combined definition is opened, click **创建表现配置**. The tool copies its old positions, icons, curve handles, and style into a new layout asset without rewriting the logic asset. After that, each editing mode writes only its own asset and supports Unity Undo. Obsolete layout entries are retained but ignored by export; missing entries must be synchronized or placed before export.

**检查** validates both assets and their relationship. **生成 Prefab + JSON** merges their current saved data into the pair under `Assets/Content/UI/Prefab/talent_tree/`; it does not modify either source. Regenerate after either role changes its asset. Collaborators should submit their own source asset and let one integration export publish the generated pair, avoiding concurrent edits to the same output files.

The prefab contains a transparent viewport, clipped radial background, batched curves, node UI, `TalentTreeData`, and `TalentTreePanZoom`. Place it under an existing Canvas and EventSystem. It does not implement unlocking, costs, save data, or activation visuals. The six equal sectors are visual guides only and add no sector field to nodes.

The version 2 JSON contains `sourceGuid` for the logic asset, `layoutGuid` for the UI asset, `boardSize`, `nodes`, and `edges`. Each node has `id`, `type`, `position`, `iconAssetPath`, and sorted `prerequisiteIds`. Each edge has `fromId`, `toId`, `fromHandle`, and `toHandle`. The prefab's `TalentTreeData` exposes the same merged graph and direct node-view references.

For a first look, run **Tools > UI > Talent Tree > Create Example Tree**. It creates or migrates the two example assets and rebuilds their output pair. **Run Validation Checks** tests duplicate IDs, directed cycles, and missing UI layout. **Run 1000 Node Stress Check** verifies curve batching and removes only its own temporary assets.
