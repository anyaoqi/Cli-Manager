namespace CliManager.Core.Registry;

/// <summary>
/// 注册表同步执行结果。
/// </summary>
public sealed class RegistrySyncResult
{
    public bool Success { get; set; } = true;

    public int AddedOrUpdatedCount { get; set; }

    public int DeletedCount { get; set; }

    public string? BackupFilePath { get; set; }

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];
}
