# Talent Tree Runtime Rules

- Keep only player-safe UGUI components and serialized prefab data in this directory. Do not reference `UnityEditor` or authoring assets.
- The generated prefab must remain embeddable under an existing Canvas. Do not add a Canvas or EventSystem to it.
- Runtime behavior in this tool covers display and pan/zoom. Unlocking, point costs, persistence, and activation visuals are owned by game integration code.
- Curve rendering must batch edges in bounded chunks and avoid rebuilding meshes when the viewport merely pans or zooms.
