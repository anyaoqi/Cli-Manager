using System.Collections.ObjectModel;
using CliManager.App.Services;
using CliManager.Core.Detection;
using CliManager.Core.Models;
using CliManager.Core.Registry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace CliManager.App.ViewModels;

/// <summary>
/// 主界面 ViewModel：统一管理配置数据、树节点编排、拖拽排序与系统同步。
/// 严格满足 §2.1 架构守则与 §四 UI 设计规范。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly RegistrySyncEngine _syncEngine = new();

    public CliConfig Config { get; private set; }

    public ObservableCollection<TreeNodeViewModel> RootNodes { get; } = [];

    [ObservableProperty]
    private bool _hasRootNodes;

    [ObservableProperty]
    private TreeNodeViewModel? _selectedNode;

    [ObservableProperty]
    private bool _isEditingTool;

    [ObservableProperty]
    private bool _isEditingFolder;

    [ObservableProperty]
    private string _livePreviewText = string.Empty;

    [ObservableProperty]
    private string? _infoBarTitle;

    [ObservableProperty]
    private string? _infoBarMessage;

    [ObservableProperty]
    private bool _isInfoBarOpen;

    [ObservableProperty]
    private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Success;

    [ObservableProperty]
    private bool _hasDetectedTools;

    [ObservableProperty]
    private string _detectedBannerText = string.Empty;

    public ToolEditorViewModel ToolEditor { get; } = new();

    public FolderEditorViewModel FolderEditor { get; } = new();

    private readonly List<DetectedTool> _unconfiguredDetectedTools = [];

    public MainViewModel()
    {
        Config = ConfigStorageService.LoadConfig();
        BuildTree();
        RunDiscovery();
        UpdateLivePreview();
    }

    /// <summary>
    /// 根据当前 Config 构建两级树结构。
    /// </summary>
    public void BuildTree()
    {
        RootNodes.Clear();

        // 1. 顶级项排序汇总
        var topItems = new List<(int Order, bool IsFolder, FolderItem? Folder, ToolItem? Tool)>();

        foreach (var folder in Config.Folders)
        {
            topItems.Add((folder.Order, true, folder, null));
        }

        foreach (var tool in Config.Tools.Where(t => string.IsNullOrEmpty(t.ParentId)))
        {
            topItems.Add((tool.Order, false, null, tool));
        }

        topItems.Sort((a, b) => a.Order.CompareTo(b.Order));

        foreach (var item in topItems)
        {
            if (item.IsFolder && item.Folder != null)
            {
                var folderNode = TreeNodeViewModel.CreateFolderNode(item.Folder);

                // 加载子工具
                var children = Config.Tools.Where(t => t.ParentId == item.Folder.Id).ToList();
                children.Sort((a, b) => a.Order.CompareTo(b.Order));

                foreach (var child in children)
                {
                    folderNode.Children.Add(TreeNodeViewModel.CreateToolNode(child, folderNode));
                }

                RootNodes.Add(folderNode);
            }
            else if (!item.IsFolder && item.Tool != null)
            {
                RootNodes.Add(TreeNodeViewModel.CreateToolNode(item.Tool, null));
            }
        }

        HasRootNodes = RootNodes.Count > 0;

        // 默认选中第一个
        if (SelectedNode == null && RootNodes.Count > 0)
        {
            SelectedNode = RootNodes[0];
        }
    }

    partial void OnSelectedNodeChanged(TreeNodeViewModel? value)
    {
        if (value == null)
        {
            IsEditingFolder = false;
            IsEditingTool = false;
            return;
        }

        if (value.IsFolder && value.Folder != null)
        {
            IsEditingFolder = true;
            IsEditingTool = false;
            FolderEditor.Load(value.Folder, () =>
            {
                value.Title = value.Folder.Name;
                value.IconPath = value.Folder.Icon;
                value.RefreshIcon();
                UpdateLivePreview();
            });
        }
        else if (!value.IsFolder && value.Tool != null)
        {
            IsEditingFolder = false;
            IsEditingTool = true;
            ToolEditor.Load(value.Tool, Config.Folders, () =>
            {
                value.Title = value.Tool.Name;
                value.IconPath = value.Tool.Icon;
                value.RefreshIcon();
                UpdateLivePreview();
            });
        }
    }

    /// <summary>
    /// 运行本地工具探测。
    /// </summary>
    private void RunDiscovery()
    {
        try
        {
            var detected = CliDiscoveryService.DiscoverInstalledTools();
            var existingExes = new HashSet<string>(
                Config.Tools.Select(t => t.Executable),
                StringComparer.OrdinalIgnoreCase);

            _unconfiguredDetectedTools.Clear();
            foreach (var d in detected)
            {
                if (!existingExes.Contains(d.ExecutablePath))
                {
                    _unconfiguredDetectedTools.Add(d);
                }
            }

            if (_unconfiguredDetectedTools.Count > 0)
            {
                HasDetectedTools = true;
                string names = string.Join(", ", _unconfiguredDetectedTools.Take(4).Select(t => t.Name));
                DetectedBannerText = $"🔍 本机发现 {_unconfiguredDetectedTools.Count} 个未配置的 CLI 工具 ({names}...)";
            }
            else
            {
                HasDetectedTools = false;
            }
        }
        catch
        {
            HasDetectedTools = false;
        }
    }

    /// <summary>
    /// 一键将探测到的工具归纳为 [AI 编程工具] 菜单文件夹。
    /// </summary>
    [RelayCommand]
    private void ImportDetectedTools()
    {
        if (_unconfiguredDetectedTools.Count == 0)
        {
            return;
        }

        // 查找或创建 AI 编程工具文件夹
        var folder = Config.Folders.FirstOrDefault(f => f.Name.Contains("AI"));
        if (folder == null)
        {
            folder = new FolderItem
            {
                Name = "AI 编程工具",
                Order = 10,
                Enabled = true
            };
            Config.Folders.Add(folder);
        }

        int startOrder = Config.Tools.Count > 0 ? Config.Tools.Max(t => t.Order) : 0;
        foreach (var d in _unconfiguredDetectedTools)
        {
            startOrder += 10;
            Config.Tools.Add(new ToolItem
            {
                Name = d.RecommendedDisplayName ?? d.Name,
                Executable = d.ExecutablePath,
                Host = d.RecommendedHost ?? TerminalHosts.WindowsTerminal,
                ParentId = folder.Id,
                Order = startOrder,
                Enabled = true
            });
        }

        _unconfiguredDetectedTools.Clear();
        HasDetectedTools = false;

        BuildTree();
        UpdateLivePreview();
        ShowInfoBar("导入成功", "已将发现的工具归纳至【AI 编程工具】文件夹，点击右下角【保存并同步】即可生效。");
    }

    [RelayCommand]
    private void AddFolder()
    {
        int maxOrder = Config.Folders.Count > 0 ? Config.Folders.Max(f => f.Order) + 10 : 10;
        var folder = new FolderItem
        {
            Name = "新文件夹",
            Order = maxOrder,
            Enabled = true
        };
        Config.Folders.Add(folder);

        BuildTree();
        SelectedNode = RootNodes.FirstOrDefault(n => n.Id == folder.Id);
        UpdateLivePreview();
    }

    [RelayCommand]
    private void AddTool()
    {
        string? parentId = null;
        if (SelectedNode != null)
        {
            parentId = SelectedNode.IsFolder ? SelectedNode.Id : SelectedNode.ParentNode?.Id;
        }

        int maxOrder = Config.Tools.Count > 0 ? Config.Tools.Max(t => t.Order) + 10 : 10;
        var tool = new ToolItem
        {
            Name = "新 CLI 工具",
            Executable = "",
            Host = TerminalHosts.WindowsTerminal,
            ParentId = parentId,
            Order = maxOrder,
            Enabled = true
        };
        Config.Tools.Add(tool);

        BuildTree();
        SelectedNode = FindNode(tool.Id);
        UpdateLivePreview();
    }

    [RelayCommand]
    private void DeleteCurrent()
    {
        if (SelectedNode == null)
        {
            return;
        }

        if (SelectedNode.IsFolder && SelectedNode.Folder != null)
        {
            // 删除文件夹及其内部所有工具
            Config.Tools.RemoveAll(t => t.ParentId == SelectedNode.Folder.Id);
            Config.Folders.Remove(SelectedNode.Folder);
        }
        else if (!SelectedNode.IsFolder && SelectedNode.Tool != null)
        {
            Config.Tools.Remove(SelectedNode.Tool);
        }

        SelectedNode = null;
        BuildTree();
        UpdateLivePreview();
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedNode == null)
        {
            return;
        }

        MoveNodeInternal(SelectedNode, moveUp: true);
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedNode == null)
        {
            return;
        }

        MoveNodeInternal(SelectedNode, moveUp: false);
    }

    private void MoveNodeInternal(TreeNodeViewModel node, bool moveUp)
    {
        // 查找兄弟集合
        var siblings = node.ParentNode != null ? node.ParentNode.Children : RootNodes;
        int index = siblings.IndexOf(node);
        if (index < 0)
        {
            return;
        }

        int targetIndex = moveUp ? index - 1 : index + 1;
        if (targetIndex < 0 || targetIndex >= siblings.Count)
        {
            return;
        }

        // 交换次序
        siblings.Move(index, targetIndex);

        // 重新分配次序序号并回写模型
        for (int i = 0; i < siblings.Count; i++)
        {
            var item = siblings[i];
            int newOrder = (i + 1) * 10;
            item.Order = newOrder;

            if (item.IsFolder && item.Folder != null)
            {
                item.Folder.Order = newOrder;
            }
            else if (!item.IsFolder && item.Tool != null)
            {
                item.Tool.Order = newOrder;
            }
        }

        UpdateLivePreview();
    }

    /// <summary>
    /// 跨组或在同组内拖拽移动节点。
    /// </summary>
    public void MoveItemTo(TreeNodeViewModel source, TreeNodeViewModel? targetFolder, int targetIndex)
    {
        if (source == null)
        {
            return;
        }

        // 改变工具的 ParentId
        if (!source.IsFolder && source.Tool != null)
        {
            source.Tool.ParentId = targetFolder?.Id;
        }

        BuildTree();
        SelectedNode = FindNode(source.Id);
        UpdateLivePreview();
    }

    [RelayCommand]
    private void SaveAndSync()
    {
        try
        {
            // 确保编辑面板的内容已回写到模型
            ToolEditor.ApplyToModel();
            FolderEditor.ApplyToModel();

            // 1. 持久化到本地配置文件
            ConfigStorageService.SaveConfig(Config);

            // 2. 探测 wt.exe 路径
            string? wtPath = CliDiscoveryService.DetectWindowsTerminalPath();
            string backupDir = ConfigStorageService.GetBackupDirectory();

            // 3. 执行注册表原子 Diff & Apply
            var result = _syncEngine.Sync(Config, wtPath, backupDir);

            if (result.Success)
            {
                UpdateLivePreview();
                LivePreviewText = $"✅ [同步成功] 生效 {result.AddedOrUpdatedCount} 项，清理 {result.DeletedCount} 项 - {DateTime.Now:HH:mm:ss}";
                ShowInfoBar(
                    "保存并同步成功",
                    $"已成功写入注册表！生效项: {result.AddedOrUpdatedCount}，清理旧项: {result.DeletedCount}。现在可在任意目录空白处右键查看效果！",
                    InfoBarSeverity.Success);
            }
            else
            {
                string errorMsg = string.Join("；", result.Errors);
                LivePreviewText = $"❌ [同步失败] {errorMsg} - {DateTime.Now:HH:mm:ss}";
                ShowInfoBar(
                    "同步时出现错误",
                    errorMsg,
                    InfoBarSeverity.Error);

                System.Windows.MessageBox.Show(
                    $"同步到右键菜单失败：\n\n{errorMsg}",
                    "CliManager 同步失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            LivePreviewText = $"❌ [异常] {ex.Message} - {DateTime.Now:HH:mm:ss}";
            ShowInfoBar("保存异常", ex.Message, InfoBarSeverity.Error);
            System.Windows.MessageBox.Show(
                $"保存并同步过程中发生未预期异常：\n\n{ex.Message}",
                "CliManager 异常",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void UpdateLivePreview()
    {
        var parts = new List<string>();
        foreach (var node in RootNodes)
        {
            if (node.IsFolder)
            {
                string childrenNames = node.Children.Count > 0
                    ? string.Join(" / ", node.Children.Select(c => c.Title))
                    : "空";
                parts.Add($"[ 📂 {node.Title} ▷ ({childrenNames}) ]");
            }
            else
            {
                parts.Add($"[ 🚀 {node.Title} ]");
            }
        }

        LivePreviewText = parts.Count > 0
            ? "空白处右击 ➔ " + string.Join(" ➔ ", parts)
            : "(暂无配置项，请点击上方按钮新建分组或添加工具)";
    }

    private async void ShowInfoBar(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Success)
    {
        InfoBarTitle = title;
        InfoBarMessage = message;
        InfoBarSeverity = severity;
        IsInfoBarOpen = true;

        if (severity == InfoBarSeverity.Success)
        {
            await Task.Delay(5000);
            if (InfoBarTitle == title)
            {
                IsInfoBarOpen = false;
            }
        }
    }

    private TreeNodeViewModel? FindNode(string id)
    {
        foreach (var root in RootNodes)
        {
            if (root.Id == id)
            {
                return root;
            }

            foreach (var child in root.Children)
            {
                if (child.Id == id)
                {
                    return child;
                }
            }
        }

        return null;
    }
}
