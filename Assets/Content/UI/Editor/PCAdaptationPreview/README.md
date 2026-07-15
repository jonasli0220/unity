# PC Adaptation Preview

Scene View toolbar button for temporarily previewing UI PC adaptation while editing UI prefabs or scenes, including Play Mode.

## Usage

- Open a UI prefab or scene in the Unity Editor, or enter Play Mode.
- In Prefab Stage, click the button directly to preview the currently opened prefab; no specific node selection is required.
- In regular scenes, select a UI node under the target canvas when you only want to preview that canvas.
- Enable the existing Scene View overlay `Prefab导航` if it is hidden.
- Click the `PC适配预览` button in `Prefab导航` to toggle preview.
- The button text stays `PC适配预览` whether preview is on or off.
- Click the button again to restore the original scale state.
- Fallback menu: `Tools/UI/PC Adaptation Preview/Toggle PC 0.8`.

## Behavior

- When an active scene `CanvasScaler` is found, preview uses `CanvasScaler.uiScaleMode = ConstantPixelSize` and `CanvasScaler.scaleFactor = 0.8`.
- When editing a UI prefab with a root `Canvas` but no effective `CanvasScaler`, preview temporarily adds a `DontSave` `CanvasScaler` to that existing root `Canvas`, then removes it when preview ends.
- The preview tool never adds or deletes `Canvas` components. It may remove old preview-created `DontSave` `CanvasScaler` components left by earlier tool versions.
- In Prefab Stage, embedded child prefab `CanvasScaler` components are ignored so background prefabs are not treated as the preview target.

Enabled nodes with `AdaptationLocking` are refreshed during preview so they keep their locked visual size, then restored to their exact pre-preview state. Disabled `AdaptationLocking` components are ignored, and nested-prefab `AdaptationLocking` components removed through prefab override are treated as missing. If the selected node has no effective lock but an ancestor does, Scene View shows `父级锁定: <node>` so the inherited locking source is visible. Enabled `AutomaticBackground` nodes are also refreshed and restored, because background adaptation reads and caches the parent scaler state.
