namespace RightMenu.Core.Discovery;

/// <summary>内置 CLI 目录：只提供候选名称与别名，不硬编码安装位置（规划 02 §6）。</summary>
public sealed record CliCatalogEntry(string DisplayName, IReadOnlyList<string> Aliases);

public static class CliCatalog
{
    public static IReadOnlyList<CliCatalogEntry> KnownClis { get; } =
    [
        new("Claude Code", ["claude"]),
        new("Codex", ["codex"]),
        new("Gemini CLI", ["gemini"]),
        new("Kiro", ["kiro", "kiro-cli"]),
        new("Antigravity", ["antigravity"]),
        new("OpenCode", ["opencode"]),
    ];
}
