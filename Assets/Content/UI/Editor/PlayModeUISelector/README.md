# 运行时 Game 视图点选 UI

## 使用方式

1. 进入 Unity Play Mode。
2. 鼠标移到 `Game` 视图中的目标 UI 上。
3. 按住 `Alt`，点击鼠标左键。
4. Unity 会在 Hierarchy 中自动展开父节点、选中目标，并滚动到目标位置。

首次使用默认开启，不需要给 Prefab 或场景对象添加任何组件。

## 开关

菜单：`UITools/运行时 Alt+左键选中UI`

- 菜单前有勾：功能开启。
- 再点一次：关闭功能。

## 拾取规则

- 只在 Play Mode 的 `Game` 视图中响应。
- 只响应 `Alt + 鼠标左键`，普通点击不介入。
- 这次 Alt 点击只用于检查，不会继续触发鼠标下方的游戏按钮。
- 优先选中鼠标下实际显示在最上层的可见 UI 图层。
- UI 即使关闭了 `Raycast Target` 也可以被选中。
- 完全透明、被裁剪、Hierarchy 中隐藏或禁止拾取的对象不会被选中。
- 点击文字的 TMP 子网格时，会选中文字对象本身，而不是 TMP 自动生成的子对象。

## 范围

当前版本面向 Unity UGUI（`Graphic`、`Image`、TMP UI 文本等）。没有可见 `Graphic` 的空节点，以及没有 Collider 的 3D/2D 场景物体，不属于本工具的拾取范围。

工具是纯 Editor 功能，不会写入场景、Prefab 或游戏包。
