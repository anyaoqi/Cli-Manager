namespace CliManager.Core.Detection;

/// <summary>
/// 本地已安装 CLI 工具与终端探测服务。
/// 遵守 §3.4.1：全部由环境变量与标准机制推导，不硬编码个人私有路径。
/// 严格过滤无扩展名 Linux/bash 脚本，按 .exe > .cmd > .bat > .ps1 优先级去重。
/// 扫描范围包含 Node.js 安装目录内的全部 .cmd/.exe 工具（无论是否为 AI CLI）。
/// </summary>
public static class CliDiscoveryService
{
    public static readonly string[] ExecutableExtensions = [".exe", ".cmd", ".bat", ".ps1"];

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
    /// 自动排除无扩展名脚本，按 .exe > .cmd > .bat > .ps1 优先级去重。
    /// </summary>
    public static List<DetectedTool> DiscoverInstalledTools()
    {
        var rawTools = new List<DetectedTool>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. 探测 ~/.local/bin（Claude Code 等默认目录）
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localBin = Path.Combine(userProfile, @".local\bin");
        ScanDirectory(localBin, "local_bin", rawTools, seenPaths);

        // 2. 探测全局 npm / pnpm bin 目录
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string npmGlobal = Path.Combine(appData, "npm");
        ScanDirectory(npmGlobal, "npm", rawTools, seenPaths);

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pnpmGlobal = Path.Combine(localAppData, "pnpm");
        ScanDirectory(pnpmGlobal, "npm", rawTools, seenPaths);

        // 3. 探测 Node.js 安装目录（npm 全局 .cmd 垫片的另一落盘位置，如自定义安装的 D:\Software\nodejs）
        string? nodeDir = DetectNodeInstallDirectory();
        if (nodeDir != null)
        {
            ScanDirectory(nodeDir, "node", rawTools, seenPaths);
        }

        // 4. 探测用户 PATH 中的预设匹配项
        ScanPathEnvironment(rawTools, seenPaths);

        // 5. 全局归一化与去重优选
        return DeduplicateDetectedTools(rawTools);
    }

    /// <summary>
    /// 探测 Node.js 安装目录（node.exe 所在目录）。
    /// 优先从 PATH 解析（覆盖自定义安装位置与 nvm 链接目录），其次读取官方安装包写入的注册表 InstallPath。
    /// </summary>
    public static string? DetectNodeInstallDirectory()
    {
        // 1. PATH 中的 node.exe：可定位任意自定义安装目录（如 D:\Software\nodejs）
        string? pathNode = FindInPath("node.exe");
        if (pathNode != null)
        {
            return Path.GetDirectoryName(Path.GetFullPath(pathNode));
        }

        // 2. 官方 Node.js 安装包会写入 HKLM/HKCU SOFTWARE\Node.js 的 InstallPath
        foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Node.js");
                if (key?.GetValue("InstallPath") is string installPath && Directory.Exists(installPath))
                {
                    return installPath;
                }
            }
            catch
            {
                // 忽略非特权/非致命异常
            }
        }

        return null;
    }

    /// <summary>
    /// 扫描单个目录下的全部 Windows 可执行工具（同名按 .exe > .cmd > .bat > .ps1 去重）。
    /// </summary>
    public static List<DetectedTool> ScanDirectoryForExecutables(string directoryPath, string sourceTag)
    {
        var result = new List<DetectedTool>();
        ScanDirectory(directoryPath, sourceTag, result, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
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
            // 按基础文件名分组，仅选取有效 Windows 可执行扩展名
            var fileGroups = files
                .Select(f => new
                {
                    FullPath = Path.GetFullPath(f),
                    BaseName = Path.GetFileNameWithoutExtension(f),
                    Extension = Path.GetExtension(f).ToLowerInvariant()
                })
                .Where(f => ExecutableExtensions.Contains(f.Extension))
                .GroupBy(f => f.BaseName, StringComparer.OrdinalIgnoreCase);

            foreach (var group in fileGroups)
            {
                // 优先级排序：.exe (0) > .cmd (1) > .bat (2) > .ps1 (3)
                var best = group.OrderBy(f => Array.IndexOf(ExecutableExtensions, f.Extension)).First();

                if (seenPaths.Add(best.FullPath))
                {
                    var preset = CliPresetRegistry.FindMatch(best.FullPath);
                    string name = preset != null ? preset.DisplayName : best.BaseName;

                    result.Add(new DetectedTool
                    {
                        Name = name,
                        ExecutablePath = best.FullPath,
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
                    var baseNames = preset.MatchKeywords
                        .Select(Path.GetFileNameWithoutExtension)
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                    foreach (var baseName in baseNames)
                    {
                        // 优先级匹配：.exe > .cmd > .bat > .ps1（严格要求必须带有效扩展名，忽略无扩展名 bash 脚本）
                        string? matchedFile = null;
                        foreach (var ext in ExecutableExtensions)
                        {
                            string candidate = Path.Combine(dir, baseName + ext);
                            if (File.Exists(candidate))
                            {
                                matchedFile = candidate;
                                break;
                            }
                        }

                        if (matchedFile != null)
                        {
                            string fullPath = Path.GetFullPath(matchedFile);
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

    /// <summary>
    /// 全局工具去重与最佳候选择优。
    /// 确保同一个工具（相同 PresetId 或相同名称）只保留 1 个最佳可执行文件。
    /// </summary>
    public static List<DetectedTool> DeduplicateDetectedTools(IEnumerable<DetectedTool> tools)
    {
        var deduplicated = new List<DetectedTool>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int GetExtRank(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            int idx = Array.IndexOf(ExecutableExtensions, ext);
            return idx >= 0 ? idx : 99;
        }

        int GetSourceRank(string source) => source switch
        {
            "local_bin" => 0,
            "npm" or "node" => 1,
            _ => 2
        };

        var sorted = tools
            .Where(t =>
            {
                // 必须具有有效扩展名且路径非空
                string ext = Path.GetExtension(t.ExecutablePath).ToLowerInvariant();
                return ExecutableExtensions.Contains(ext);
            })
            .OrderBy(t => t.MatchedPresetId == null ? 1 : 0)
            .ThenBy(t => GetExtRank(t.ExecutablePath))
            .ThenBy(t => GetSourceRank(t.Source))
            .ToList();

        foreach (var tool in sorted)
        {
            // 去重键：优先使用预设 ID，其次使用规范化名称
            string key = !string.IsNullOrEmpty(tool.MatchedPresetId)
                ? $"preset:{tool.MatchedPresetId}"
                : $"name:{(tool.RecommendedDisplayName ?? tool.Name).Trim()}";

            if (seenKeys.Add(key))
            {
                deduplicated.Add(tool);
            }
        }

        return deduplicated;
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
