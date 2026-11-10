# Comic Page Editor 目录约定

- 本目录只放漫画逐页编辑器的 Editor 代码与使用说明。窗口入口为 `ComicPageEditorWindow.cs`；如需拆分，导出逻辑命名为 `ComicPageExporter.cs`，不放运行时代码。
- Unity 资源必须配套提交 `.meta`；本目录不存放漫画图片、prefab、临时预览或备份。
- 导出资源只进入所选 `Assets/Content/UI/Prefab/exterior_hero_story/resource/<赛季_英雄>/comic/`：`comic_N.prefab` 与 `resource/N.png`。预览对象随窗口关闭销毁；覆盖备份放在 `Library/Dragon/ComicPageEditor/Backups`，可按需清理。
- 修改导出格式前先更新本文件与 `README.md`，再改代码；以现有 `a10_24370/comic/comic_1.prefab` 的层级、组件、字体和描边材质为兼容基准。
