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

    [ObservableProperty]
    private bool _isChild;

    /// <summary>该节点正被拖拽中（源行半透明显示）。</summary>
    [ObservableProperty]
    private bool _isDragging;

    /// <summary>鼠标悬停在该文件夹行上，将作为"移入"投递目标。</summary>
    [ObservableProperty]
    private bool _isDropTarget;

    /// <summary>插入指示线：将插入到该行上方。</summary>
    [ObservableProperty]
    private bool _isDropAbove;

    /// <summary>插入指示线：将插入到该行下方。</summary>
    [ObservableProperty]
    private bool _isDropBelow;

    public FolderItem? Folder { get; set; }

    public ToolItem? Tool { get; set; }

    private TreeNodeViewModel? _parentNode;
    public TreeNodeViewModel? ParentNode
    {
        get => _parentNode;
        set
        {
            _parentNode = value;
            IsChild = value != null;
        }
    }

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
            IsChild = false,
            IconSource = ImageHelper.GetFolderIcon(folder.Icon)
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
            IsChild = parent != null,
            IconSource = ImageHelper.GetToolIcon(tool.Icon, tool.Executable)
        };
        return node;
    }

    public void RefreshIcon()
    {
        if (IsFolder)
        {
            if (!string.IsNullOrWhiteSpace(IconPath))
            {
                ImageHelper.InvalidateCache(IconPath);
            }
            IconSource = ImageHelper.GetFolderIcon(IconPath);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(IconPath))
            {
                ImageHelper.InvalidateCache(IconPath);
            }
            if (!string.IsNullOrWhiteSpace(Tool?.Executable))
            {
                ImageHelper.InvalidateCache(Tool.Executable);
            }
            IconSource = ImageHelper.GetToolIcon(IconPath, Tool?.Executable);
        }
    }
}
