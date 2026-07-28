using System.Runtime.InteropServices;
using System.Text;

namespace RightMenu.Core.Registry;

/// <summary>
/// 展开 @dll,-id 形式的注册表间接显示名（规划 04 Phase 1）。
/// 失败时返回原字符串，不抛异常。
/// </summary>
public static class IndirectStringResolver
{
    public static string Resolve(string source)
    {
        if (!source.StartsWith('@'))
        {
            return source;
        }

        var buffer = new StringBuilder(512);
        var hr = SHLoadIndirectString(source, buffer, buffer.Capacity, IntPtr.Zero);
        return hr == 0 && buffer.Length > 0 ? buffer.ToString() : source;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);
}
