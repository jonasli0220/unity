# Unity UI Sprite Meta Generator

用于在不打开 Unity 编辑器的情况下，为任意 Unity 工程 `Assets` 下新增图片补齐 `.meta` 文件，并设置单张 Sprite 的九宫边距。

## 文件

- `GenerateUnitySpriteMeta.ps1`：扫描当前目录及子目录，生成缺失的图片和文件夹 `.meta`。
- `SetUnitySpriteBorder.ps1`：显示图片及九宫切线，设置左、上、右、下边距。
- `UnityAssetPath.ps1`：两个功能共用的 Unity 工程路径识别。
- `InstallContextMenu.ps1`：安装文件夹空白处和图片文件的右键菜单。
- `UninstallContextMenu.ps1`：卸载右键菜单。

## 安装右键菜单

在工具目录的 PowerShell 中执行：

```powershell
powershell -ExecutionPolicy Bypass -File ".\InstallContextMenu.ps1"
```

或直接双击：

```text
install.bat
```

安装无需填写工程路径。运行时自动从所选路径向上识别 Unity 工程：`Assets` 与 `ProjectSettings` 同级。trunk、分支及其他工程都能使用，无需切换配置。

安装后，在任意 Unity 工程的 `Assets` 文件夹或子文件夹空白处右键，点击“生成 Unity Sprite Meta”，可批量生成缺失的 `.meta`。

设置九宫时，右键单张 PNG/JPG/JPEG 图片，点击“设置 Unity Sprite 九宫”。窗口会读取现有边距并显示绿色切线；输入左、上、右、下像素值后保存。

可以直接拖动四条绿色切线，数字会实时同步；拖动按原图像素吸附，并保留至少 1 像素的中心区域。窗口启用高 DPI 感知，文字按屏幕缩放绘制，图片使用像素清晰放大。

文件名、尺寸说明和操作提示按实际字体高度自动排版，避免高 DPI 下文字重叠或截断。

Windows 11 上，这个命令位于图片右键菜单的“显示更多选项”内；选中图片后也可以按 `Shift+F10` 打开经典菜单。请右键**图片文件本身**，文件夹空白处显示的是批量生成命令。

如果图片的经典右键菜单没有显示该命令，可以使用 **“发送到 → 设置 Unity Sprite 九宫”**。安装脚本会为当前用户同时创建这个入口。

安装器同时在通用文件菜单注册图片专用的一级命令（限定 PNG/JPG/JPEG），使用完整程序路径。默认安装范围为当前用户；需要系统级安装时，在管理员 PowerShell 中运行 `./InstallContextMenu.ps1 -AllUsers`，卸载使用 `./UninstallContextMenu.ps1 -AllUsers`。系统级入口对本机所有用户生效。

九宫入口位于普通文件命令组，在本机显示于微信隔空传送下方、图片旋转上方；其他电脑的位置随已安装的菜单命令变化。重装会清理本工具旧版的重复注册，并保留“发送到”备用入口。

如需手动启动窗口：

```powershell
powershell -ExecutionPolicy Bypass -File ".\SetUnitySpriteBorder.ps1" -ImagePath "G:\Dragon\trunk\dragon\Assets\Content\UI\Image\example.png"
```

## 生成规则

- 支持任意 Unity 工程的 `Assets` 根目录及其子目录；工程根目录、Packages、Assets 外的路径不执行。
- 递归处理当前文件夹和子文件夹。
- 已存在 `.meta` 的图片会跳过，不会覆盖。
- 缺失 `.meta` 的子文件夹会自动补齐；不生成 `Assets.meta`。
- 跳过符号链接和目录联接，避免写入所选资源树以外的内容。
- 支持 `.png`、`.jpg`、`.jpeg`、`.tga`、`.psd`。
- 图片 `.meta` 默认写入 Unity `TextureImporter`，`textureType: 8`，`spriteMode: 1`。
- 九宫功能支持 PNG/JPG/JPEG 单张 Sprite。已有 `.meta` 只修改 `spriteBorder`，保留 GUID 和其他设置；没有 `.meta` 时按原模板创建。
- 参数顺序与 Unity Sprite Editor 一致：左、上、右、下，单位为像素。左右之和须小于图片宽度，上下之和须小于图片高度。
- Unity `.meta` 的 `spriteBorder` 字段顺序为 `x=左、y=下、z=右、w=上`。Sprite Mode 为 Multiple 的资源暂不支持编辑。

## 日志

旧 `config.json` 中的 `projectRoot`、`uiRoot` 和旧命令行同名参数保留兼容，但不再限制路径。配置仅使用菜单名称和日志目录。

运行后终端会停留并展示：

- 扫描目录
- 新增图片 `.meta` 数量
- 新增文件夹 `.meta` 数量
- 跳过数量
- 所有新增图片的 Unity 相对路径

同一份日志会保存到：

```text
C:\tmp\UnityMetaGenerator
```

## 手动测试

只预览不写入：

```powershell
powershell -ExecutionPolicy Bypass -File ".\GenerateUnitySpriteMeta.ps1" -Path "G:\Dragon\trunk\dragon\Assets\Content\UI\Image" -WhatIf
```

真实生成：

```powershell
powershell -ExecutionPolicy Bypass -File ".\GenerateUnitySpriteMeta.ps1" -Path "G:\Dragon\trunk\dragon\Assets\Content\UI\Image"
```

不用窗口也可以给单张图片设置九宫，例如左 80、上 24、右 49、下 47：

```powershell
powershell -ExecutionPolicy Bypass -File ".\GenerateUnitySpriteMeta.ps1" -Path "G:\Dragon\trunk\dragon\Assets\Content\UI\Image\example.png" -BorderLeft 80 -BorderTop 24 -BorderRight 49 -BorderBottom 47
```
