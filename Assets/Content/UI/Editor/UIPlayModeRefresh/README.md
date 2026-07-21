# 运行模式 UI 刷新

进入 Play Mode 后，Game 窗口顶部会出现 `刷新界面` 按钮。

## 使用

1. 保持游戏运行并打开要预览的 UI。
2. 修改并保存对应 Prefab。
3. 回到 Game 窗口点击 `刷新界面`。

工具会找到当前最前层的 Main / Activity / Tips / Filter 界面，释放旧实例和旧 Prefab 资源，再使用本次运行中记录的原始创建参数重新打开。也可通过 `UITools/运行模式/刷新当前界面` 触发。

## 限制与安全策略

- 仅支持 `AssetSystem.SimulationOnEditor` 编辑器资源模拟模式；运行 AssetBundle 时不会假装刷新成功。
- 工具必须在界面打开前捕获创建参数。若某个界面早于工具连接且它的 `Awake` 需要必填参数，工具会保留现状并提示先手动重开一次。
- 同一个 Prefab 同时被多个活动界面实例引用时不会强制卸载，以免破坏其他实例。
- `DontDestroyOnLoadAB` 界面不会被强制刷新。
- 工具不自动保存 Prefab，也不直接替换运行时层级对象。

## 文件

- `UIPlayModeRefreshButton.cs`：Game View 按钮、状态反馈和 Python 调用。
- `game/client/cli_utils/ui_editor_refresh.py`：捕获 UI 创建上下文，并通过项目 UIManager 安全关闭、卸载和重开界面。

共享工具目录中的文件需要分别放回以下位置：

- `UIPlayModeRefreshButton.cs` → `Assets/Content/UI/Editor/UIPlayModeRefresh/`
- `ui_editor_refresh.py` → `game/client/cli_utils/`
