using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dragon.UI.EditorTools
{
    internal static class DualProjectBrowser
    {
        private const string MenuPath =
            "UITools/Project \u53cc\u7a97\u53e3/\u6253\u5f00\u9501\u5b9a\u8d44\u6e90\u7a97\u53e3";
        private const string AssetMenuPath =
            "Assets/\u5728\u9501\u5b9a\u8d44\u6e90\u7a97\u53e3\u4e2d\u6253\u5f00";
        private const int ConfigureAttemptCount = 8;

        private const BindingFlags InstanceMemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMemberFlags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Type ProjectBrowserType =
            typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");

        private static readonly MethodInfo InitMethod = ProjectBrowserType == null
            ? null
            : ProjectBrowserType.GetMethod(
                "Init",
                InstanceMemberFlags,
                null,
                Type.EmptyTypes,
                null);

        private static readonly MethodInfo GetActiveFolderPathMethod =
            ProjectBrowserType == null
                ? null
                : ProjectBrowserType.GetMethod(
                    "GetActiveFolderPath",
                    InstanceMemberFlags,
                    null,
                    Type.EmptyTypes,
                    null);

        private static readonly MethodInfo ShowFolderContentsMethod =
            ProjectBrowserType == null
                ? null
                : ProjectBrowserType.GetMethod(
                    "ShowFolderContents",
                    InstanceMemberFlags,
                    null,
                    new[] { typeof(int), typeof(bool) },
                    null);

        private static readonly PropertyInfo IsLockedProperty =
            ProjectBrowserType == null
                ? null
                : ProjectBrowserType.GetProperty(
                    "isLocked",
                    InstanceMemberFlags);

        private static readonly FieldInfo LockTrackerField =
            ProjectBrowserType == null
                ? null
                : ProjectBrowserType.GetField(
                    "m_LockTracker",
                    InstanceMemberFlags);

        private static readonly FieldInfo LastInteractedProjectBrowserField =
            ProjectBrowserType == null
                ? null
                : ProjectBrowserType.GetField(
                    "s_LastInteractedProjectBrowser",
                    StaticMemberFlags);

        [MenuItem(MenuPath, false, 2050)]
        private static void OpenLockedResourceBrowser()
        {
            EditorWindow sourceBrowser = FindSourceProjectBrowser();
            string selectedPath = NormalizeAssetPath(
                AssetDatabase.GetAssetPath(Selection.activeObject));
            string folderPath = AssetDatabase.IsValidFolder(selectedPath)
                ? selectedPath
                : GetActiveFolderPath(sourceBrowser);
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                folderPath = GetSelectedAssetFolderPath();
            }

            OpenLockedResourceBrowserAt(
                AssetDatabase.IsValidFolder(folderPath) ? folderPath : "Assets",
                sourceBrowser);
        }

        [MenuItem(AssetMenuPath, false, 2050)]
        private static void OpenSelectedFolderInLockedResourceBrowser()
        {
            string folderPath = GetSelectedAssetFolderPath();
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            OpenLockedResourceBrowserAt(
                folderPath,
                FindSourceProjectBrowser());
        }

        [MenuItem(AssetMenuPath, true)]
        private static bool ValidateOpenSelectedFolderInLockedResourceBrowser()
        {
            return AssetDatabase.IsValidFolder(GetSelectedAssetFolderPath());
        }

        private static void OpenLockedResourceBrowserAt(
            string folderPath,
            EditorWindow sourceBrowser)
        {
            if (!ReflectionIsAvailable())
            {
                EditorUtility.DisplayDialog(
                    "\u65e0\u6cd5\u6253\u5f00\u7b2c\u4e8c\u4e2a Project \u7a97\u53e3",
                    "\u5f53\u524d Unity \u7248\u672c\u65e0\u6cd5\u8bbf\u95ee\u539f\u751f ProjectBrowser "
                    + "\u7684\u7a97\u53e3\u3001\u5bfc\u822a\u6216\u9501\u5b9a\u63a5\u53e3\u3002\n\n"
                    + "\u539f\u6709 Project \u7a97\u53e3\u672a\u88ab\u4fee\u6539\uff0c"
                    + "\u8bf7\u66f4\u65b0\u8be5\u5de5\u5177\u7684 Unity \u7248\u672c\u9002\u914d\u3002",
                    "\u77e5\u9053\u4e86");
                return;
            }

            EditorWindow browser = null;
            try
            {
                browser = ScriptableObject.CreateInstance(ProjectBrowserType)
                    as EditorWindow;
                if (browser == null)
                {
                    throw new InvalidOperationException(
                        "UnityEditor.ProjectBrowser did not create an EditorWindow.");
                }

                PositionBesideSource(browser, sourceBrowser);
                ConfigureBrowser(browser, folderPath);
                browser.Show();
                browser.Focus();
                ShowSuccess(browser, folderPath);
            }
            catch (Exception exception)
            {
                if (browser == null)
                {
                    ShowConfigurationFailure(
                        null,
                        folderPath,
                        exception);
                    return;
                }

                browser.Show();
                browser.Focus();
                ConfigureWhenReady(
                    browser,
                    folderPath,
                    ConfigureAttemptCount);
            }
        }

        private static void ConfigureBrowser(
            EditorWindow browser,
            string folderPath)
        {
            if (InitMethod != null)
            {
                InitMethod.Invoke(browser, null);
            }

            Object folderAsset =
                AssetDatabase.LoadAssetAtPath<Object>(folderPath);
            if (folderAsset == null)
            {
                throw new InvalidOperationException(
                    "Folder asset could not be loaded: " + folderPath);
            }

            ShowFolderContentsMethod.Invoke(
                browser,
                new object[] { folderAsset.GetInstanceID(), true });

            if (!SetLocked(browser, true))
            {
                throw new MissingMemberException(
                    "ProjectBrowser lock state could not be changed.");
            }

            ApplyResourceWindowTitle(browser, folderPath);
            browser.Repaint();
        }

        private static void ConfigureWhenReady(
            EditorWindow browser,
            string folderPath,
            int attemptsRemaining)
        {
            EditorApplication.delayCall += () =>
            {
                if (browser == null)
                {
                    return;
                }

                try
                {
                    ConfigureBrowser(browser, folderPath);
                    browser.Focus();
                    ShowSuccess(browser, folderPath);
                }
                catch (Exception exception)
                {
                    if (attemptsRemaining > 1)
                    {
                        ConfigureWhenReady(
                            browser,
                            folderPath,
                            attemptsRemaining - 1);
                        return;
                    }

                    ShowConfigurationFailure(
                        browser,
                        folderPath,
                        exception);
                }
            };
        }

        private static void ShowSuccess(
            EditorWindow browser,
            string folderPath)
        {
            browser.ShowNotification(
                new GUIContent(
                    "\u5df2\u9501\u5b9a\u8d44\u6e90\u6587\u4ef6\u5939\n"
                    + folderPath),
                2.5d);
        }

        private static bool ReflectionIsAvailable()
        {
            return ProjectBrowserType != null
                && ShowFolderContentsMethod != null
                && (IsLockedProperty != null || LockTrackerField != null);
        }

        private static bool SetLocked(
            EditorWindow browser,
            bool isLocked)
        {
            try
            {
                if (IsLockedProperty != null && IsLockedProperty.CanWrite)
                {
                    IsLockedProperty.SetValue(
                        browser,
                        isLocked,
                        null);
                    return true;
                }

                if (LockTrackerField == null)
                {
                    return false;
                }

                object lockTracker = LockTrackerField.GetValue(browser);
                if (lockTracker == null)
                {
                    return false;
                }

                PropertyInfo trackerIsLockedProperty =
                    lockTracker.GetType().GetProperty(
                        "isLocked",
                        InstanceMemberFlags);
                if (trackerIsLockedProperty == null
                    || !trackerIsLockedProperty.CanWrite)
                {
                    return false;
                }

                trackerIsLockedProperty.SetValue(
                    lockTracker,
                    isLocked,
                    null);
                if (lockTracker.GetType().IsValueType)
                {
                    LockTrackerField.SetValue(browser, lockTracker);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static EditorWindow FindSourceProjectBrowser()
        {
            EditorWindow focusedWindow = EditorWindow.focusedWindow;
            if (IsProjectBrowser(focusedWindow))
            {
                return focusedWindow;
            }

            if (LastInteractedProjectBrowserField != null)
            {
                EditorWindow lastInteracted =
                    LastInteractedProjectBrowserField.GetValue(null)
                    as EditorWindow;
                if (IsProjectBrowser(lastInteracted))
                {
                    return lastInteracted;
                }
            }

            if (ProjectBrowserType == null)
            {
                return null;
            }

            Object[] browsers =
                Resources.FindObjectsOfTypeAll(ProjectBrowserType);
            for (int index = 0; index < browsers.Length; index++)
            {
                EditorWindow browser = browsers[index] as EditorWindow;
                if (browser != null)
                {
                    return browser;
                }
            }

            return null;
        }

        private static bool IsProjectBrowser(EditorWindow window)
        {
            return window != null
                && ProjectBrowserType != null
                && ProjectBrowserType.IsInstanceOfType(window);
        }

        private static string GetActiveFolderPath(EditorWindow browser)
        {
            if (!IsProjectBrowser(browser)
                || GetActiveFolderPathMethod == null)
            {
                return null;
            }

            try
            {
                return NormalizeAssetPath(
                    GetActiveFolderPathMethod.Invoke(browser, null)
                    as string);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string GetSelectedAssetFolderPath()
        {
            Object selectedObject = Selection.activeObject;
            if (selectedObject == null)
            {
                return null;
            }

            string selectedPath = NormalizeAssetPath(
                AssetDatabase.GetAssetPath(selectedObject));
            if (AssetDatabase.IsValidFolder(selectedPath))
            {
                return selectedPath;
            }

            if (string.IsNullOrEmpty(selectedPath))
            {
                return null;
            }

            return NormalizeAssetPath(
                Path.GetDirectoryName(selectedPath));
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrEmpty(path)
                ? null
                : path.Replace('\\', '/').TrimEnd('/');
        }

        private static void PositionBesideSource(
            EditorWindow browser,
            EditorWindow sourceBrowser)
        {
            if (sourceBrowser == null)
            {
                return;
            }

            Rect sourcePosition = sourceBrowser.position;
            float width = Mathf.Max(560f, sourcePosition.width);
            float height = Mathf.Max(320f, sourcePosition.height);
            browser.position = new Rect(
                sourcePosition.x + 24f,
                sourcePosition.y + 24f,
                width,
                height);
        }

        private static void ApplyResourceWindowTitle(
            EditorWindow browser,
            string folderPath)
        {
            GUIContent projectIcon =
                EditorGUIUtility.IconContent("Project");
            browser.titleContent = new GUIContent(
                "Project \u00b7 \u8d44\u6e90",
                projectIcon == null ? null : projectIcon.image,
                "\u5df2\u9501\u5b9a\uff1a" + folderPath);
        }

        private static void ShowConfigurationFailure(
            EditorWindow browser,
            string folderPath,
            Exception exception)
        {
            Exception rootException =
                exception is TargetInvocationException
                && exception.InnerException != null
                    ? exception.InnerException
                    : exception;

            if (browser != null)
            {
                browser.Repaint();
            }

            Debug.LogWarning(
                "[DualProjectBrowser] "
                + "\u7b2c\u4e8c\u4e2a Project \u7a97\u53e3\u5df2\u5c1d\u8bd5\u6253\u5f00\uff0c"
                + "\u4f46\u672a\u80fd\u81ea\u52a8\u5b9a\u4f4d\u6216\u9501\u5b9a\u3002\n"
                + rootException.GetType().Name
                + ": "
                + rootException.Message);

            EditorUtility.DisplayDialog(
                "Project \u8d44\u6e90\u7a97\u53e3\u9700\u8981\u624b\u52a8\u786e\u8ba4",
                "\u7b2c\u4e8c\u4e2a Project \u7a97\u53e3\u5df2\u5c1d\u8bd5\u6253\u5f00\uff0c"
                + "\u4f46\u672a\u80fd\u81ea\u52a8\u5b8c\u6210\u4ee5\u4e0b\u64cd\u4f5c\uff1a\n\n"
                + "\u6587\u4ef6\u5939\uff1a"
                + folderPath
                + "\n\u9501\u5b9a\uff1a\u6253\u5f00\n\n"
                + "\u8bf7\u5728\u65b0\u7a97\u53e3\u4e2d\u624b\u52a8\u8fdb\u5165\u8be5\u6587\u4ef6\u5939\uff0c"
                + "\u7136\u540e\u70b9\u51fb\u53f3\u4e0a\u89d2\u9501\u5934\u3002"
                + "\n\u539f\u6709 Project \u7a97\u53e3\u672a\u88ab\u4fee\u6539\u3002",
                "\u77e5\u9053\u4e86");
        }
    }
}
