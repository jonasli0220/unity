# QuickReleaseMerge 目录约定

## 目标

本目录存放 UI 快速合并到 release 的本地工具。工具用于在 `trunk` 的 `Assets/Content/UI` 右键打开待提交 release 单列表，并把对应单号在 trunk UI 下的 SVN 提交合并到 CN release 或 NA release UI 工作副本。

## 文件分工

- `QuickReleaseMerge.ps1`：主入口，负责拉取飞书/Meegle 单、扫描 trunk UI SVN 提交、做风险检查、执行单条 merge。
- `InstallContextMenu.ps1`：安装资源管理器文件夹空白处右键菜单，并完成 API key、TortoiseSVN 路径、trunk UI 路径的首次配置。
- `SetupLocalMeegoKey.cmd` / `SetupLocalMeegoKey.ps1`：独立 API key 配置入口；已存在 `MEEGO_BASE_API_KEY` 时应自动检测并允许保留。
- `server/`：QuickReleaseMerge 自带的本地只读单据服务，不依赖 `SvnFeishuCommitPicker` 目录。
- `UninstallContextMenu.ps1`：卸载右键菜单。
- `install.bat` / `uninstall.bat`：给非命令行用户使用的安装入口。
- `config.sample.json`：配置模板，可打包分享。
- `config.local.json`：本机配置，不应打包分享，存放本机路径和个人偏好。
- `settings.local.json`：窗口大小、列宽等 UI 偏好，不应打包分享。
- `README.md`：安装、使用、排错说明。

## 配置原则

- 安装阶段只要求填写 Meego Base API key、TortoiseSVN 程序路径、trunk UI 路径。
- QuickReleaseMerge 包必须自带单据服务；分享包内不再需要 `SvnFeishuCommitPicker` 同级目录。
- CN release / NA release UI 路径后置到第一次点击对应目标 merge 按钮时填写并保存。
- 不需要 NA release 的同事不应在安装阶段被要求填写或拉取 NA UI 工作副本。
- 面向用户的界面文字使用中文；脚本和配置字段使用英文。

## 安全边界

- 可以读取飞书/Meegle 单据、读取 SVN 日志、执行 `svn status`、`svn diff`、`svn merge --dry-run` 和单条 `svn merge`。
- merge 成功后可以复制提交信息，并唤起目标工作副本的 TortoiseSVN Commit 窗口。
- 不自动执行 `svn commit`。
- 不接入 Meegle workflow 写接口，不读取 workflow token；提交后只检测 release SVN log，流程由用户手动打开单子流转。
- Meego Base API key 只写入 Windows 用户环境变量 `MEEGO_BASE_API_KEY`，不写入代码、示例配置或分享包。
- 不删除资源文件。
- CN/NA release 目标路径已有本地改动或冲突时，必须按目标分支分别阻止自动 merge。
- 发生 SVN merge 冲突时，默认停止；只有用户在弹窗中明确确认“用 trunk 覆盖冲突”后，才允许对本次单号 merge 使用 `--accept theirs-full`，并对 `local missing/deleted + incoming edit` 这类 tree conflict 从 trunk 对应 revision `svn export --force` 到 release 工作副本后 `svn resolve --accept working`。
- 如果 SVN 输出曾报告冲突，但 `svn status` 已没有未解决的 `C` 路径，应视为 `--accept theirs-full` 已自动处理完文本冲突并继续后续 revision，不要误报失败。
- trunk 提交包含 UI 外路径时，必须标红/提示；继续合并前需要用户确认。
- 工具只按单号对应的 trunk UI 提交内容 merge，不做整棵 UI 目录的大范围 merge。
- 同一 merge group 内必须逐 revision 执行 dry-run / merge，避免 SVN 在中途冲突后跳过后续 revision。
- 单据列表默认实时拉取 Meego 服务，不自动使用 `tickets.cache.json` 兜底；缓存兜底只能作为临时排障开关。
- 启动分析应优先把多张单号合并为一次 `svn log --search` 查询，并把同一 release 目标的多个路径合并为一次 `svn status`；不得为了性能改用可能过期的单据或 SVN 结果缓存。
- 右键启动后应立即显示加载反馈，耗时的 Meego/SVN 查询结束后再进入主列表，避免用户误以为工具没有响应。
- 列表应显示每张单最后一条匹配 trunk UI 提交的本地时间，并按最后 UI revision 从旧到新排列；没有匹配提交的阻止项排在末尾，便于按 trunk 时间顺序执行覆盖 merge。
