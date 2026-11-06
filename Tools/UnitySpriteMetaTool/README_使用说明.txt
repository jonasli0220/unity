Unity Sprite Meta Tool 使用说明

用途：
在不打开 Unity 编辑器的情况下，为任意 Unity 工程 Assets 目录内新增图片生成 .meta，并设置 Sprite 九宫参数。

安装：
1. 解压 UnitySpriteMetaTool.zip。
2. 双击 install.bat。
3. 无需填写工程路径，工具自动识别所选资源所属的 Unity 工程。
4. 支持 trunk、分支和其他 Unity 工程；Assets 同级需要有 ProjectSettings。
5. 在 Assets 或其子文件夹空白处右键，点击“生成 Unity Sprite Meta”。
6. 要设置九宫时，右键单张 PNG/JPG/JPEG 图片，点击“设置 Unity Sprite 九宫”。在窗口内输入左、上、右、下边距并保存。
   Windows 11 请先点“显示更多选项”，或选中图片后按 Shift+F10。文件夹空白处只有批量生成命令。
   如果经典菜单里没有九宫命令，可用“发送到 → 设置 Unity Sprite 九宫”。

卸载：
双击 uninstall.bat。

配置文件：
config.json

projectRoot / uiRoot：旧配置字段，现已不再限制路径。
menuName：右键菜单显示名称
logRoot：日志输出目录

说明：
一般不需要手动编辑 config.json。双击 install.bat 安装菜单；运行时自动识别工程。

生成规则：
- 支持任意 Unity 工程的 Assets 及其子目录；Assets 外的路径不执行。
- 递归扫描当前文件夹和子文件夹。
- 已存在 .meta 的图片不会覆盖。
- 缺失 .meta 的子文件夹会自动补齐；不生成 Assets.meta。
- 跳过符号链接和目录联接，避免写入其他位置。
- 支持 .png、.jpg、.jpeg、.tga、.psd。
- 九宫窗口支持 .png、.jpg、.jpeg，显示图片与绿色九宫切线。
- 已有单张 Sprite .meta 只修改 spriteBorder，保留 GUID 和其他导入设置；缺失时创建新 .meta。
- 左右之和必须小于图片宽度，上下之和必须小于图片高度；Multiple Sprite 暂不支持。
- Unity meta 的字段映射：x=左，y=下，z=右，w=上。
- 生成完成后会显示所有新增图片的 Unity 相对路径，并自动复制到剪贴板，方便同步给策划。

注意：
如果 Unity 打开后仍然修改 .meta，通常是 Unity 对 importer 做最终序列化。只要 guid 没变，资源引用就是稳定的。
