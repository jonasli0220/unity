# Effect Preview

在不进入 Play Mode 的情况下，从 Scene 视图的 `Prefab导航` 工具条预览当前 UI Prefab 或选中节点下的粒子效果。

## 使用方式

- 打开一个 UI Prefab。
- 如果只想看局部效果，选中包含粒子的节点；否则无需选择，工具会预览整个当前 Prefab。
- 点击 `▶ 粒子` 开始播放，再点击 `■ 粒子` 停止并清空粒子。
- 普通 Scene 中必须先选择目标节点，工具不会自动播放整个场景的粒子。
- 备用菜单：`Tools/UI/Effect Preview/Play or Stop Particles`。

## 当前支持

- 支持 Unity `ParticleSystem`，包含拖尾、Sub Emitters 等由粒子系统自身驱动的表现。
- 预览只推进非序列化的粒子模拟状态，不修改粒子参数，不创建 Undo，不保存到 Prefab。
- 脚本逻辑、Shader 全局时间、Timeline、Animator 和 Visual Effect Graph 仍需要 Play Mode；工具不会把这些结果伪装成完整运行时预览。

脚本重编译、进入 Play Mode、退出 Unity 或切换/关闭 Prefab 时，预览会自动停止并清空。
