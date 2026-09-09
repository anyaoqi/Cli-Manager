using System.Text;

namespace CliManager.Core.Registry;

/// <summary>
/// 键名生成与加权排序辅助器。
/// 规则：{Order:D3}_{SafeIdentifier}，保证字典序严格等于界面拖拽排序。
/// </summary>
public static class KeyNameHelper
{
    /// <summary>
    /// 生成符合加权排序规范的注册表键名。
    /// </summary>
    /// <param name="order">排序序号（如 10, 20...）</param>
    /// <param name="id">GUID 或唯一 ID</param>
    /// <param name="name">显示名称（用于生成助记前缀）</param>
    /// <param name="width">数字填充宽度（默认 3 位）</param>
    public static string GenerateKeyName(int order, string id, string? name = null, int width = 3)
    {
        string safeOrder = order.ToString().PadLeft(width, '0');
        string safeSlug = GenerateSafeSlug(name, id);
        return $"{safeOrder}_{safeSlug}";
    }

    /// <summary>
    /// 生成仅包含 ASCII 字母数字与下划线的安全 slug。
    /// </summary>
    public static string GenerateSafeSlug(string? name, string id)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (char c in name)
            {
                if (char.IsAsciiLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else if (c is '_' or '-' or ' ')
                {
                    sb.Append('_');
                }
            }
        }

        // 限制 slug 长度，避免注册表键名过长导致 Windows Explorer 截断/放弃枚举 (64 字符限制)
        if (sb.Length > 24)
        {
            sb.Length = 24;
        }

        // 提取 ID 的前 8 位短标识保证全局唯一
        string shortId = id.Replace("-", "");
        if (shortId.Length > 8)
        {
            shortId = shortId[..8];
        }

        if (sb.Length == 0)
        {
            return $"item_{shortId}";
        }

        return $"{sb}_{shortId}";
    }
}
