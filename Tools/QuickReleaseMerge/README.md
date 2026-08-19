# UI/远程资源快速 merge 到 CN/NA release

这个工具用于在 `trunk` 的 `Assets/Content/UI` 目录右键打开待 release 单列表，并把单号对应的 trunk UI、RemoteAssets SVN 提交合并到 CN release 或 NA release 对应工作副本。

## 功能边界

- 处理 `Assets/Content/UI` 下的 UI 改动，以及 `RemoteAssets` 下的远程拉图资源改动。
- 只按单号对应的 trunk SVN 提交做 merge，不复制或覆盖整棵 UI/RemoteAssets 目录。
- 不自动 SVN Commit，只按本单实际改动范围唤起一个或两个 TortoiseSVN Commit 窗口。
- 不自动流转飞书/Meegle 流程；提交后只检查 release SVN log 是否已包含单号，流程请点击【打开单子】手动流转。
- 目标 release 工作副本已有本地改动时，会阻止对应目标分支的 merge。

## 安装

双击运行：

```text
QuickReleaseMerge\install.bat
```

安装时会依次确认：

```text
Meego Base API Key
TortoiseSVN 程序路径
trunk UI 路径
```

如果本机已经配置过 `MEEGO_BASE_API_KEY`，安装脚本会自动检测到，并允许直接保留，不需要重复粘贴 API Key。

默认 TortoiseSVN 程序路径：

```text
C:\Program Files\TortoiseSVN\bin\TortoiseProc.exe
```

默认 trunk UI 路径：

```text
G:\Dragon\trunk\dragon\Assets\Content\UI
```

安装阶段不会要求填写 CN release、NA release 或 RemoteAssets 路径。第一次点击【merge to CN release】或【merge to NA release】时，工具会按本单实际改动范围检查目标路径：

- UI 路径未配置时，提示选择并保存。
- RemoteAssets 路径优先自动检测；只有本单包含远程资源改动且自动检测失败时，才提示选择并保存。

默认自动检测路径：

```text
CN RemoteAssets: G:\Dragon\release\dragon\RemoteAssets
NA RemoteAssets: G:\Dragon\NA RemoteAssets
```

## 日常使用

1. 在 trunk UI 目录空白处右键，点击【快速 merge】。
2. 工具会立即显示加载进度，并实时拉取当前用户名下节点为【待提交 CN release】的需求/BUG，同时查询 trunk UI 与 RemoteAssets 提交。
3. 需要提前处理仍在【QA测试】节点的单子时，勾选窗口右上角的【包含 QA测试】；工具会立即重新拉取，并把 QA 测试中的需求/BUG 一起纳入 SVN 检测。该勾选只在本次打开期间有效，下次启动仍默认不勾选。
4. 选择一行，点击【merge to CN release】或【merge to NA release】。
5. 列表“变更范围”会显示 `UI`、`远程资源` 或 `UI + 远程资源`。如果对应目标路径尚未配置且无法自动检测，先按弹窗选择工作副本目录。
6. 工具先对本单全部 UI/远程资源 merge group 逐 revision 执行 `svn merge --dry-run`，全部检查完成后才按 revision 顺序真实 merge 到对应工作副本。
   如果 dry-run 发现冲突，会弹窗询问是否【用 trunk 覆盖冲突】；只有你确认后，工具才会用 trunk 版本重试并 resolve 本次 merge 的文本/树冲突。
7. merge 成功后，工具会自动复制提交信息，例如：

```text
#7006319869 【外观】海外活动宣传页坐骑名称修正
```

8. 点击该行的【提交】，工具按实际变更范围打开 TortoiseSVN Commit 窗口：只改一个区域时打开一个窗口，同时改 UI 和远程资源时分别打开两个窗口。
9. 你在每个 TortoiseSVN 窗口中手动检查、粘贴相同的 message 并提交。
10. 提交完成后，回到快速 merge 窗口点击【检查提交】。
11. 工具检测到 release SVN log 已包含该单号后，按钮变为【已提交】。
12. 点击【打开单子】，在飞书/Meegle 页面里手动流转流程。

启动时，多张单号会合并为一批 SVN 日志查询，UI 与 RemoteAssets 日志会并行读取，同一 CN/NA 目标的路径状态也会按工作副本批量检查。这里只减少 SVN 调用次数，不使用过期缓存，单据状态和新提交仍会在每次打开或点击【重新加载】时刷新。

列表会显示每张单最后一条 trunk UI/RemoteAssets 提交的时间，并按最后匹配 revision 从旧到新排列。多个需求/BUG 的提交彼此穿插时，建议从上往下 merge，让较新的 trunk 改动最后应用；没有找到 UI 或远程资源提交的阻止项会排在末尾。

## 风险提示

- `可 merge`：dry-run 前检查通过，可以点击 merge。
- `Warning`：可以 merge，但有风险提示，例如包含 UI/RemoteAssets 根目录变更，或目标目录仅有待提交的 SVN 合并记录（`svn:mergeinfo`）。
- `Blocked`：工具会阻止 merge，例如目标 release 工作副本已有文件内容修改、其他属性修改、冲突，或没有找到对应 trunk UI/远程资源提交。

SVN merge 可能在 UI 或 RemoteAssets 目录上留下 `svn:mergeinfo` 属性修改。它只记录已合入的 revision，不代表 prefab、图片等文件内容被本地修改；工具会显示黄色提示并允许继续，之后在对应 TortoiseSVN 提交窗口中与本次 merge 一并提交即可。

## 冲突覆盖

当 UI 或 RemoteAssets 的 SVN merge 提示冲突，而你确认这次要以 trunk 改动为准时，可以在冲突弹窗中选择继续。工具只会对本单涉及的冲突路径使用 trunk 版本处理：

```text
svn merge --accept theirs-full
svn resolve --accept theirs-full / theirs-conflict
svn export --force -r <trunk_revision> <trunk_conflict_url> <release_conflict_path>
svn resolve --accept working <release_conflict_path>
```

这里的 `theirs` 指本次从 trunk 合过来的版本。若 SVN 返回 tree conflict，例如 release 里目标文件/目录缺失、但 trunk 有编辑，工具会在你确认后把 trunk 对应 revision 的文件/目录导出到 release 工作副本，再用 `svn resolve --accept working` 标记已处理。这个动作可能覆盖 release 上同一文件/目录的改动，所以工具不会默认执行，必须由你在弹窗里二次确认。

如果 SVN 返回过冲突信息，但 `svn status` 已经没有未解决的 `C` 路径，工具会认为 `--accept theirs-full` 已经自动处理完文本冲突，并继续后续 revision。

## API Key

安装流程会把 Meego Base API Key 保存到 Windows 用户环境变量：

```text
MEEGO_BASE_API_KEY
```

API key 不会写进工具目录或分享包。安装完成后，QuickReleaseMerge 自带的本地只读单据服务会自动尝试启动：

```text
QuickReleaseMerge\server\StartMeegoBaseTicketService.cmd
```

如果后续需要重新配置 API key，可运行：

```text
QuickReleaseMerge\SetupLocalMeegoKey.cmd
```

## 日志

每次 dry-run 和真实 merge 的输出会保存到：

```text
C:\tmp\QuickReleaseMerge
```

## 卸载右键菜单

双击：

```text
QuickReleaseMerge\uninstall.bat
```
