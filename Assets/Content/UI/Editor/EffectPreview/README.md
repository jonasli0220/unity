# Effect Preview

在不进入 Play Mode 的情况下，从 Scene 视图的 `Prefab导航` 工具栏预览当前 UI Prefab 的运行时动效。

## 使用方式

- 打开一个 UI Prefab。
- 打开 Prefab 后无需选择节点；工具会扫描当前 Prefab 里所有已开启节点的动效。
- 点击 `动态预览` 开始播放，再次点击停止并复位到点击前的临时预览状态。
- 默认只预览粒子和 Spine。需要把 `Animator` / legacy `Animation` 也纳入预览时，先勾选 `UITools/动态预览包含animator`。
- 普通 Scene 中必须先选择目标节点，工具不会自动播放整个场景的动效。
- 备用菜单：`Tools/UI/Effect Preview/Play or Stop Dynamic Effects`。

## 当前支持

- Unity `ParticleSystem`，包含拖尾、Sub Emitters 等由粒子系统自身驱动的表现。
- Spine `SkeletonGraphic`、`SkeletonAnimation`、`SkeletonMecanim`。
- 可选：Unity `Animator`。
- 可选：Legacy `Animation`。
- 如果节点上有 `UIPanelAnimation`，优先按项目运行时约定播放 `enter`，并在配置允许时自动转入 `loop`。
- 勾选 `动态预览包含animator` 后，如果 `Animator` 没有 `UIPanelAnimation`，会重新绑定并更新 Animator，让 Animator Controller 从 Entry/default state 自己开始走，并按 Controller 里的 transition 继续播放。

## 预览边界

- 工具只推进编辑器中的临时播放状态，不修改粒子、Spine、Animator 或 Animation 的源参数，不创建 Undo，不保存到 Prefab。
- `Animator` 和 legacy `Animation` 默认不参与预览；参与时会通过本工具的临时快照在停止时恢复到点击预览前的可见状态。
- 暂不伪装完整运行时逻辑：自定义脚本驱动、Shader 全局时间、Timeline、Audio、Visual Effect Graph 等仍需要 Play Mode 或对应专用预览器。
- 脚本重编译、进入 Play Mode、退出 Unity、切换/关闭 Prefab 时，预览会自动停止并复位。
