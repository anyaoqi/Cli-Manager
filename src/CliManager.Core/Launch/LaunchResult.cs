namespace CliManager.Core.Launch;

/// <summary>
/// 编译输出结果。
/// </summary>
public sealed class LaunchResult
{
    public bool Success => Validation.IsValid && !string.IsNullOrEmpty(Command);

    public string? Command { get; set; }

    public LaunchValidationResult Validation { get; } = new();
}
