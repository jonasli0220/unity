# 运行模式 Animation 动效保存

用于把 Play Mode 中针对运行时 UI 实例调好的 Animation 窗口曲线，安全保存回源 `.anim`。

## 使用方法

1. 在运行模式里照常选中 UI 节点，在 Animation 窗口调整关键帧和曲线。
2. **不要先退出运行模式。**
3. 点击 `UITools/运行模式/保存当前 Animation 动效`。
4. 当前片段本身就是可写 `.anim` 时会直接保存；若是 AssetBundle/运行时克隆，工具会按动画名和当前 UI Prefab、Animator Controller 依赖寻找源 `.anim` 并回写。
5. 只有存在重名、无法唯一判断时才会弹出目标选择窗。按完整路径选中正确源文件后保存。
6. Animation 窗口提示 `✓ 已保存`，并在 Project 中定位源 `.anim` 后，即可安全退出运行模式。

## 保存内容

- 属性曲线和关键帧
- 对象引用曲线（Sprite 等）
- Animation Events
- Clip Settings、帧率与 Wrap Mode
- 运行时已删除的曲线也会从源 `.anim` 同步删除

回写源动画支持 `Ctrl+Z`，写入后还会从 `.anim` 回读验证；验证失败会自动撤销。

## 不会保存的内容

- 运行时 Scene 或 Hierarchy 实例
- Prefab 上其他组件的临时属性
- FBX 内嵌的只读 AnimationClip
- 脚本在运行时动态生成、且工程中没有对应源 `.anim` 的片段

如果调整的是 RectTransform、CanvasGroup 等组件当前值，而不是 Animation 窗口中的关键帧，需要另用 Prefab 编辑或运行时属性记录工具。
