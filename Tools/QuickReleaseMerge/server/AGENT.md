# QuickReleaseMerge Server 目录约定

## 目标

本目录存放 QuickReleaseMerge 自带的本地只读单据服务，用于读取 Meego Base 中当前用户待 release 的需求/BUG。它是 QuickReleaseMerge 包的一部分，不依赖 `SvnFeishuCommitPicker`。

## 文件分工

- `ticket-service.ps1`：本地 HTTP 服务入口，提供 `http://127.0.0.1:18765/api/my-open-workitems`。
- `meego-base-provider.ps1`：调用 Meego Base 只读接口查询单据。
- `server.config.meego-base.sample.json`：服务配置示例，不包含 API key。
- `StartMeegoBaseTicketService.cmd`：启动服务的便捷入口。
- `mock-tickets.sample.json` / `server.config.sample.json`：排障和离线调试示例。

## 安全边界

- 只允许读取 Meego Base 单据信息。
- 不保存、不提交 API key；API key 只从 `MEEGO_BASE_API_KEY` 用户环境变量读取。
- 不接入流程流转、评论、状态修改等写接口。
