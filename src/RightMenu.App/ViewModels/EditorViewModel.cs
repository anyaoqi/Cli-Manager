using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using RightMenu.Core.Discovery;
using RightMenu.Core.Launching;
using RightMenu.Core.Registry;

namespace RightMenu.App.ViewModels;

/// <summary>Inspector 模式（规划 03 §2）。</summary>
public enum EditorMode
{
    /// <summary>新建受管 CLI（CLI-first 首屏为发现列表）。</summary>
    AddCli,

    /// <summary>编辑受管项（结构化字段）。</summary>
    EditManaged,

    /// <summary>编辑简单项（显示名/图标/原始命令）。</summary>
    EditSimple,

    /// <summary>只读查看（HKLM 复杂项等）。</summary>
    ViewOnly,

    /// <summary>可迁移的 HKLM 简单项（只读 + 迁移动作）。</summary>
    Migratable,
}

/// <summary>发现结果行（添加 CLI 首屏）。</summary>
public sealed record DiscoveredCliViewModel(string DisplayName, string? Path, string? Source)
{
    public bool IsFound => Path is not null;

    public string Detail => Path ?? "未找到，可手动浏览选择";
}

/// <summary>Inspector 编辑器：单一面板承担新建、查看与编辑（规划 03 §2）。</summary>
public sealed partial class EditorViewModel : ObservableObject
{
    /// <summary>正在编辑的现有项；AddCli 模式为 null。</summary>
    public VerbItemViewModel? Target { get; }

    public EditorMode Mode { get; }

    public EditorViewModel(EditorMode mode, VerbItemViewModel? target, IReadOnlyList<DiscoveredCliViewModel>? discovered = null)
    {
        Mode = mode;
        Target = target;
        Discovered = discovered ?? [];

        if (target is not null)
        {
            DisplayName = target.DisplayName;
            IconSpec = target.Verb.IconSpec;
            RawCommand = target.Verb.Command;

            if (target.Verb.IsManaged &&
                ManagedEntryService.ParseLaunchSpec(target.Verb.Raw) is { Spec: { } spec })
            {
                ExecutablePath = spec.ExecutablePath;
                ArgumentsText = string.Join(Environment.NewLine, spec.Arguments);
                TerminalIndex = (int)spec.Terminal;
                KeepOpen = spec.KeepOpenAfterExit;
            }
            else
            {
                ExecutablePath = target.CommandExecutablePath;
            }
        }
    }

    public IReadOnlyList<DiscoveredCliViewModel> Discovered { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; } = "";

    [ObservableProperty]
    public partial string ExecutablePath { get; set; } = "";

    /// <summary>参数数组编辑：每行一个参数（高级区）。</summary>
    [ObservableProperty]
    public partial string ArgumentsText { get; set; } = "";

    /// <summary>0=自动 1=Windows Terminal 2=经典控制台。</summary>
    [ObservableProperty]
    public partial int TerminalIndex { get; set; }

    [ObservableProperty]
    public partial bool KeepOpen { get; set; } = true;

    [ObservableProperty]
    public partial string IconSpec { get; set; } = "";

    /// <summary>简单项原始命令（仅 EditSimple 模式可编辑）。</summary>
    [ObservableProperty]
    public partial string RawCommand { get; set; } = "";

    [ObservableProperty]
    public partial string? ValidationError { get; set; }

    public bool IsEditable => Mode is EditorMode.AddCli or EditorMode.EditManaged or EditorMode.EditSimple;

    public bool IsManagedForm => Mode is EditorMode.AddCli or EditorMode.EditManaged;

    public bool IsSimpleForm => Mode is EditorMode.EditSimple;

    public bool ShowDiscovery => Mode is EditorMode.AddCli;

    public bool ShowMigrate => Mode is EditorMode.Migratable;

    public bool ShowUpgrade => Target?.IsLegacyManaged == true && Mode is EditorMode.EditSimple;

    public string Title => Mode switch
    {
        EditorMode.AddCli => "添加 CLI",
        EditorMode.EditManaged => "编辑",
        EditorMode.EditSimple => "编辑",
        EditorMode.Migratable => "系统项",
        _ => "详情",
    };

    /// <summary>只读的最终启动说明（同一编译实现，规划 02 §5）。</summary>
    public string CommandPreview => IsManagedForm
        ? (ExecutablePath.Length == 0
            ? ""
            : $"通过 Windows Terminal 在目标目录运行\n{ExecutablePath}{FormatArgsPreview()}")
        : RawCommand;

    private string FormatArgsPreview()
    {
        var args = ParseArguments();
        return args.Count == 0 ? "" : " " + string.Join(' ', args);
    }

    public IReadOnlyList<string> ParseArguments() =>
        ArgumentsText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    partial void OnExecutablePathChanged(string value)
    {
        OnPropertyChanged(nameof(CommandPreview));
        if (Mode is EditorMode.AddCli && DisplayName.Length == 0 && value.Length > 0)
        {
            var name = Path.GetFileNameWithoutExtension(value);
            var known = CliCatalog.KnownClis.FirstOrDefault(c =>
                c.Aliases.Contains(name, StringComparer.OrdinalIgnoreCase));
            DisplayName = $"在 {known?.DisplayName ?? name} 中打开";
        }
    }

    partial void OnArgumentsTextChanged(string value) => OnPropertyChanged(nameof(CommandPreview));

    partial void OnRawCommandChanged(string value) => OnPropertyChanged(nameof(CommandPreview));

    /// <summary>保存前只读验证（规划 03 §6）。</summary>
    public bool Validate()
    {
        if (IsManagedForm)
        {
            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                ValidationError = "显示名不能为空";
                return false;
            }

            if (!File.Exists(ExecutablePath))
            {
                ValidationError = $"文件不存在: {ExecutablePath}";
                return false;
            }

            var ext = Path.GetExtension(ExecutablePath).ToLowerInvariant();
            if (ext is not (".exe" or ".com" or ".cmd" or ".bat"))
            {
                ValidationError = $"不支持的扩展名: {ext}（支持 .exe .com .cmd .bat）";
                return false;
            }
        }
        else if (IsSimpleForm)
        {
            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                ValidationError = "显示名不能为空";
                return false;
            }

            if (string.IsNullOrWhiteSpace(RawCommand))
            {
                ValidationError = "命令不能为空";
                return false;
            }
        }

        ValidationError = null;
        return true;
    }

    public LaunchSpec ToLaunchSpec() => new()
    {
        ExecutablePath = ExecutablePath,
        Arguments = ParseArguments(),
        Terminal = (TerminalKind)TerminalIndex,
        KeepOpenAfterExit = KeepOpen,
    };

    /// <summary>图标规格：优先用户填写，否则取可执行文件（.cmd/.bat 无图标资源时由展示层回退）。</summary>
    public string EffectiveIconSpec()
    {
        if (!string.IsNullOrWhiteSpace(IconSpec))
        {
            return IconSpec;
        }

        var ext = Path.GetExtension(ExecutablePath).ToLowerInvariant();
        return ext is ".exe" or ".com" ? $"{ExecutablePath},0" : "";
    }
}
