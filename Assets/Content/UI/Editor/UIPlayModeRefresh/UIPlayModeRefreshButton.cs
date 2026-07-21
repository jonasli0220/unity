using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using XPython;
using XSolution.AssetBundles;

[InitializeOnLoad]
internal static class UIPlayModeRefreshButton
{
    private const string ButtonName = "dragon-ui-play-mode-refresh-button";
    private const string PythonModule = "cli_utils.ui_editor_refresh";
    private const string MenuPath = "UITools/运行模式/刷新当前界面";

    // Unity 2021.3 的 Game View 工具栏右侧控件宽约 380px。
    // 将按钮固定在这些控件之前；不改变 Game View 的内容区域或运行时输入。
    private const float ToolbarRightOffset = 384f;
    private const float ButtonWidth = 88f;
    private const float ButtonHeight = 20f;
    private const double UpdateInterval = 0.25d;

    private static readonly Type GameViewType =
        typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
    private static readonly Color EnabledButtonBackground =
        new Color32(76, 76, 76, 255);
    private static readonly Color EnabledButtonText =
        new Color32(224, 224, 224, 255);
    private static readonly Color DisabledButtonBackground =
        new Color32(52, 52, 52, 255);
    private static readonly Color DisabledButtonText =
        new Color32(112, 112, 112, 255);

    private static double nextUpdateTime;
    private static bool pythonBridgeInstalled;
    private static bool installErrorLogged;

    static UIPlayModeRefreshButton()
    {
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem(MenuPath, false, 500)]
    private static void RefreshFromMenu()
    {
        RefreshCurrentUI(FindFocusedOrFirstGameView(), null);
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateRefreshFromMenu()
    {
        return EditorApplication.isPlaying;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            pythonBridgeInstalled = false;
            installErrorLogged = false;
            nextUpdateTime = 0d;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode ||
                 state == PlayModeStateChange.EnteredEditMode)
        {
            pythonBridgeInstalled = false;
            installErrorLogged = false;
        }
    }

