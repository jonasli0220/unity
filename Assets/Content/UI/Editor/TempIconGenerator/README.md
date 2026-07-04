# Temp Icon Generator

Unity 编辑器内的临时图标生成工具，用于快速创建带文本水印的 PNG 占位资源。

## 应用场景

- 策划配置表需要先绑定图标，但正式美术资源还没出。
- UI 拼 prefab 时需要临时占位图，避免资源路径空缺。
- 同一批临时资源需要明确区分内容，例如“临时商店背景”“测试道具图标”。
- 希望临时图标尺寸和目标文件夹现有资源保持一致。

## 使用方式

1. 在 Unity Project 窗口中右键目标文件夹。
2. 点击 `UI > Temp Icon Generator`。
3. 确认目标路径，必要时点 `change folder` 更换文件夹。
4. 点击 `+add` 添加临时资源条目。新条目会自动沿用上一条命名并递增末尾数字。
5. 填写资源命名、文本水印和尺寸。
6. 点击 `create` 生成 PNG。
7. 成功弹窗会展示生成路径，可点击 `copy` 复制。

说明：

- 尺寸会默认使用目标文件夹中数量最多的图片尺寸。
- 例如上一条命名为 `icon_item_zizouqi_baowu_310`，点击 `+add` 后会自动填入 `icon_item_zizouqi_baowu_311`。
- 如果目标文件夹没有图片，默认尺寸为 `0 x 0`，需要手动填写。
- 同名资源不会被直接覆盖，Unity 会自动生成唯一文件名。

## 安装方式

把下面两个内容一起复制到同一个 Unity 工程路径下：

- `Assets/Content/UI/Editor/TempIconGenerator/`
- `Assets/Content/UI/Editor/TempIconGenerator.meta`

复制完成后回到 Unity，等待脚本编译完成即可使用。

注意：`.meta` 文件需要一起复制，用来保留 Unity 文件夹 GUID。
