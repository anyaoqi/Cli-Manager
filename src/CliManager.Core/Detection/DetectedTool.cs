namespace CliManager.Core.Detection;

/// <summary>
/// 本地探测发现的 CLI 工具项。
/// </summary>
public sealed class DetectedTool
{
    public string Name { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty; // "local_bin" | "npm" | "programs" | "path"

    public string? MatchedPresetId { get; set; }

    public string? RecommendedDisplayName { get; set; }

    public string? RecommendedHost { get; set; }
}
