# 漫画页面编辑器

入口：Unity 菜单 `UITools/漫画页面编辑器`。

1. 选择 `Assets/Content/UI/Prefab/exterior_hero_story/resource` 下的赛季 + 英雄文件夹，例如 `a10_24370`。
2. 选择 PNG/JPG 背景。窗口把图片居中裁切至 16:9，导出为 1920×1080 PNG。
3. 点击「＋ 文本框」，在预览画布上拖拽创建。拖动框体可移动，拖动右下角可缩放。右侧可调整文字、坐标、尺寸、字色和 Auto Size 范围，长文本会自动换行。锚点使用 TMP 的两排对齐图标，上排选左、中、右，下排选上、中、下。关闭「文字对齐跟随锚点」后可单独调整文字对齐。
4. 新文本框默认填入 `##请输入文字`，前缀 `##` 用于跳过多语言 ID 分配。文本节点自动命名为 `txt_1`、`txt_2`……。调整字色后，之后创建的文本框沿用该颜色，重开 Unity 后也会记住。
5. 点击「导出」，在所选目录生成 `comic/comic_N.prefab` 和 `comic/resource/N.png`。预览和导出复用样例 `a10_24370/comic/comic_1.prefab` 的 `SgrImage`、`MultiLanguageTMPText`、字体及描边材质，并统一使用 TMP 行距 `-10`。
6. 点击「＋ 新页」继续制作下一页。顶部下拉框可打开并修改已有页。新页编号取当前最大编号 + 1。切换页面或目标文件夹时，未导出的修改会先提示，避免误丢。

窗口内文本框修改可用 Unity `Ctrl+Z` 撤销。导出是磁盘资源写入，Unity Undo 无法撤销；覆盖已有 PNG/prefab 前会自动在项目的 `Library/Dragon/ComicPageEditor/Backups` 留备份。该目录是本机临时备份，可确认资源无误后清理。

导出仅接受项目内指定的赛季 + 英雄目录。图片像素写入 `resource`，不修改原始图片；prefab 保持 `comic_N → root → img_bg / txt_N` 结构。
