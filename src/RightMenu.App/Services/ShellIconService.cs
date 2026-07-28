using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RightMenu.App.Services;

/// <summary>
/// 从 IconSpec（"path,index" 或 path）提取 16-20px 图标。
/// .cmd/.bat 等无图标资源时回退关联图标；全部失败返回 null，UI 显示默认占位
/// （规划 02 §10 图标回退，不显示破图）。
/// </summary>
public static class ShellIconService
{
    public static ImageSource? Load(string iconSpec, string commandFallbackPath)
    {
        var source = LoadFromSpec(iconSpec) ?? LoadAssociated(commandFallbackPath);
        source?.Freeze();
        return source;
    }

    private static BitmapSource? LoadFromSpec(string iconSpec)
    {
        if (string.IsNullOrWhiteSpace(iconSpec))
        {
            return null;
        }

        var (path, index) = ParseSpec(iconSpec.Trim().Trim('"'));
        path = Environment.ExpandEnvironmentVariables(path);
        if (!File.Exists(path))
        {
            return null;
        }

        var hIcon = ExtractIcon(path, index);
        return hIcon == IntPtr.Zero ? LoadAssociated(path) : FromHIcon(hIcon);
    }

    private static BitmapSource? LoadAssociated(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var info = new SHFILEINFO();
        var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_ICON | SHGFI_SMALLICON);
        return result == IntPtr.Zero || info.hIcon == IntPtr.Zero ? null : FromHIcon(info.hIcon);
    }

    private static (string Path, int Index) ParseSpec(string spec)
    {
        var comma = spec.LastIndexOf(',');
        if (comma > 1 && int.TryParse(spec[(comma + 1)..], out var index))
        {
            return (spec[..comma].Trim().Trim('"'), index);
        }

        return (spec, 0);
    }

    private static IntPtr ExtractIcon(string path, int index)
    {
        IntPtr[] small = new IntPtr[1];
        var count = ExtractIconEx(path, index, null, small, 1);
        return count > 0 ? small[0] : IntPtr.Zero;
    }

    private static BitmapSource? FromHIcon(IntPtr hIcon)
    {
        try
        {
            return Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
