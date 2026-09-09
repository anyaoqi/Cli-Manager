namespace CliManager.Core.Models;

/// <summary>
/// 终端宿主类型。
/// </summary>
public static class TerminalHosts
{
    public const string WindowsTerminal = "wt";
    public const string Cmd = "cmd";
    public const string WindowsPowerShell = "powershell";
    public const string PowerShell7 = "pwsh";
    public const string Custom = "custom";

    public static readonly string[] All = [WindowsTerminal, Cmd, WindowsPowerShell, PowerShell7, Custom];

    public static bool IsValid(string host) =>
        host is WindowsTerminal or Cmd or WindowsPowerShell or PowerShell7 or Custom;
}
