# Asset Favorites

面向 UI 设计与 Unity 拼接工作的团队共享素材收藏夹。收藏只记录源资产 GUID，不复制、不移动、也不会修改原始资产。

## 打开方式

- Unity 菜单：`Tools > UI > Asset Favorites`
- Project 面板选中资产或文件夹后右键：`★ 收藏到 Favorites`

## 使用方式

- 直接把 Project 中的资产或文件夹拖到右侧内容区。
- 当前选择了左侧文件夹时，拖入内容收藏到该目录；选择 `All Favorites` 时，按 Prefab、Texture、Animation、Material、Audio 等类型自动分类。
- 右侧支持 Grid / List 切换和路径搜索。
- `Ctrl` / `Cmd` 点击切换多选，`Shift` 点击连续多选；多选后可一起拖到左侧文件夹。
- 单击资产会在 Project 中定位，双击打开；从收藏卡片拖出的是原始 Unity 资产，可继续拖入场景或 Inspector。
- 右键资产或点击工具栏 `Remove` 只移除收藏关系。

## 文件夹

- `+ Folder` 会在当前目录下新建子文件夹并立即进入重命名。
- 自定义文件夹支持嵌套、方向键导航、重命名和删除。
- 删除文件夹会移除它和子文件夹内的收藏关系，但绝不会删除项目原资产。
- 自动分类目录保持固定，不能重命名或删除。

## 团队数据

共享数据保存在：

```text
Assets/Content/UI/Library/AssetFavoritesLibrary.asset
```

该文件适合随项目 SVN 提交给团队。资产引用使用 GUID，因此源资产改名或移动后仍能定位。若多人同时编辑此资产，请像其他 Unity YAML 文件一样谨慎处理合并冲突。

