namespace RightMenu.Core.Launching;

/// <summary>终端选择（规划 02 §3.2）。</summary>
public enum TerminalKind
{
    /// <summary>优先 Windows Terminal，不可用时经典控制台。</summary>
    Auto,

    /// <summary>强制 Windows Terminal。</summary>
    WindowsTerminal,

    /// <summary>经典控制台。</summary>
    Console,
}

/// <summary>
/// 受管 CLI 启动规格（规划 02 §3.2）。
/// 参数按数组存储，不以 raw shell string 作为主数据，避免再次解析引号。
/// </summary>
public sealed record LaunchSpec
{
    public required string ExecutablePath { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public TerminalKind Terminal { get; init; } = TerminalKind.Auto;

    public bool KeepOpenAfterExit { get; init; } = true;

    public static string TerminalToString(TerminalKind kind) => kind switch
    {
        TerminalKind.WindowsTerminal => "wt",
        TerminalKind.Console => "console",
        _ => "auto",
    };

    public static TerminalKind TerminalFromString(string? value) => value switch
    {
        "wt" => TerminalKind.WindowsTerminal,
        "console" => TerminalKind.Console,
        _ => TerminalKind.Auto,
    };
}
