using System.Diagnostics;
using Microsoft.Win32;

namespace CliManager.App.Services;

/// <summary>
/// Windows 11 经典右键菜单控制服务（ADR D2 & §3.6）。
/// </summary>
public static class ClassicMenuService
{
    private const string ClsidSubKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string InprocServerSubKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    /// <summary>
    /// 检测当前是否已启用 Windows 10 经典右键菜单。
    /// </summary>
    public static bool IsClassicMenuEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(InprocServerSubKey);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 启用 Windows 10 经典右键菜单（写入空字符串 InprocServer32 键）。
    /// </summary>
    public static bool EnableClassicMenu()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(InprocServerSubKey, writable: true);
            key.SetValue("", "", RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 恢复 Windows 11 默认新版右键菜单（删除整个覆盖 CLSID 键）。
    /// </summary>
    public static bool RestoreWin11ModernMenu()
    {
        try
        {
            using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\CLSID", writable: true);
            if (root != null)
            {
                root.DeleteSubKeyTree("{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", throwOnMissingSubKey: false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 重启 Windows 资源管理器 (explorer.exe) 以使菜单设置即时生效。
    /// </summary>
    public static void RestartExplorer()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
                catch
                {
                    // 忽略进程杀除异常
                }
            }
        }
        catch
        {
            // 忽略
        }

        // 重新启动 explorer
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true
        });
    }
}
