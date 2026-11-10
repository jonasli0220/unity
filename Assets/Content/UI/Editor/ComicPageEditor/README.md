# 漫画页面编辑器

入口：Unity 菜单 `UITools/漫画页面编辑器`。

1. 选择 `Assets/Content/UI/Prefab/exterior_hero_story/resource` 下的赛季 + 英雄文件夹，例如 `a10_24370`。
2. 选择 PNG/JPG 背景。窗口把图片居中裁切至 16:9，导出为 1920×1080 PNG。
3. 点击「＋ 文本框」，在预览画布上拖拽创建；拖动框体可移动，拖动右下角可缩放。右侧编辑文字、坐标尺寸、九宫格锚点、文字对齐、字色与 Auto Size 最小/最大字号。文本节点自动命名为 `txt_1`、`txt_2`……。
4. 点击「导出」。在所选目录生成 `comic/comic_N.prefab` 和 `comic/resource/N.png`。预览和导出复用样例 `a10_24370/comic/comic_1.prefab` 的 `SgrImage`、`MultiLanguageTMPText`、字体及描边材质。
5. 点击「＋ 新页」继续制作下一页。顶部下拉框可打开并修改已有页；新页编号取当前最大编号 + 1。

窗口内文本框修改可用 Unity `Ctrl+Z` 撤销。导出是磁盘资源写入，Unity Undo 无法撤销；覆盖已有 PNG/prefab 前会自动在项目的 `Library/Dragon/ComicPageEditor/Backups` 留备份。该目录是本机临时备份，可确认资源无误后清理。

导出仅接受项目内指定的赛季 + 英雄目录。图片像素写入 `resource`，不修改原始图片；prefab 保持 `comic_N → root → img_bg / txt_N` 结构。
