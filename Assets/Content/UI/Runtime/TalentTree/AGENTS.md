# Talent Tree Runtime Rules

- Keep only player-safe UGUI components and serialized prefab data in this directory. Do not reference `UnityEditor` or authoring assets.
- The generated prefab must remain embeddable under an existing Canvas. Do not add a Canvas or EventSystem to it.
- New helper MonoBehaviours in this folder are not guaranteed to exist in a released player and must not be attached to patch-delivered prefabs. `TalentTreeData` and `TalentTreeCurveGraphic` remain available for reading older generated prefabs in the editor. Current exports use baked Sprite Images plus built-in UGUI components and a paired JSON graph.
- Patch-safe output pans with `ScrollRect` and contains no new runtime MonoBehaviours. Game code already shipped in the player mounts visible node prefabs and active edge Images from JSON, then handles zoom, unlocking, point costs, persistence, and activation visuals.
- Current exports have empty `Content/Nodes` and `Content/ActiveEdges` layers. Do not expect per-edge curve Graphics or hidden `Active` children in a newly generated prefab.
- Generated content may be rectangular but stays centered on the authored `(0,0)` origin. Keep its size and zero origin in the paired JSON. The six sector guides belong only in the editor and must not render in generated prefabs.
