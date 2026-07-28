using Microsoft.Win32;

namespace RightMenu.Core.Discovery;

/// <summary>单个发现结果。</summary>
public sealed record CliCandidate(string Alias, string FullPath, string SourceDescription);

/// <summary>
/// CLI 发现（规划 02 §6）：合并进程/用户/系统 PATH、PATHEXT、App Paths 与常见用户级 bin 目录。
/// 只做存在性检查，绝不执行候选文件；同名多路径全部返回，由用户选择。
/// </summary>
public sealed class CliDiscoveryService
{
    private static readonly string[] DefaultExtensions = [".exe", ".com", ".cmd", ".bat"];

    /// <summary>查找一个别名的所有候选（按来源优先级排序、全路径去重）。</summary>
    public IReadOnlyList<CliCandidate> FindAll(string alias)
    {
        var results = new List<CliCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extensions = GetPathExtensions();

        foreach (var (dir, source) in EnumerateSearchDirectories())
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, alias + ext);
                if (seen.Add(candidate) && File.Exists(candidate))
                {
                    results.Add(new CliCandidate(alias, candidate, source));
                }
            }
        }

        foreach (var appPath in QueryAppPaths(alias))
        {
            if (seen.Add(appPath))
            {
                results.Add(new CliCandidate(alias, appPath, "App Paths"));
            }
        }

        return results;
    }

    /// <summary>发现目录中所有已知 CLI。</summary>
    public IReadOnlyDictionary<CliCatalogEntry, IReadOnlyList<CliCandidate>> DiscoverKnown()
    {
        var result = new Dictionary<CliCatalogEntry, IReadOnlyList<CliCandidate>>();
        foreach (var entry in CliCatalog.KnownClis)
        {
            var candidates = entry.Aliases
                .SelectMany(FindAll)
                .DistinctBy(c => c.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result[entry] = candidates;
        }

        return result;
    }

    /// <summary>PATHEXT 中的可执行扩展名（限制在支持的四种内）。</summary>
    private static string[] GetPathExtensions()
    {
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExt))
        {
            return DefaultExtensions;
        }

        var fromEnv = pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => DefaultExtensions.Contains(e, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        return fromEnv.Length > 0 ? fromEnv : DefaultExtensions;
    }

    /// <summary>
    /// 合并进程 PATH、用户 PATH（HKCU\Environment）、系统 PATH 并去重；
    /// 兼容 Explorer 尚未刷新环境变量的情况（规划 02 §6）。
    /// 再补充常见用户级 CLI 目录。
    /// </summary>
    private static IEnumerable<(string Dir, string Source)> EnumerateSearchDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in SplitPath(Environment.GetEnvironmentVariable("PATH")))
        {
            if (seen.Add(dir))
            {
                yield return (dir, "PATH");
            }
        }

        foreach (var dir in SplitPath(ReadRegistryPath(Microsoft.Win32.Registry.CurrentUser, "Environment")))
        {
            if (seen.Add(dir))
            {
                yield return (dir, "用户 PATH");
            }
        }

        foreach (var dir in SplitPath(ReadRegistryPath(Microsoft.Win32.Registry.LocalMachine,
            @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")))
        {
            if (seen.Add(dir))
            {
                yield return (dir, "系统 PATH");
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] extraDirs =
        [
            Path.Combine(profile, ".local", "bin"),
            Path.Combine(appData, "npm"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pnpm"),
            Path.Combine(profile, ".bun", "bin"),
        ];
        foreach (var dir in extraDirs)
        {
            if (Directory.Exists(dir) && seen.Add(dir))
            {
                yield return (dir, "用户 bin 目录");
            }
        }
    }

    private static IEnumerable<string> SplitPath(string? path) =>
        (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Environment.ExpandEnvironmentVariables)
            .Where(Directory.Exists);

    private static string? ReadRegistryPath(RegistryKey root, string subKey)
    {
        using var key = root.OpenSubKey(subKey, writable: false);
        // 手动展开：REG_EXPAND_SZ 中可能引用其他环境变量
        return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    /// <summary>HKCU/HKLM App Paths 注册的可执行文件。</summary>
    private static IEnumerable<string> QueryAppPaths(string alias)
    {
        foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{alias}.exe", writable: false);
            if (key?.GetValue(null) is string path)
            {
                path = Environment.ExpandEnvironmentVariables(path.Trim('"'));
                if (File.Exists(path))
                {
                    yield return path;
                }
            }
        }
    }
}
