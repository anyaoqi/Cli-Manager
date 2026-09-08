namespace CliManager.Core.Detection;

/// <summary>
/// CLI 预设模板规则。
/// </summary>
public sealed class CliPreset
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string[] MatchKeywords { get; set; } = [];

    public string RecommendedHost { get; set; } = "wt";

    public List<string> DefaultArgs { get; set; } = [];

    public bool KeepOpen { get; set; } = false;

    public string? FallbackIconResource { get; set; }
}

/// <summary>
/// 内置预设注册表。
/// </summary>
public static class CliPresetRegistry
{
    public static readonly IReadOnlyList<CliPreset> Presets =
    [
        new CliPreset
        {
            Id = "claude",
            DisplayName = "Claude Code",
            MatchKeywords = ["claude", "claude.exe"],
            RecommendedHost = "wt",
            DefaultArgs = []
        },
        new CliPreset
        {
            Id = "opencode",
            DisplayName = "OpenCode",
            MatchKeywords = ["opencode", "opencode.exe", "opencode.cmd"],
            RecommendedHost = "wt",
            DefaultArgs = []
        },
        new CliPreset
        {
            Id = "codex",
            DisplayName = "Codex CLI",
            MatchKeywords = ["codex", "codex.cmd", "codex.exe"],
            RecommendedHost = "wt",
            DefaultArgs = []
        },
        new CliPreset
        {
            Id = "kimi",
            DisplayName = "Kimi CLI",
            MatchKeywords = ["kimi", "kimi.exe", "kimi.cmd"],
            RecommendedHost = "wt",
            DefaultArgs = []
        },
        new CliPreset
        {
            Id = "agy",
            DisplayName = "Antigravity CLI",
            MatchKeywords = ["agy", "agy.exe", "agy.cmd"],
            RecommendedHost = "wt",
            DefaultArgs = []
        },
        new CliPreset
        {
            Id = "gemini",
            DisplayName = "Gemini CLI",
            MatchKeywords = ["gemini", "gemini.exe", "gemini.cmd"],
            RecommendedHost = "wt",
            DefaultArgs = []
        }
    ];

    public static CliPreset? FindMatch(string fileNameOrPath)
    {
        string fileName = Path.GetFileName(fileNameOrPath).ToLowerInvariant();
        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileNameOrPath).ToLowerInvariant();

        foreach (var preset in Presets)
        {
            foreach (var kw in preset.MatchKeywords)
            {
                if (string.Equals(fileName, kw, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(nameWithoutExt, kw, StringComparison.OrdinalIgnoreCase))
                {
                    return preset;
                }
            }
        }

        return null;
    }
}
