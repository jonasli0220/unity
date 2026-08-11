# Hierarchy Grouping

Select one or more sibling objects in the Unity Hierarchy and press `Ctrl+G`.

The command creates an empty parent named `group`, inserts it at the first selected sibling position, reparents the selected objects in their existing order, and selects the new group without a blocking Ping highlight so it can be renamed immediately. The whole operation is one Undo step; Undo restores each UI child's original parent, sibling order, anchors, pivot, position, size, rotation, and scale.

For UI objects, the new `RectTransform` is sized to the visible bounds of the selection. Selected children are converted to fixed center anchors so their current position, size, rotation, and scale stay visually unchanged after grouping. This intentionally freezes responsive/stretch anchor behavior at the moment of grouping, matching a Figma-style group more closely than a Unity layout container.

The command requires all selected top-level objects to share one immediate parent. It does not edit prefab assets selected in the Project window. During Play Mode it is available only while editing an open Prefab Stage.

The previous `UITools/Find With Path` command is still available from the menu, but no longer owns `Ctrl+G`.

When installing the standalone tool into another Dragon checkout, also remove `%g` from the old `[MenuItem("UITools/Find With Path %g", false)]` declaration so only this tool owns the shortcut.
