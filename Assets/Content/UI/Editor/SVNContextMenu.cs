using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;

// SVN Context Menu
// 在 Unity Project 视图选中 Assets 文件或文件夹后，右键 SVN 调用 TortoiseSVN。
// 菜单：提交 Commit、更新 Update、还原 Revert、查看日志 Show Log、配置 SVN 路径。
// 依赖：本机安装 TortoiseSVN；路径不一致时先用“配置 SVN 路径”选择 TortoiseProc.exe。
// 注意：本工具只打开 TortoiseSVN 操作窗口，不会自动提交或推送。
public class SVNContextMenu
{
    // Personal development record. Do not expose this version in Unity menus.
    private const string TOOL_VERSION = "1.0.0";

    // 保存 TortoiseSVN 路径
    private const string TORTOISE_PROC_PATH_KEY = "Tortoise_Proc_Path";
    private static string TortoiseProcPath
    {
        get => EditorPrefs.GetString(TORTOISE_PROC_PATH_KEY, @"D:\TortoiseSVN\bin\TortoiseProc.exe");
        set => EditorPrefs.SetString(TORTOISE_PROC_PATH_KEY, value);
    }

    // ------------------------------
    // 核心：调整菜单优先级，数字越大位置越靠下
    // 把SVN主菜单优先级设为150，让它排到菜单中间位置
    // ------------------------------
    [MenuItem("Assets/SVN/提交 Commit", false, 150)]
    private static void CommitWithTortoise()
    {
        if (!ValidateTortoiseProc()) return;

        List<string> selectedPaths = GetSelectedAssetPaths();
        if (selectedPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先选中要提交的文件/文件夹！", "确定");
            return;
        }

        string paths = string.Join("*", selectedPaths.Select(p => $"\"{p}\""));
        string command = $"/command:commit /path:{paths} /closeonend:0";
        ExecuteTortoiseCommand(command);
    }

    [MenuItem("Assets/SVN/更新 Update", false, 151)]
    private static void UpdateWithTortoise()
    {
        if (!ValidateTortoiseProc()) return;

        List<string> selectedPaths = GetSelectedAssetPaths();
        if (selectedPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先选中要更新的文件/文件夹！", "确定");
            return;
        }

        string paths = string.Join("*", selectedPaths.Select(p => $"\"{p}\""));
        string command = $"/command:update /path:{paths} /closeonend:0";
        ExecuteTortoiseCommand(command);
    }

    [MenuItem("Assets/SVN/还原 Revert", false, 152)]
    private static void RevertWithTortoise()
    {
        if (!ValidateTortoiseProc()) return;

        List<string> selectedPaths = GetSelectedAssetPaths();
        if (selectedPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先选中要还原的文件/文件夹！", "确定");
            return;
        }

        string paths = string.Join("*", selectedPaths.Select(p => $"\"{p}\""));
        string command = $"/command:revert /path:{paths} /closeonend:0";
        ExecuteTortoiseCommand(command);
    }

    // ------------------------------
    // 新增：Show Log 功能
    // ------------------------------
    [MenuItem("Assets/SVN/查看日志 Show Log", false, 153)]
    private static void ShowLogWithTortoise()
    {
        if (!ValidateTortoiseProc()) return;

        List<string> selectedPaths = GetSelectedAssetPaths();
        if (selectedPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先选中要查看日志的文件/文件夹！", "确定");
            return;
        }

        string paths = string.Join("*", selectedPaths.Select(p => $"\"{p}\""));
        string command = $"/command:log /path:{paths} /closeonend:0";
        ExecuteTortoiseCommand(command);
    }

    [MenuItem("Assets/SVN/配置 SVN 路径", false, 159)]
    private static void ConfigureTortoisePath()
    {
        string newPath = EditorUtility.OpenFilePanel("选择 TortoiseProc.exe 路径", "", "exe");
        if (!string.IsNullOrEmpty(newPath))
        {
            TortoiseProcPath = newPath;
            EditorUtility.DisplayDialog("成功", "TortoiseSVN 路径已更新为：\n" + newPath, "确定");
        }
    }

    // ------------------------------
    // 验证菜单是否可用
    // ------------------------------
    [MenuItem("Assets/SVN/提交 Commit", true, 150)]
    [MenuItem("Assets/SVN/更新 Update", true, 151)]
    [MenuItem("Assets/SVN/还原 Revert", true, 152)]
    [MenuItem("Assets/SVN/查看日志 Show Log", true, 153)]
    private static bool ValidateMenu()
    {
        return Selection.GetFiltered<Object>(SelectionMode.Assets).Length > 0;
    }

    // ------------------------------
    // 工具方法：获取选中 Assets 的本地路径
    // ------------------------------
    private static List<string> GetSelectedAssetPaths()
    {
        List<string> paths = new List<string>();
        foreach (Object obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
        {
            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                string localPath = Path.Combine(projectRoot, assetPath);
                paths.Add(localPath);
            }
        }
        return paths;
    }

    // ------------------------------
    // 工具方法：执行 TortoiseSVN 命令
    // ------------------------------
    private static void ExecuteTortoiseCommand(string command)
    {
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = TortoiseProcPath;
            process.StartInfo.Arguments = command;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"调用 TortoiseSVN 异常：{e.Message}");
            EditorUtility.DisplayDialog("错误", "无法启动 TortoiseSVN，请检查路径配置！", "确定");
        }
    }

    // ------------------------------
    // 工具方法：验证 TortoiseProc.exe 是否有效
    // ------------------------------
    private static bool ValidateTortoiseProc()
    {
        if (!File.Exists(TortoiseProcPath))
        {
            EditorUtility.DisplayDialog("错误", "TortoiseProc.exe 路径无效！请先配置路径。", "确定");
            ConfigureTortoisePath();
            return false;
        }
        return true;
    }
}
