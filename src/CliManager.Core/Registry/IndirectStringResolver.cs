using System.Runtime.InteropServices;
using System.Text;

namespace CliManager.Core.Registry;

/// <summary>
/// 解析 Windows MUI 间接资源字符串（如 @shell32.dll,-8506 或 @dllpath,-resId）。
/// </summary>
public static class IndirectStringResolver
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(
        string pszSource,
        StringBuilder pszOutBuf,
        uint cchOutBuf,
        IntPtr ppvReserved);

    /// <summary>
    /// 将间接字符串解析为用户可读的本地化文本。若非间接字符串或解析失败，返回回退文本。
    /// </summary>
    public static string Resolve(string? source, string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return fallback ?? string.Empty;
        }

        string trimmed = source.Trim();
        if (!trimmed.StartsWith('@'))
        {
            return trimmed;
        }

        try
        {
            var sb = new StringBuilder(1024);
            int hr = SHLoadIndirectString(trimmed, sb, (uint)sb.Capacity, IntPtr.Zero);
            if (hr == 0 && sb.Length > 0)
            {
                string resolved = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(resolved) && !resolved.StartsWith('@'))
                {
                    return resolved;
                }
            }
        }
        catch
        {
            // 忽略非托管调用异常
        }

        // 若解析失败且有备选名称（如 keyName），优先使用备选名称
        if (!string.IsNullOrWhiteSpace(fallback) && !fallback.StartsWith('@'))
        {
            return fallback.Trim();
        }

        // 避免返回未解析且可能含有非法路径字符的 @ 字符串
        return trimmed.TrimStart('@');
    }
}
