# Talent Tree Runtime Rules

- Keep only player-safe UGUI components and serialized prefab data in this directory. Do not reference `UnityEditor` or authoring assets.
- The generated prefab must remain embeddable under an existing Canvas. Do not add a Canvas or EventSystem to it.
- New helper MonoBehaviours in this folder are not guaranteed to exist in a released player and must not be attached to patch-delivered prefabs. The exporter uses shipped `UILineRenderer` for cubic curves and built-in UGUI for nodes and panning; the paired JSON carries the graph.
- Patch-safe output displays with shipped UGUI components and pans with `ScrollRect`. Zoom, unlocking, point costs, persistence, and activation visuals require game integration with scripts already in the installed player.
- Each generated directed edge has its own curve Graphic with stable `Edge <from> to <to>` and `Active` hierarchy names so shipped game code can change that edge's state independently. Avoid rebuilding curve meshes when the viewport merely pans or zooms.
- Give every directed edge a hidden `Active` child curve. Keep editable line style on shipped `UILineRenderer` components and geometry in its four serialized control points; runtime never references editor-only `Handles`. A prefab re-export may refresh geometry without resetting manually tuned styles.
- Generated content may be rectangular but stays centered on the authored `(0,0)` origin. Keep its size and zero origin in the paired JSON. The six sector guides belong only in the editor and must not render in generated prefabs.
