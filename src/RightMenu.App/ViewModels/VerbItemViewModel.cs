using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RightMenu.Core.Registry;

namespace RightMenu.App.ViewModels;

/// <summary>主列表单行（规划 03 §2）。Phase 1 只读。</summary>
public sealed partial class VerbItemViewModel(ShellVerb verb) : ObservableObject
{
    public ShellVerb Verb { get; } = verb;

    public string DisplayName => Verb.DisplayName;

    public string KeyName => Verb.KeyName;

    /// <summary>副标题：命令主体 · 来源。</summary>
    public string Subtitle
    {
        get
        {
            var source = Verb.Source == RegistryHiveSource.Hkcu ? "当前用户" : "系统";
            var head = CommandHead;
            return head.Length > 0 ? $"{head} · {source}" : source;
        }
    }

    /// <summary>命令的第一段（完整可执行文件路径），供图标回退使用。</summary>
    public string CommandExecutablePath
    {
        get
        {
            var command = Verb.Command.Trim();
            if (command.Length == 0)
            {
                return "";
            }

            if (command.StartsWith('"'))
            {
                var end = command.IndexOf('"', 1);
                return end > 1 ? command[1..end] : command;
            }

            var space = command.IndexOf(' ');
            return space > 0 ? command[..space] : command;
        }
    }

    /// <summary>命令的第一段（仅文件名），供副标题展示。</summary>
    private string CommandHead
    {
        get
        {
            var head = CommandExecutablePath;
            if (head.Length == 0)
            {
                return Verb.Kind switch
                {
                    ShellVerbKind.Submenu => "子菜单",
                    ShellVerbKind.Delegate => "COM 委托",
                    _ => "",
                };
            }

            var slash = head.LastIndexOf('\\');
            return slash >= 0 ? head[(slash + 1)..] : head;
        }
    }

    /// <summary>状态短文案：开启 / 已禁用 / 只读 / 可迁移 / 已遮蔽。</summary>
    public string StatusText
    {
        get
        {
            if (Verb.IsShadowed)
            {
                return "已遮蔽";
            }

            return Verb.Capability switch
            {
                EditCapability.ReadOnly => "只读",
                EditCapability.Migratable => "可迁移",
                _ => Verb.IsEnabled ? "开启" : "已禁用",
            };
        }
    }

    /// <summary>高级项：默认视图不显示（规划 02 §1）。</summary>
    public bool IsAdvanced =>
        !Verb.IsEffective || !Verb.IsExplorerVisible || Verb.IsConditional ||
        Verb.Kind is ShellVerbKind.Unknown;

    public bool IsLegacyManaged => Verb.IsLegacyManaged;

    public string? ReadOnlyReason => Verb.ReadOnlyReason;

    public string FullKeyPath => Verb.FullKeyPath;

    /// <summary>行图标，扫描后异步加载（规划 04 Phase 1：不阻塞首屏）。</summary>
    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

    /// <summary>搜索匹配：显示名、命令、键名（规划 03 §5）。</summary>
    public bool Matches(string query) =>
        DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Verb.Command.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Verb.KeyName.Contains(query, StringComparison.OrdinalIgnoreCase);
}
