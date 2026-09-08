using CliManager.Core.Models;

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
            return CliConfig.FromJson(json);
        }
        catch
        {
            return new CliConfig();
        }
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
