# Effect Preview

在不进入 Play Mode 的情况下，从 Scene 视图的 `Prefab导航` 工具栏预览当前 UI Prefab 或选中节点下的运行时动效。

## 使用方式

- 打开一个 UI Prefab。
- 如果只想看局部效果，选中包含动效的节点；如果未选中可预览节点，工具会预览整个当前 Prefab。
- 点击 `▶ 动效` 开始播放，再点击 `■ 动效` 停止并复位临时预览状态。
- 普通 Scene 中必须先选择目标节点，工具不会自动播放整个场景的动效。
- 备用菜单：`Tools/UI/Effect Preview/Play or Stop Dynamic Effects`。

## 当前支持

- Unity `ParticleSystem`，包含拖尾、Sub Emitters 等由粒子系统自身驱动的表现。
- Spine `SkeletonGraphic`、`SkeletonAnimation`、`SkeletonMecanim`。
- Unity `Animator`。
- Legacy `Animation`。
- 如果节点上有 `UIPanelAnimation`，优先按项目运行时约定播放 `enter`，并在配置允许时自动转入 `loop`。

## 预览边界

- 工具只推进编辑器中的临时播放状态，不修改粒子、Spine、Animator 或 Animation 的源参数，不创建 Undo，不保存到 Prefab。
- `Animator` 和 legacy `Animation` 通过 Unity Animation Mode 采样；停止预览时会退出本工具开启的 Animation Mode 并恢复采样前状态。
- 暂不伪装完整运行时逻辑：自定义脚本驱动、Shader 全局时间、Timeline、Audio、Visual Effect Graph 等仍需要 Play Mode 或对应专用预览器。
- 脚本重编译、进入 Play Mode、退出 Unity、切换/关闭 Prefab 时，预览会自动停止并复位。
