# SVN 飞书单号选择工具

用于 TortoiseSVN 提交资源时快速填写飞书需求/BUG 单号，支持 Dragon 与 Life（trunk/dev）。

使用效果：

1. 在 TortoiseSVN Commit 界面点击 `Run hook script`。
2. 工具弹出“我参与的未关闭需求/BUG”列表，并排除状态或当前节点为 `end` 等终态的单据。
3. 选中单号，点击 `Copy message`，工具会复制提交信息并尝试直接填入 Message 输入框。

Hook 命令应始终指向本工具目录中的 `RunSvnFeishuCommitPicker.cmd`。不要指向其他工具包里遗留的 `SvnFeishuCommitPicker` 副本，否则升级不会生效。
4. 双击单号，会执行同样操作并关闭窗口。

提交信息格式：

```text
#6995000262 【A7】种植玩法新增ui及配置文本信息
```

## 首次配置


1. 双击：

```text
解压后的工具目录\SetupLocalMeegoKey.cmd
```

2. 按提示打开 API Key 创建页面：

```text
https://sgra.woa.com/meego-base/
```

3. 创建自己的 Meego Base API Key，并粘贴到配置窗口。

4. 等待工具自动完成：

- 保存 API Key 到当前 Windows 用户环境变量
- 写入本机配置
- 最小化启动本地查询服务
- 自动验证能否拉到单号

API Key 不会写入工具目录，不要把 API Key 发给别人。

## 配置 TortoiseSVN Hook

1. 打开 `TortoiseSVN Settings`。
2. 进入 `Hook Scripts`。
3. 点击 `Add...`。
4. `Hook Type` 选择：

```text
Manual Pre-commit Hook
```

5. `Working Copy Path` 选择自己的 SVN 工作目录，例如：

```text
D:\YourSvnWorkingCopy
```

6. `Command Line` 填：

```cmd
解压后的工具目录\RunSvnFeishuCommitPicker.cmd
```

7. 勾选：

```text
Wait for the script to finish
```

## Life trunk/dev 配置

Life 使用同一套界面和提交格式，但需要单独授权 `sgra_life` 项目。

1. 双击 `SetupLifeMeegoKey.cmd`。
2. 在 API Key 页面创建或选择一个具有 `sgra_life` 只读权限的 Key。
3. 等待脚本自动验证 Life 单据接口。
4. 分别给 Life trunk 和 dev 添加 `Manual Pre-commit Hook`，命令都填：

```cmd
解压后的工具目录\RunSvnFeishuCommitPicker-Life.cmd
```

建议的 `Working Copy Path`：

```text
H:\Life\trunk
H:\Life\dev
```

Dragon Key 保存在 `MEEGO_BASE_API_KEY`；Life Key 保存在 `MEEGO_BASE_API_KEY_LIFE`。两者互不覆盖，也不会写入工具目录。

## 日常使用

1. 打开 TortoiseSVN Commit 界面。
2. 点击 `Run hook script`。
3. 在列表中选中一行或多行。
4. 点击 `Copy message`，或直接双击目标单号。
5. 工具关闭后，提交信息会直接出现在 Message 输入框；如果当前 TortoiseSVN 版本未识别到输入框，剪贴板中仍保留提交信息，可手动按 `Ctrl+V`。

列表支持：

- 点击行即选中
- 双击单行可直接复制并关闭窗口
- 搜索标题、单号、类型、节点
- `Open link` 打开对应飞书单
- 记住窗口大小和列宽

重启电脑后不需要手动启动服务。第一次点击 Hook 时，如果本地服务未运行，工具会自动最小化启动服务，然后再拉取单号。

## 常见问题

### 点 Hook 后列表不是最新的

先双击检查：

```text
解压后的工具目录\VerifyLocalSetup.cmd
```

如果提示本地服务未运行，可以双击：

```text
解压后的工具目录\server\StartMeegoBaseTicketService.cmd
```

### Windows 用户名和邮箱不一致

默认会按这个规则推导邮箱：

```text
Windows用户名@tencent.com
```

如果不一致，新建或修改：

```text
解压后的工具目录\user-map.local.json
```

示例：

```json
{
  "windows_user_name": {
    "email": "xxx@tencent.com",
    "userKey": "飞书 user_key",
    "name": "中文名"
  }
}
```

### Message 没有自动填入

工具会先写入 TortoiseSVN 传入的临时 message 文件，再静默识别当前提交窗口并粘贴。不同 TortoiseSVN 版本的输入控件可能不同；如果没有自动填入，提交信息仍在剪贴板中，回到 Message 输入框按 `Ctrl+V` 即可。

### Hook 成功后还有提示气泡

这个气泡由 TortoiseSVN 自身显示。工具会在拉取单号时通过常驻的本机单据服务预热关闭任务，在 Hook 成功结束后尽快静默关闭它，不影响 Hook 错误对话框。升级工具后需要重启一次本机单据服务或重启电脑。

### 服务窗口能不能关

可以最小化，不建议关闭。关闭后下次点 Hook 会自动再启动。

### API Key 要不要提交

不要。API Key 只保存在自己的 Windows 用户环境变量 `MEEGO_BASE_API_KEY` 中。

## 文件说明

- `SetupLocalMeegoKey.cmd`：首次配置入口。
- `RunSvnFeishuCommitPicker.cmd`：TortoiseSVN Hook 调用入口。
- `SetupLifeMeegoKey.cmd`：Life API Key 配置和验证入口。
- `RunSvnFeishuCommitPicker-Life.cmd`：Life trunk/dev 共用的 Hook 入口。
- `VerifyLocalSetup.cmd`：本机自检入口。
- `SvnFeishuCommitPicker.ps1`：单号选择窗口主脚本。
- `server\StartMeegoBaseTicketService.cmd`：手动启动本地查询服务。
- `config.local.json`：个人本机配置，不要提交。
- `user-map.local.json`：个人用户映射，不要提交。
- `settings.local.json`：窗口大小和列宽偏好，不要提交。

## 工具边界

- 只读取飞书需求/BUG。
- 写入临时 message 文件，并尝试直接填入当前 TortoiseSVN Message 输入框。
- 不会自动提交 SVN。
- 不会修改资源文件。
