# 运行模式 Prefab 修改保存

只记录你在 Play Mode 里通过 Inspector 或 Scene 手柄亲手修改的序列化属性，并一键同步回源 Prefab。运行逻辑自行改变的状态不会因为全量比对而混进保存结果。

## 使用方法

1. 进入 Play Mode，选中运行时 UI Prefab 的节点。
2. 在 Inspector 中修改位置、尺寸、颜色、文本、显隐或脚本公开字段等属性；也可以使用 Scene 手柄移动节点。
3. Inspector 顶部会出现 `保存修改 (N)`，`N` 是当前 Prefab 实例内尚未同步的手动修改属性数。
4. 点击按钮。工具会先自动备份源 Prefab，再只写入这些已记录属性，并回读验证。

同一个属性连续调整多次只计作一项，并以最后一次手动输入的值为准。保存成功后计数自动清零，不会销毁或重新创建当前运行时实例。

## 支持

- 现有节点、现有组件上的 Inspector/Scene 手柄属性修改
- RectTransform、图片、文字、材质、显隐、常见 Unity 组件及自定义脚本的可序列化字段
- Prefab 内部 GameObject/Component 引用自动映射
- 同名兄弟节点按同名顺序精确定位
- 保存失败自动还原；也可使用 `UITools/运行模式/撤销上次 Prefab 保存`

## 不会保存

- 游戏脚本在运行过程中自行改变、但没有经过 Unity Editor Undo 管线的状态
- 新增、删除、重排、重命名节点
- 新增或删除组件
- 数组长度与其他结构性修改
- Prefab 外部运行时 Scene 对象引用
- 粒子当前播放状态、Animator 当前 State、脚本运行缓存、托管引用等非安全序列化状态

如果同一源 Prefab 正在 Prefab Mode 中打开，工具会拒绝写入。请先保存并关闭该 Prefab Stage，避免两个编辑上下文互相覆盖。

源 Prefab 写入不能可靠进入普通 `Ctrl+Z` 栈，因此每次保存前都会自动备份。需要撤回时使用 `UITools/运行模式/撤销上次 Prefab 保存`。
