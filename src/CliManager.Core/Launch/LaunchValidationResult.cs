namespace CliManager.Core.Launch;

/// <summary>
/// 校验与编译结果。
/// </summary>
public sealed class LaunchValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public void AddError(string error) => Errors.Add(error);

    public void AddWarning(string warning) => Warnings.Add(warning);
}
