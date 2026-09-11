using CliManager.Core.Models;
using CliManager.Core.Registry;

namespace CliManager.App.Services;

/// <summary>
/// 配置文件读写服务，遵守 §3.5.3 便携模式与安装模式双模式规范。
/// </summary>
public static class ConfigStorageService
{
    private const string ConfigFileName = "config.json";
    private const string AppDirName = "CliManager";

    /// <summary>
    /// 获取当前生效的配置文件路径。
    /// 优先级：如果当前 exe 目录下存在 config.json，则视为便携模式；否则使用 %APPDATA%\CliManager\config.json。
    /// </summary>
    public static string GetConfigFilePath()
    {
        string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
        if (File.Exists(localPath))
        {
            return localPath;
        }

        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDirName);

        Directory.CreateDirectory(appDataDir);
        return Path.Combine(appDataDir, ConfigFileName);
    }

    /// <summary>
    /// 获取当前备份存储目录。
    /// </summary>
    public static string GetBackupDirectory()
    {
        string configPath = GetConfigFilePath();
        string baseDir = Path.GetDirectoryName(configPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        string backupDir = Path.Combine(baseDir, "backups");
        Directory.CreateDirectory(backupDir);
        return backupDir;
    }

    /// <summary>
    /// 获取网站 favicon 图标缓存目录（与配置文件同级的 icons 子目录，便携模式随程序目录走）。
    /// </summary>
    public static string GetIconCacheDirectory()
    {
        string configPath = GetConfigFilePath();
        string baseDir = Path.GetDirectoryName(configPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        string iconDir = Path.Combine(baseDir, "icons");
        Directory.CreateDirectory(iconDir);
        return iconDir;
    }

    /// <summary>
    /// 加载配置。
    /// </summary>
    public static CliConfig LoadConfig()
    {
        string path = GetConfigFilePath();
        if (!File.Exists(path))
        {
            return new CliConfig();
        }

        try
        {
            string json = File.ReadAllText(path);
            var config = CliConfig.FromJson(json);
            if (SanitizeConfig(config))
            {
                SaveConfig(config);
            }
            return config;
        }
        catch
        {
            return new CliConfig();
        }
    }

    private static bool SanitizeConfig(CliConfig config)
    {
        bool changed = false;

        foreach (var tool in config.Tools)
        {
            // 修复未解析的 MUI 间接资源字符串（如 @VSLauncherUI.dll,-1002）
            if (tool.Name.StartsWith('@'))
            {
                string resolved = IndirectStringResolver.Resolve(tool.Name, fallback: "Visual Studio");
                if (!resolved.Equals(tool.Name, StringComparison.Ordinal))
                {
                    tool.Name = resolved;
                    changed = true;
                }
            }

            // 针对已知存量 GUI / 常用工具自动升级为精确模板，避免黑窗口闪烁或参数丢失
            if (string.IsNullOrWhiteSpace(tool.CustomTemplate) && !string.IsNullOrWhiteSpace(tool.Executable))
            {
                if (tool.Executable.EndsWith("Cursor.exe", StringComparison.OrdinalIgnoreCase))
                {
                    tool.Host = TerminalHosts.Custom;
                    tool.CustomTemplate = "\"%EXE%\" \"%V\"";
                    changed = true;
                }
                else if (tool.Executable.EndsWith("Trae.exe", StringComparison.OrdinalIgnoreCase))
                {
                    tool.Host = TerminalHosts.Custom;
                    tool.CustomTemplate = "\"%EXE%\" \"%V\"";
                    changed = true;
                }
                else if (tool.Executable.EndsWith("Cmder.exe", StringComparison.OrdinalIgnoreCase))
                {
                    tool.Host = TerminalHosts.Custom;
                    tool.CustomTemplate = "\"%EXE%\" \"%V\"";
                    changed = true;
                }
                else if (tool.Executable.EndsWith("VSLauncher.exe", StringComparison.OrdinalIgnoreCase))
                {
                    tool.Host = TerminalHosts.Custom;
                    tool.CustomTemplate = "\"%EXE%\" \"%V\" source:ExplorerBackground";
                    changed = true;
                }
                else if (tool.Executable.EndsWith("warp.exe", StringComparison.OrdinalIgnoreCase))
                {
                    tool.Host = TerminalHosts.Custom;
                    tool.CustomTemplate = tool.Name.Contains("window", StringComparison.OrdinalIgnoreCase)
                        ? "\"%EXE%\" \"Warp://action/new_window?path=%V\""
                        : "\"%EXE%\" \"Warp://action/new_tab?path=%V\"";
                    changed = true;
                }
                else if (tool.Executable.EndsWith("git-gui.exe", StringComparison.OrdinalIgnoreCase))
                {
                    tool.Host = TerminalHosts.Custom;
                    tool.CustomTemplate = "\"%EXE%\" \"--working-dir\" \"%v.\"";
                    changed = true;
                }
                else if (tool.Executable.EndsWith("git-bash.exe", StringComparison.OrdinalIgnoreCase))
                {
                    tool.Host = TerminalHosts.Custom;
                    tool.CustomTemplate = "\"%EXE%\" \"--cd=%v.\"";
                    changed = true;
                }
            }
        }

        foreach (var folder in config.Folders)
        {
            if (folder.Name.StartsWith('@'))
            {
                string resolved = IndirectStringResolver.Resolve(folder.Name, fallback: "Folder");
                if (!resolved.Equals(folder.Name, StringComparison.Ordinal))
                {
                    folder.Name = resolved;
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// 保存配置到磁盘。
    /// </summary>
    public static void SaveConfig(CliConfig config)
    {
        string path = GetConfigFilePath();
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = config.ToJson();
        File.WriteAllText(path, json, System.Text.Encoding.UTF8);
    }
}
