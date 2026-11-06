# Unity UI Sprite Meta Generator

用于在不打开 Unity 编辑器的情况下，为 `Assets/Content/UI` 下新增图片补齐 `.meta` 文件，并设置单张 Sprite 的九宫边距。

## 文件

- `GenerateUnitySpriteMeta.ps1`：扫描当前目录及子目录，生成缺失的图片和文件夹 `.meta`。
- `SetUnitySpriteBorder.ps1`：显示图片及九宫切线，设置左、上、右、下边距。
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

安装时会提示输入 Unity 工程根目录。直接按回车会使用默认路径（首次安装用内置路径，之后用 `config.json`）；如果同事本机工程路径不同，粘贴自己的工程根目录即可，例如：

```text
G:\Dragon\trunk\dragon
```

安装后，在 `G:\Dragon\trunk\dragon\Assets\Content\UI` 内任意文件夹空白处右键，点击“生成 Unity Sprite Meta”，可批量生成缺失的 `.meta`。

设置九宫时，右键单张 PNG/JPG/JPEG 图片，点击“设置 Unity Sprite 九宫”。窗口会读取现有边距并显示绿色切线；输入左、上、右、下像素值后保存。

Windows 11 上，这个命令位于图片右键菜单的“显示更多选项”内；选中图片后也可以按 `Shift+F10` 打开经典菜单。请右键**图片文件本身**，文件夹空白处显示的是批量生成命令。

如果图片的经典右键菜单没有显示该命令，可以使用 **“发送到 → 设置 Unity Sprite 九宫”**。安装脚本会为当前用户同时创建这个入口。

如需手动启动窗口：

```powershell
powershell -ExecutionPolicy Bypass -File ".\SetUnitySpriteBorder.ps1" -ImagePath "G:\Dragon\trunk\dragon\Assets\Content\UI\Image\example.png"
```

## 生成规则

- 只允许扫描安装时配置的 `Assets\Content\UI` 及其子目录。
- 递归处理当前文件夹和子文件夹。
- 已存在 `.meta` 的图片会跳过，不会覆盖。
- 缺失 `.meta` 的文件夹会自动补齐。
- 支持 `.png`、`.jpg`、`.jpeg`、`.tga`、`.psd`。
- 图片 `.meta` 默认写入 Unity `TextureImporter`，`textureType: 8`，`spriteMode: 1`。
- 九宫功能支持 PNG/JPG/JPEG 单张 Sprite。已有 `.meta` 只修改 `spriteBorder`，保留 GUID 和其他设置；没有 `.meta` 时按原模板创建。
- 参数顺序与 Unity Sprite Editor 一致：左、上、右、下，单位为像素。左右之和须小于图片宽度，上下之和须小于图片高度。
- Unity `.meta` 的 `spriteBorder` 字段顺序为 `x=左、y=下、z=右、w=上`。Sprite Mode 为 Multiple 的资源暂不支持编辑。

## 日志

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
