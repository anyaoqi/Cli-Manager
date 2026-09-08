using System.Diagnostics;

namespace CliManager.Core.Detection;

/// <summary>
/// 本地已安装 CLI 工具与终端探测服务。
/// 遵守 §3.4.1：全部由环境变量与标准机制推导，不硬编码个人私有路径。
/// </summary>
public static class CliDiscoveryService
{
    private static readonly string[] ExecutableExtensions = [".exe", ".cmd", ".bat", ".ps1"];

    /// <summary>
    /// 探测 Windows Terminal (wt.exe) 的实际绝对路径。
    /// </summary>
    public static string? DetectWindowsTerminalPath()
    {
        // 1. 检查微软商店应用标准别名路径
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string defaultAlias = Path.Combine(localAppData, @"Microsoft\WindowsApps\wt.exe");
        if (File.Exists(defaultAlias))
        {
            return defaultAlias;
        }

        // 2. 检查系统 PATH
        return FindInPath("wt.exe");
    }

    /// <summary>
    /// 探测 PowerShell 7 (pwsh.exe) 是否可用。
    /// </summary>
    public static string? DetectPowerShell7Path()
    {
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string defaultPwsh = Path.Combine(progFiles, @"PowerShell\7\pwsh.exe");
        if (File.Exists(defaultPwsh))
        {
            return defaultPwsh;
        }

        return FindInPath("pwsh.exe");
    }

    /// <summary>
    /// 全面扫描本机已安装的 CLI 工具（特别针对 AI CLI）。
    /// </summary>
    public static List<DetectedTool> DiscoverInstalledTools()
    {
        var result = new List<DetectedTool>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. 探测 ~/.local/bin（Claude Code 等默认目录）
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localBin = Path.Combine(userProfile, @".local\bin");
        ScanDirectory(localBin, "local_bin", result, seenPaths);

        // 2. 探测全局 npm / pnpm bin 目录
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string npmGlobal = Path.Combine(appData, "npm");
        ScanDirectory(npmGlobal, "npm", result, seenPaths);

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pnpmGlobal = Path.Combine(localAppData, "pnpm");
        ScanDirectory(pnpmGlobal, "npm", result, seenPaths);

        // 3. 探测用户 PATH 中的预设匹配项
        ScanPathEnvironment(result, seenPaths);

        return result;
    }

    private static void ScanDirectory(
        string directoryPath,
        string sourceTag,
        List<DetectedTool> result,
        HashSet<string> seenPaths)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            var files = Directory.GetFiles(directoryPath);
            foreach (var file in files)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (!ExecutableExtensions.Contains(ext))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(file);
                if (seenPaths.Add(fullPath))
                {
                    var preset = CliPresetRegistry.FindMatch(file);
                    string name = preset != null ? preset.DisplayName : Path.GetFileNameWithoutExtension(file);

                    result.Add(new DetectedTool
                    {
                        Name = name,
                        ExecutablePath = fullPath,
                        Source = sourceTag,
                        MatchedPresetId = preset?.Id,
                        RecommendedDisplayName = preset?.DisplayName ?? name,
                        RecommendedHost = preset?.RecommendedHost ?? "wt"
                    });
                }
            }
        }
        catch
        {
            // 忽略权限等非致命扫描异常
        }
    }

    private static void ScanPathEnvironment(List<DetectedTool> result, HashSet<string> seenPaths)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return;
        }

        string[] dirs = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string dir in dirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            try
            {
                // 只查找与内置预设匹配的项，避免把系统上千个通用可执行文件全部列出
                foreach (var preset in CliPresetRegistry.Presets)
                {
                    foreach (var kw in preset.MatchKeywords)
                    {
                        string candidate = Path.Combine(dir, kw);
                        if (File.Exists(candidate))
                        {
                            string fullPath = Path.GetFullPath(candidate);
                            if (seenPaths.Add(fullPath))
                            {
                                result.Add(new DetectedTool
                                {
                                    Name = preset.DisplayName,
                                    ExecutablePath = fullPath,
                                    Source = "path",
                                    MatchedPresetId = preset.Id,
                                    RecommendedDisplayName = preset.DisplayName,
                                    RecommendedHost = preset.RecommendedHost
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
                // 忽略非致命异常
            }
        }
    }

    private static string? FindInPath(string fileName)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        string[] dirs = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string dir in dirs)
        {
            try
            {
                string fullPath = Path.Combine(dir, fileName);
                if (File.Exists(fullPath))
                {
                    return Path.GetFullPath(fullPath);
                }
            }
            catch
            {
                // 忽略
            }
        }

        return null;
    }
}
