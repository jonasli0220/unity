# Temp Icon Generator Agent Context

## Scope

This directory contains the Unity Editor tool for creating temporary UI icon PNG assets with readable text watermarks.

## Placement

- Editor code stays in this directory.
- Tool documentation stays in `README.md`.
- Do not place generated PNG assets in this tool directory. The tool writes only to the target Project folder selected by the user.
- Keep all shareable tool files inside this folder. The only file outside it is Unity's required `TempIconGenerator.meta` folder metadata beside the folder.
- To share this tool, copy the `TempIconGenerator` folder and its adjacent `TempIconGenerator.meta` file together.

## Behavior Rules

- The Project context menu must work on folder selections only.
- The target folder path is stored as a Unity asset path under `Assets/`.
- New rows inherit the most common direct-child texture size in the target folder.
- Clicking `+add` should prefill the new row name by incrementing the last filled row's trailing number, for example `icon_item_310` to `icon_item_311`.
- If the target folder has no direct child textures, row size defaults to `0 x 0` and the user must edit it before creating assets.
- Existing assets are not overwritten silently. When a requested file name already exists, Unity's unique asset path generation is used.
- Generated PNGs are temporary placeholders: transparent background, neutral placeholder shape, centered text watermark.

## Cleanup Rules

- Keep generated resources in their feature/resource folders so planners can bind them directly.
- Delete unused temporary PNGs from the target folder when the real art resource arrives.
- Update this file before changing workflow rules or output naming behavior.
