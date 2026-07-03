# Asset Favorites

面向 UI 设计与 Unity 拼接工作的团队共享素材收藏夹。Project 资产收藏只记录源资产 GUID，不复制、不移动、也不会修改原始资产。Hierarchy 节点可收藏为“节点模板”，用于复用那些高频但不想手动维护成组件 prefab 的 UI 片段。

## 打开方式

- Unity 菜单：`Tools > UI > Asset Favorites`
- Project 面板选中资产或文件夹后右键：`★ 收藏到 Favorites`
- Hierarchy 或 Prefab Stage 中选中节点后右键：`★ 收藏为节点模板`

## 使用方式

- 直接把 Project 中的资产或文件夹拖到右侧内容区。
- 直接把 Hierarchy 中的 UI 节点拖到右侧内容区，可收藏为节点模板。
- 当前选择了左侧文件夹时，拖入内容收藏到该目录；选择 `All Favorites` 时，按 Prefab、Texture、Animation、Material、Audio 等类型自动分类。
- 未指定目录时，节点模板会自动进入 `节点 Node` 分类。
- 右侧支持路径搜索，以及由预览尺寸自动驱动的 Grid / List 浏览。
- 节点模板在列表中显示为 `Node · 宽×高 · 主要组件`，并可按来源层级路径、组件类型搜索。
- 节点模板会复用 `UIPrefabPreviewProjectOverlay.cs` 里的 UI prefab 预览生成逻辑绘制真实缩略图；Asset Favorites 侧只做 Editor 内存缓存，不额外生成图片资产。
- 右下角滑杆范围为 `64–512 px`：拖到 `64 px` 时自动变为紧凑单行列表，放大到 `>64 px` 时自动恢复网格；两端图标只提示当前形态，不能点击切换。
- 鼠标悬停滑杆时可直接滚轮缩放，在内容区使用 `Ctrl` / `Cmd + 滚轮` 缩放。普通滚轮仍用于上下浏览。
- `Ctrl` / `Cmd` 点击切换多选，`Shift` 点击连续多选；多选后可一起拖到左侧文件夹。
- 单击资产会在 Project 中定位，双击打开；从收藏卡片拖出的是原始 Unity 资产，可继续拖入场景或 Inspector。
- 单击节点模板会优先定位原始节点；若来源未加载，则定位工具生成的模板资源。
- 从节点模板收藏拖到 Hierarchy 某个父节点上，会复制出一个已 unpack 的节点实例；也可以右键选择 `放到当前选中节点下`。
- 从节点模板收藏直接拖入 Scene：若开始拖拽前已有选中的场景节点，新节点会放到同一父级并紧跟在该节点后；没有有效场景选中节点时，放到当前场景根级。
- 右键资产选择 `Remove from Favorites` 只移除收藏关系。
- 右键节点模板选择 `Remove from Favorites` 会移除收藏关系并删除工具生成的模板 prefab，但不会影响原始场景节点或 Prefab Stage 节点。

## 文件夹

- 左侧 `+ 新建文件夹` 会在当前目录下新建子文件夹并立即进入重命名。
- 自定义文件夹支持嵌套、方向键导航、重命名和删除。
- 删除文件夹会移除它和子文件夹内的收藏关系，但绝不会删除项目原资产。
- 自动分类目录保持固定，不能重命名或删除。

## 团队数据

共享数据保存在：

```text
Assets/Content/UI/Library/AssetFavoritesLibrary.asset
```

该文件适合随项目 SVN 提交给团队。资产引用使用 GUID，因此源资产改名或移动后仍能定位。若多人同时编辑此资产，请像其他 Unity YAML 文件一样谨慎处理合并冲突。

节点模板的生成 prefab 存放在：

```text
Assets/Content/UI/Library/AssetFavoritesNodeTemplates/
```

这个目录由工具维护，不作为普通 UI prefab 目录使用。同步到 GitHub 的 reusable script 仓库时，只发布编辑器脚本、文档和 `.meta`，不发布这里生成的节点模板 prefab。
