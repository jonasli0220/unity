# Life Sprite Meta 与九宫编辑工具

适用于 Life 的 Unity 2022.3 UI 工程。无需打开 Unity，可生成图片 meta、预览并拖动九宫绿线。自动识别所选资源所属工程，不绑定盘符或 trunk 分支。

## 给同事使用

1. 解压整个 `LifeSpriteMetaTool.zip` 到一个固定目录，例如 `D:\Tools\LifeSpriteMetaTool`。安装后不要移动或删除该目录。
2. 双击 `install.bat`，允许 Windows 管理员提示。安装系统级菜单，无需填写工程路径。
3. 在 `Assets\Content\UI` 内的文件夹空白处右键，选 **生成 Life Sprite Meta**，补齐当前文件夹及子文件夹的图片、文件夹 meta。
4. 右键单张 PNG/JPG/JPEG，选 **设置 Life Sprite 九宫**。可以拖动绿线或输入左、上、右、下边距，保存后更新图片旁的 meta。

没有 meta 也能直接编辑九宫：保存时自动生成。Windows 11 使用“显示更多选项”或 Shift+F10；备用入口为“发送到 → 设置 Life Sprite 九宫”。

没有管理员权限时，可在 PowerShell 运行 `powershell -ExecutionPolicy Bypass -File .\InstallContextMenu.ps1` 安装到当前用户。若一级菜单被本机其他扩展隐藏，可先用“发送到”入口。

卸载：双击 `uninstall.bat`。Life 版使用独立注册项和菜单名称，不覆盖原 Unity Sprite 工具。

## Life 生成规则

- 路径限定为所选 Unity 工程的 `Assets/Content/UI`；同级 `ProjectSettings` 用于识别工程。支持任意盘符、trunk 或分支。
- 批量支持 PNG/JPG/JPEG/TGA/PSD；已有 meta 跳过。软链接或目录联接跳过。
- 普通 UI：Single Sprite、100 Pixels Per Unit、sRGB、关闭 Mipmap、透明通道、Bilinear、Repeat、默认最大尺寸 2048。
- Android/iPhone：ASTC 6×6、最大尺寸 2048、压缩质量 50；Server：1024、不开启覆盖。不附带 Dragon 的 OpenHarmony 设置。
- 路径包含 `notcompress`：默认平台及 Android/iPhone 使用 RGBA32。
- `Assets/Content/UI/image/cursor...`：按 Life 导入代码生成可读写的 Cursor 类型；此类图片不提供 Sprite 九宫。
- 已有 meta 的九宫保存只更改 `spriteBorder`，保留 GUID、平台覆盖、internalID 和其他字段。已有非 Single Sprite 不自动转换，提示用户在 Unity 中确认类型。
- 左右之和必须小于原图宽度，上下之和必须小于原图高度。边距单位为原图像素。

本工具用于 UI Sprite 工作流，不把现有特效纹理、字体图集自动转成 Sprite。Life 的其他检查器仍可能在 Unity 内按资源用途调整设置，例如图标压缩检查；工具不会覆盖已有 meta 来强制统一这些设置。

## 命令行

预览批量生成（不写资源 meta）：

```powershell
powershell -ExecutionPolicy Bypass -File .\GenerateUnitySpriteMeta.ps1 -Path "H:\Life\trunk\life\Assets\Content\UI\image" -WhatIf
```

手动打开九宫：

```powershell
powershell -STA -ExecutionPolicy Bypass -File .\SetUnitySpriteBorder.ps1 -ImagePath "H:\Life\trunk\life\Assets\Content\UI\image\example.png"
```

日志默认在 `C:\tmp\LifeMetaGenerator`。`config.json` 在安装时生成，只保存菜单名称和日志目录。分享包不包含本机配置、游戏资源或项目 meta。

## 兼容性核验

核对对象：Life Unity 2022.3.44f1 的 `Assets/Content/UI` 及现有 `Assets/Editor/ImportChecker.cs`。
扫描 22,867 张图片：22,624 张 Single Sprite、243 张其他导入配置；672 张存在非零边距，未发现小数边距，所有图片均已有 meta。

验证使用临时生成图片及现有 meta 的本地副本，检查新建模板、原 GUID/导入设置保留、九宫读写、特殊路径和路径边界。没有批量修改 Life 实际资源，也没有启动 Unity 做导入验证；首次导入时 Unity 仍可能规范化序列化字段。
