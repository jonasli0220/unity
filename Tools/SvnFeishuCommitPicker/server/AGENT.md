# SvnFeishuCommitPicker Server 目录约定

## 目标

本目录存放团队版单号查询服务。服务接收客户端传来的 Windows 用户名、邮箱或飞书 user_key，返回该用户参与的未关闭需求/BUG 列表。

## 文件分工

- `ticket-service.ps1`：本地/内网 HTTP 服务入口，使用 PowerShell 内置 `HttpListener`，不依赖第三方库。
- `meego-base-provider.ps1`：按 Meego Base API 文档查询当前用户参与的未关闭需求/BUG，输出标准票据 JSON。
- `server.config.sample.json`：服务配置模板。
- `server.config.meego-base.sample.json`：Meego Base API provider 配置示例。
- `StartMeegoBaseTicketService.cmd`：启动 Meego Base API 实时查询服务的便捷脚本，不包含密钥。
- `server.config.local.json`：本地服务配置，不应提交，存放监听地址、数据源路径等。
- `mock-tickets.sample.json`：接口返回数据样例，可用于本地联调。

## 接口约定

- `GET /api/my-open-workitems?windowsUser=xxx&email=xxx@tencent.com&userKey=xxx`
- `POST /api/dismiss-hook-success`：仅允许本机调用，挂起一次 TortoiseSVN Hook 成功通知关闭任务。
- 返回 JSON 数组，每项至少包含：
  - `id`
  - `type`
  - `title`
  - `status`
  - `node`

## 安全边界

- 不保存 token、密码、cookie。
- 正式接入飞书时，凭证只能放在服务端本地配置或公司密钥系统，不进入仓库。
- 客户端只拿到当前用户可见的必要字段，不返回完整需求详情。
- Provider 必须保留飞书返回的真实状态；status 或当前节点精确命中终态时，在服务端排除该单据。
- Provider 的 BUG 类型必须可配置；Dragon 使用内部 BUG type key，Life 使用 `issue`，输出统一归一化为 `bug`。
- 通知关闭接口只能启动工具目录内的静默辅助进程，不接收或执行外部命令。
