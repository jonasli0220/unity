# SvnFeishuCommitPicker 目录约定

## 目标

本目录存放 TortoiseSVN 客户端 Hook 版的飞书单号选择工具。它支持 Dragon 与 Life 工作副本，只负责在提交前辅助填写 commit message，不负责执行 SVN 提交。

## 文件分工

- `SvnFeishuCommitPicker.ps1`：主入口，供 TortoiseSVN Manual Pre-commit Hook 调用。
- `RunSvnFeishuCommitPicker.cmd`：Dragon Hook 入口。
- `RunSvnFeishuCommitPicker-Life.cmd`：Life trunk/dev 共用的 Hook 入口，显式选择 Life 项目。
- `SetupLocalMeegoKey.cmd` / `SetupLocalMeegoKey.ps1`：本机首次配置入口，写入用户环境变量和本地配置。
- `SetupLifeMeegoKey.cmd` / `SetupLifeMeegoKey.ps1`：Life API Key 配置入口，只写入 `MEEGO_BASE_API_KEY_LIFE` 用户环境变量。
- `VerifyLocalSetup.cmd` / `VerifyLocalSetup.ps1`：本机自检入口，使用与 Hook 一致的当前用户身份规则检查 API Key、服务、接口和客户端来源。
- `BuildDistribution.ps1`：按固定白名单生成团队分发压缩包，并在打包后移除临时目录。
- `config.sample.json`：配置模板，提交到仓库，可给团队参考。
- `config.local.json`：个人本地配置，不应提交，存放接口地址、命令、用户偏好等。
- `user-map.local.json`：本地或团队生成的 Windows 用户名到飞书账号映射，不应提交。
- `user-map.sample.json`：用户名映射示例，可提交。
- `tickets.cache.json`：离线兜底缓存，可由刷新脚本或接口生成。
- `settings.local.json`：个人 UI 偏好，不应提交，存放窗口大小、列宽等。
- `README.md`：安装、配置、验证、排错说明。
- `dist/`：只存放可分发压缩包。压缩包必须按白名单生成，不得包含任何 `*.local.json`、缓存、用户映射或密钥。

## 命名和格式

- 脚本、配置字段使用英文。
- 面向用户的界面文字使用中文。
- 提交信息格式统一为 `#单号 标题`，多单号每行一条。
- Dragon 与 Life 必须使用独立的项目配置和详情链接；Life 的 trunk/dev 共用 `sgra_life` 单据空间。
- Dragon 与 Life API Key 分别使用 `MEEGO_BASE_API_KEY` 与 `MEEGO_BASE_API_KEY_LIFE`，不得互相覆盖，也不得写入工具目录。
- `end`、`ended`、`closed`、`done`、`finish`、`finished`、`completed`、`closed_done` 视为终态；当 status 或当前节点精确命中终态时，不得出现在默认列表中。
- 不在任何文件中保存 token、密码、cookie。
- 用户映射文件只允许保存 Windows 用户名、邮箱、飞书 user_key、展示名，不保存鉴权信息。
- 分发包只包含运行脚本、示例配置和 README；打包后必须检查文件清单并扫描个人账号、API Key、token 等敏感信息。

## 安全边界

- 工具只读飞书单据数据，只写 TortoiseSVN 传入的临时 message 文件，并静默尝试将同一段文本粘贴到当前 TortoiseSVN Message 输入框。
- 自动粘贴失败时保留剪贴板内容，用户仍可手动按 `Ctrl+V`，不得弹出成功或失败提示打断提交流程。
- 工具可以通过本机单据服务预先挂起一次静默关闭任务，在 Hook 成功结束后关闭 TortoiseSVN 自带的成功通知；只允许处理 `TortoiseProc` 进程中类名为 `tooltips_class32` 或标题为 `Notice` 的通知窗口，不得隐藏错误对话框或其他应用通知。
- 不自动提交、不自动推送、不修改资源文件。
- 如果拉取失败，必须保留用户已有 message，并提示可手动输入或使用缓存。