    private static void OnEditorUpdate()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < nextUpdateTime)
        {
            return;
        }

        nextUpdateTime = now + UpdateInterval;
        EnsureGameViewButtons();

        if (EditorApplication.isPlaying && XPythonEnv.isInited && !pythonBridgeInstalled)
        {
            TryInstallPythonBridge();
        }

        UpdateButtonStates();
    }

    private static void EnsureGameViewButtons()
    {
        if (GameViewType == null)
        {
            return;
        }

        UnityEngine.Object[] gameViews = Resources.FindObjectsOfTypeAll(GameViewType);
        for (int i = 0; i < gameViews.Length; i++)
        {
            EditorWindow window = gameViews[i] as EditorWindow;
            if (window == null || window.rootVisualElement == null)
            {
                continue;
            }

            VisualElement root = window.rootVisualElement;
            Button button = root.Q<Button>(ButtonName);
            if (button == null)
            {
                button = CreateButton(window);
                root.Add(button);
            }

            // Game View 重建 UI 树或切换布局后，确保按钮仍位于最上层。
            button.BringToFront();
        }
    }

    private static Button CreateButton(EditorWindow gameView)
    {
        Button button = new Button
        {
            name = ButtonName,
            text = "刷新界面",
            tooltip = "保存当前 UI Prefab 后点击，重新加载运行中的最前层界面。"
        };

        button.AddToClassList("unity-editor-toolbar-button");
        button.style.position = Position.Absolute;
        button.style.top = 1f;
        button.style.right = ToolbarRightOffset;
        button.style.width = ButtonWidth;
        button.style.minWidth = ButtonWidth;
        button.style.maxWidth = ButtonWidth;
        button.style.height = ButtonHeight;
        button.style.minHeight = ButtonHeight;
        button.style.maxHeight = ButtonHeight;
        button.style.paddingLeft = 7f;
        button.style.paddingRight = 7f;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.borderLeftWidth = 0f;
        button.style.borderRightWidth = 0f;
        button.style.borderTopWidth = 0f;
        button.style.borderBottomWidth = 0f;
        button.style.borderTopLeftRadius = 3f;
        button.style.borderTopRightRadius = 3f;
        button.style.borderBottomLeftRadius = 3f;
        button.style.borderBottomRightRadius = 3f;
        ApplyButtonVisual(button, false);
        button.clicked += () => RefreshCurrentUI(gameView, button);
        return button;
    }

    private static void UpdateButtonStates()
    {
        if (GameViewType == null)
        {
            return;
        }

        bool isPlaying = EditorApplication.isPlaying;
        bool pythonReady = isPlaying && XPythonEnv.isInited;
        bool simulationEnabled = pythonReady && AssetSystem.SimulationOnEditor;
        bool canRefresh = simulationEnabled && pythonBridgeInstalled;

        UnityEngine.Object[] gameViews = Resources.FindObjectsOfTypeAll(GameViewType);
        for (int i = 0; i < gameViews.Length; i++)
        {
            EditorWindow window = gameViews[i] as EditorWindow;
            Button button = window != null
                ? window.rootVisualElement.Q<Button>(ButtonName)
                : null;
            if (button == null)
            {
                continue;
            }

            button.style.display = isPlaying ? DisplayStyle.Flex : DisplayStyle.None;
            button.SetEnabled(canRefresh);
            ApplyButtonVisual(button, canRefresh);

            if (!pythonReady)
            {
                button.tooltip = "游戏 Python 环境启动后即可刷新。";
            }
            else if (!simulationEnabled)
            {
                button.tooltip = "当前未启用编辑器资源模拟，运行中的 AssetBundle 无法读取刚保存的 Prefab。";
            }
            else if (!pythonBridgeInstalled)
            {
                button.tooltip = "正在连接运行时 UI 管理器，请稍候。";
            }
            else
            {
                button.tooltip = "保存当前 UI Prefab 后点击，重新加载运行中的最前层界面。";
            }
        }
    }

    private static void ApplyButtonVisual(Button button, bool isEnabled)
    {
        button.style.backgroundColor = isEnabled
            ? EnabledButtonBackground
            : DisabledButtonBackground;
        button.style.color = isEnabled
            ? EnabledButtonText
            : DisabledButtonText;
        button.style.unityFontStyleAndWeight = isEnabled
            ? FontStyle.Bold
            : FontStyle.Normal;
        // Unity's toolbar stylesheet lowers disabled opacity by default. Keep
        // explicit colors at full opacity so only the true disabled palette dims.
        button.style.opacity = 1f;
    }

    private static void TryInstallPythonBridge()
    {
        try
        {
            pythonBridgeInstalled =
                XPythonEnv.PyInvoke<bool>(PythonModule, "Install");
            installErrorLogged = false;
        }
        catch (Exception exception)
        {
            pythonBridgeInstalled = false;
            if (!installErrorLogged)
            {
                installErrorLogged = true;
                Debug.LogWarning(
                    "[UIPlayModeRefresh] 无法连接 Python 刷新桥接；按钮会自动重试。\n" +
                    exception);
            }
        }
    }

    private static void RefreshCurrentUI(EditorWindow gameView, Button sourceButton)
    {
        if (!EditorApplication.isPlaying)
        {
            ShowFeedback(gameView, "请先进入运行模式");
            return;
        }

        if (!XPythonEnv.isInited)
        {
            ShowFeedback(gameView, "游戏还在启动，请稍后再试");
            return;
        }

        if (!AssetSystem.SimulationOnEditor)
        {
            ShowFeedback(gameView, "当前不是编辑器资源模拟，无法读取刚保存的 Prefab");
            return;
        }

        if (!pythonBridgeInstalled)
        {
            TryInstallPythonBridge();
            if (!pythonBridgeInstalled)
            {
                ShowFeedback(gameView, "刷新工具还未连接，请稍后再试");
                return;
            }
        }

        SetTemporaryButtonText(sourceButton, "刷新中…");

        try
        {
            // 用户仍需先保存 Prefab；这里仅确保 Unity 已重新导入保存后的资源。
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            string message = XPythonEnv.PyInvoke<string>(PythonModule, "RefreshTopView");
            if (string.IsNullOrEmpty(message))
            {
                message = "刷新命令未返回结果，请查看 Console";
            }

            ShowFeedback(gameView, message);
            SetTemporaryButtonText(sourceButton,
                message.StartsWith("正在重新加载", StringComparison.Ordinal)
                    ? "已刷新"
                    : "刷新界面");
        }
        catch (Exception exception)
        {
            Debug.LogError("[UIPlayModeRefresh] 刷新运行中界面失败。\n" + exception);
            ShowFeedback(gameView, "刷新失败，请查看 Console");
            SetTemporaryButtonText(sourceButton, "刷新失败");
        }
    }

    private static void SetTemporaryButtonText(Button button, string text)
    {
        if (button == null)
        {
            return;
        }

        button.text = text;
        button.schedule.Execute(() =>
        {
            if (button.panel != null)
            {
                button.text = "刷新界面";
            }
        }).StartingIn(1200);
    }

    private static void ShowFeedback(EditorWindow gameView, string message)
    {
        EditorWindow target = gameView != null ? gameView : FindFocusedOrFirstGameView();
        if (target != null)
        {
            target.ShowNotification(new GUIContent(message));
        }

        Debug.Log("[UIPlayModeRefresh] " + message);
    }

    private static EditorWindow FindFocusedOrFirstGameView()
    {
        if (GameViewType == null)
        {
            return null;
        }

        EditorWindow focused = EditorWindow.focusedWindow;
        if (focused != null && GameViewType.IsInstanceOfType(focused))
        {
            return focused;
        }

        UnityEngine.Object[] gameViews = Resources.FindObjectsOfTypeAll(GameViewType);
        return gameViews.Length > 0 ? gameViews[0] as EditorWindow : null;
    }
}
