using System.Text.Json;
using System.Text.Json.Serialization;

namespace CliManager.Core.Models;

/// <summary>
/// 根配置模型（config.json 映射）。
/// </summary>
public sealed class CliConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public int SchemaVersion { get; set; } = 1;

    public AppSettings Settings { get; set; } = new();

    public List<FolderItem> Folders { get; set; } = [];

    public List<ToolItem> Tools { get; set; } = [];

    /// <summary>
    /// 将配置序列化为 JSON 字符串。
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// 从 JSON 字符串反序列化配置，格式错误或空串时返回全新默认配置。
    /// </summary>
    public static CliConfig FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CliConfig();
        }

        try
        {
            var config = JsonSerializer.Deserialize<CliConfig>(json, JsonOptions);
            return config ?? new CliConfig();
        }
        catch
        {
            return new CliConfig();
        }
    }
}
