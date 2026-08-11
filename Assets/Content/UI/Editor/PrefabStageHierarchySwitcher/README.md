# Prefab Stage Hierarchy Switcher

## 解决什么问题

在 Play Mode 中打开一个 UI Prefab 后，Unity 默认会让 Hierarchy 只显示当前 Prefab Stage，导致 Game 视窗里正在运行的完整节点树暂时看不到。

本工具让同一个 Hierarchy 跟随最近一次操作的视窗：

- 点击 `Game` 页签：Hierarchy 展示当前运行中的全部已加载 Scene，包括可发现的 `DontDestroyOnLoad` Scene。
- 点击 `Scene` 页签：Hierarchy 恢复展示当前打开的 Prefab 节点。
- 点击 Inspector、Project、Hierarchy、Console 等其他窗口：保持上一次 Game/Scene 的选择，不反复跳动。

切换只改变 Hierarchy 的显示来源，不会退出 Prefab Stage，也不会修改运行时节点、Prefab、场景、Selection 或游戏输入。

## 使用方式

工具首次导入后默认开启，只在以下条件同时满足时工作：

1. Unity 正处于 Play Mode。
2. 当前打开了 Prefab Stage。
3. Hierarchy 窗口没有被锁定。

可通过 `UITools/Prefab编辑时按视窗切换Hierarchy` 开关功能。退出 Play Mode、关闭 Prefab Stage、关闭开关、脚本重载或退出 Unity 时，Hierarchy 都会恢复原来的显示配置。

## 兼容边界

项目使用 Unity `2021.3.8f1`。Unity 没有公开“让单个原生 Hierarchy 显示另一个 Stage”的 API，因此本工具通过反射调用该版本原生 Hierarchy 自带的 `customScenes` 能力。

如果未来升级 Unity 后内部接口发生变化，工具只输出一次可操作的警告并保留 Unity 默认行为，不会持续刷 Console。

## 手动验证

1. 进入 Play Mode，确认未打开 Prefab 时 Hierarchy 正常显示运行时节点。
2. 从运行时 UI 打开一个 Prefab，确认 Scene 视窗与 Hierarchy 都显示该 Prefab。
3. 点击 Game 页签，确认 Hierarchy 切回运行时节点，且 Prefab Stage 没有关闭。
4. 点击 Scene 页签，确认 Hierarchy 恢复当前 Prefab 节点。
5. 分别验证关闭 Prefab、退出 Play Mode、关闭菜单开关后，Hierarchy 均恢复 Unity 默认状态。
6. 锁定一个 Hierarchy 窗口，确认它不会被工具切换。
