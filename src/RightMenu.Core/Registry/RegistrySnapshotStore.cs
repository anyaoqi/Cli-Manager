using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace RightMenu.Core.Registry;

/// <summary>操作类型。</summary>
public enum RegistryOperationType
{
    Create,
    Edit,
    Disable,
    Enable,
    Delete,
    ConvertToManaged,
    UpgradeLegacy,
    MigrateFromHklm,
    ClassicMenuToggle,
}

/// <summary>可序列化的键快照（值类型按 kind 编码，保证 JSON 往返不失真）。</summary>
public sealed record SnapshotValue(string Name, string Kind, string? Text, string[]? Lines, long? Number, byte[]? Bytes)
{
    public static SnapshotValue From(string name, RegistryValueData data) => data.Kind switch
    {
        RegistryValueKind.MultiString => new(name, "multi", null, (string[]?)data.Value, null, null),
        RegistryValueKind.DWord => new(name, "dword", null, null, Convert.ToInt64(data.Value ?? 0), null),
        RegistryValueKind.QWord => new(name, "qword", null, null, Convert.ToInt64(data.Value ?? 0L), null),
        RegistryValueKind.Binary => new(name, "binary", null, null, null, (byte[]?)data.Value),
        RegistryValueKind.ExpandString => new(name, "expand", data.AsString(), null, null, null),
        _ => new(name, "string", data.AsString(), null, null, null),
    };

    public RegistryValueData ToData() => Kind switch
    {
        "multi" => new(Lines ?? [], RegistryValueKind.MultiString),
        "dword" => new((int)(Number ?? 0), RegistryValueKind.DWord),
        "qword" => new(Number ?? 0L, RegistryValueKind.QWord),
        "binary" => new(Bytes ?? [], RegistryValueKind.Binary),
        "expand" => new(Text ?? "", RegistryValueKind.ExpandString),
        _ => new(Text ?? "", RegistryValueKind.String),
    };
}

public sealed record SnapshotKey(string Name, List<SnapshotValue> Values, List<SnapshotKey> SubKeys)
{
    public static SnapshotKey From(RawRegistryKey key) => new(
        key.Name,
        [.. key.Values.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase).Select(v => SnapshotValue.From(v.Key, v.Value))],
        [.. key.SubKeys.OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).Select(From)]);

    public RawRegistryKey ToRaw() => new()
    {
        Name = Name,
        Values = Values.ToDictionary(v => v.Name, v => v.ToData(), StringComparer.OrdinalIgnoreCase),
        SubKeys = [.. SubKeys.Select(k => k.ToRaw())],
    };

    /// <summary>结构化等价比较（大小写不敏感、顺序无关）。</summary>
    public static bool AreEquivalent(SnapshotKey? a, SnapshotKey? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return JsonSerializer.Serialize(Normalize(a)) == JsonSerializer.Serialize(Normalize(b));
    }

    private static SnapshotKey Normalize(SnapshotKey key) => key with
    {
        Name = key.Name.ToUpperInvariant(),
        Values = [.. key.Values
            .Select(v => v with { Name = v.Name.ToUpperInvariant() })
            .OrderBy(v => v.Name, StringComparer.Ordinal)],
        SubKeys = [.. key.SubKeys.Select(Normalize).OrderBy(k => k.Name, StringComparer.Ordinal)],
    };
}

/// <summary>单次操作记录（规划 02 §7.3）。</summary>
public sealed record OperationRecord
{
    public required string Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required RegistryOperationType Type { get; init; }
    public required RegistryHiveSource Hive { get; init; }

    /// <summary>目标动词键相对 Software\Classes 的路径。</summary>
    public required string RelativePath { get; init; }

    public SnapshotKey? Before { get; init; }
    public SnapshotKey? After { get; init; }
    public string AppVersion { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>进程内单调序号：同一毫秒内多次操作仍能稳定排序，保证撤销链顺序。</summary>
    public long Sequence { get; init; }
}

/// <summary>撤销结果。</summary>
public enum UndoResult
{
    Success,

