# Talent Tree Runtime Rules

- Keep only player-safe UGUI components and serialized prefab data in this directory. Do not reference `UnityEditor` or authoring assets.
- The generated prefab must remain embeddable under an existing Canvas. Do not add a Canvas or EventSystem to it.
- Runtime behavior in this tool covers display and pan/zoom. Unlocking, point costs, persistence, and activation visuals are owned by game integration code.
- Each generated directed edge has its own curve Graphic and serialized view reference so game code can change that edge's state independently. Avoid rebuilding curve meshes when the viewport merely pans or zooms.
- Give every directed edge a hidden `Active` child curve and serialize its reference for game code to toggle. Keep editable line style on UGUI Graphics and geometry in hidden serialized curve data; runtime never references editor-only `Handles`. A prefab re-export may refresh geometry without resetting manually tuned styles.
- Generated content may be rectangular but stays centered on the authored `(0,0)` origin. Keep its size and zero origin available in player-safe serialized data. The six sector guides belong only in the editor and must not render in generated prefabs.
