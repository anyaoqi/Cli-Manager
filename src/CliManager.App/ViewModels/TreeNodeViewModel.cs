using System.Collections.ObjectModel;
using System.Windows.Media;
using CliManager.App.Services;
using CliManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CliManager.App.ViewModels;

/// <summary>
/// 树形视图节点模型（统一表达菜单文件夹与直出工具项，支持拖拽重排）。
/// </summary>
public partial class TreeNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string? _iconPath;

    [ObservableProperty]
    private ImageSource? _iconSource;

    [ObservableProperty]
    private bool _isFolder;

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private int _order = 10;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    public FolderItem? Folder { get; set; }

    public ToolItem? Tool { get; set; }

    public TreeNodeViewModel? ParentNode { get; set; }

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    public static TreeNodeViewModel CreateFolderNode(FolderItem folder)
    {
        var node = new TreeNodeViewModel
        {
            Id = folder.Id,
            Title = folder.Name,
            IconPath = folder.Icon,
            IsFolder = true,
            Enabled = folder.Enabled,
            Order = folder.Order,
            Folder = folder,
            IconSource = ImageHelper.GetIconSource(folder.Icon)
        };
        return node;
    }

    public static TreeNodeViewModel CreateToolNode(ToolItem tool, TreeNodeViewModel? parent = null)
    {
        var node = new TreeNodeViewModel
        {
            Id = tool.Id,
            Title = tool.Name,
            IconPath = tool.Icon,
            IsFolder = false,
            Enabled = tool.Enabled,
            Order = tool.Order,
            Tool = tool,
            ParentNode = parent,
            IconSource = ImageHelper.GetIconSource(tool.Icon ?? tool.Executable)
        };
        return node;
    }

    public void RefreshIcon()
    {
        string? path = IconPath ?? (Tool?.Executable);
        ImageHelper.InvalidateCache(path);
        IconSource = ImageHelper.GetIconSource(path);
    }
}