    /// <summary>目标键当前状态与记录的 after 不一致：其他程序已修改，不静默覆盖。</summary>
    Conflict,

    /// <summary>HKLM 目标：需要提权流程，不在此自动执行。</summary>
    RequiresElevation,
}

/// <summary>
/// 目标动词键级 before/after 快照与撤销（规划 02 §7.3）。
/// 不导出/恢复整棵 shell 树，避免覆盖其他程序的并发写入。
/// </summary>
public sealed class RegistrySnapshotStore(IRegistryAccessor accessor, IRegistryWriter writer, string? storeDirectory = null)
{
    public const int MaxRecords = 50;

    private static long _sequenceCounter = DateTime.UtcNow.Ticks;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory = storeDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RightMenu", "operations");

    /// <summary>捕获目标键当前状态；键不存在返回 null（表示“原先不存在”）。</summary>
    public SnapshotKey? Capture(RegistryHiveSource hive, string relativePath)
    {
        var tree = accessor.ReadTree(hive, relativePath);
        return tree is null ? null : SnapshotKey.From(tree);
    }

    /// <summary>写入操作记录并裁剪到最近 50 次。</summary>
    public OperationRecord Record(
        RegistryOperationType type,
        RegistryHiveSource hive,
        string relativePath,
        SnapshotKey? before,
        SnapshotKey? after,
        string description)
    {
        var record = new OperationRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Timestamp = DateTimeOffset.Now,
            Type = type,
            Hive = hive,
            RelativePath = relativePath,
            Before = before,
            After = after,
            AppVersion = typeof(RegistrySnapshotStore).Assembly.GetName().Version?.ToString() ?? "",
            Description = description,
            Sequence = Interlocked.Increment(ref _sequenceCounter),
        };

        Directory.CreateDirectory(_directory);
        var fileName = $"{record.Sequence:D20}-{record.Id}.json";
        File.WriteAllText(Path.Combine(_directory, fileName), JsonSerializer.Serialize(record, JsonOptions));
        Prune();
        return record;
    }

    /// <summary>按时间倒序列出操作记录。</summary>
    public IReadOnlyList<OperationRecord> List()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var records = new List<OperationRecord>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json").OrderDescending(StringComparer.Ordinal))
        {
            try
            {
                var record = JsonSerializer.Deserialize<OperationRecord>(File.ReadAllText(file), JsonOptions);
                if (record is not null)
                {
                    records.Add(record);
                }
            }
            catch (JsonException)
            {
                // 损坏的记录跳过，不影响其他记录
            }
        }

        return [.. records.OrderByDescending(r => r.Sequence)];
    }

    /// <summary>
    /// 撤销一次操作：只恢复该目标键。
    /// 当前状态与记录 after 不一致时返回 Conflict，不静默覆盖（规划 02 §7.3）。
    /// </summary>
    public UndoResult Undo(OperationRecord record)
    {
        if (record.Hive != RegistryHiveSource.Hkcu)
        {
            return UndoResult.RequiresElevation;
        }

        var current = Capture(record.Hive, record.RelativePath);
        if (!SnapshotKey.AreEquivalent(current, record.After))
        {
            return UndoResult.Conflict;
        }

        writer.DeleteTree(record.Hive, record.RelativePath);
        if (record.Before is not null)
        {
            writer.WriteTree(record.Hive, record.RelativePath, record.Before.ToRaw());
        }

        return UndoResult.Success;
    }

    private void Prune()
    {
        var files = Directory.GetFiles(_directory, "*.json").OrderDescending(StringComparer.Ordinal).ToList();
        foreach (var stale in files.Skip(MaxRecords))
        {
            try
            {
                File.Delete(stale);
            }
            catch (IOException)
            {
                // 清理失败不影响主流程
            }
        }
    }
}
