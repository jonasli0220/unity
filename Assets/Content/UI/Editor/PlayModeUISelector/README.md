# 运行时 Game 视图点选 UI

## 使用方式

1. 进入 Unity Play Mode。
2. 鼠标移到 `Game` 视图中的目标 UI 上。
3. 按住 `Alt`，点击鼠标左键。
4. Unity 会在 Hierarchy 中自动展开父节点、选中目标，并滚动到目标位置。

如果已经选中一个 UI 节点，可以按住 `Alt`，在 `Game` 视图中用鼠标左键拖动它，临时调整这个节点的 `RectTransform` 位置。拖动只影响当前 Play Mode 中的运行时对象，不会保存回 Prefab。

首次使用默认开启，不需要给 Prefab 或场景对象添加任何组件。

## 一键打开引用的 Prefab

1. 在 Play Mode 里选中运行时 UI；也可以先用 `Alt + 左键` 从 Game 视图直接选中。
2. 在 Inspector 的对象标题下点击 `打开 xxx.prefab`。
3. 如果只找到一个同名 Prefab，Unity 会直接进入它的 Prefab Mode。

备用入口：在 Hierarchy 右键目标，选择 `UI/打开引用的 UI Prefab`。

- 工具会优先读取 Unity 自带的 Prefab 引用关系。
- 如果运行时加载已经断开引用关系，会从当前对象向上寻找最近的 `(Clone)` 节点，并按去掉 `(Clone)` 后的完整名称精确查找 Prefab。
- 优先查找 `Assets/Content/UI/Prefab`；这里没有结果时，再查找项目内其他 Prefab。
- 遇到完全同名的多个 Prefab 时会列出资源路径供选择，不会猜测并打开错误资源。

## 开关

菜单：`UITools/运行时 Alt+左键选中UI`

- 菜单前有勾：功能开启。
- 再点一次：关闭功能。

## 拾取规则

- 只在 Play Mode 的 `Game` 视图中响应。
- 只响应 `Alt + 鼠标左键`，普通点击不介入。
- 这次 Alt 点击只用于检查，不会继续触发鼠标下方的游戏按钮。
- 工具在运行时临时创建一个隐藏的 Editor 调试拦截层；退出运行状态后会自动销毁，不写入场景或 Prefab。
- 优先选中鼠标下实际显示在最上层的可见 UI 图层。
- 会自动跳过运行时点击反馈节点 `common_click_feedback(Clone)` 及其子节点，避免卡顿时误选中点击波纹/粒子反馈。
- 按住 `Alt` 拖动当前选中的 UI 节点时，会按鼠标在 Game 视图中的位移移动该节点。
- 如果鼠标按下时没有命中当前选中节点，会先选中鼠标下的 UI；继续拖动则移动这个新选中的 UI。
- 如果目标是被父级 `LayoutGroup` 直接控制的位置节点，工具不会强行写入位置，避免刚拖完又被布局系统拉回去。
- UI 即使关闭了 `Raycast Target` 也可以被选中。
- 完全透明、被裁剪、Hierarchy 中隐藏或禁止拾取的对象不会被选中。
- 点击文字的 TMP 子网格时，会选中文字对象本身，而不是 TMP 自动生成的子对象。
- Inspector 的打开按钮只在 Play Mode、且当前选中的是运行时 UI 对象时显示。

## 范围

当前版本面向 Unity UGUI（`Graphic`、`Image`、TMP UI 文本等）。没有可见 `Graphic` 的空节点，以及没有 Collider 的 3D/2D 场景物体，不属于本工具的拾取范围。

工具是纯 Editor 功能，不会写入场景、Prefab 或游戏包。
