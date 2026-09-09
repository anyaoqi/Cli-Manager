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
    private bool _hasSelectedNode;

    [ObservableProperty]
    private string _livePreviewText = string.Empty;

    [ObservableProperty]
    private string _livePreviewToolTip = string.Empty;

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

        // 自动清理/修复配置中历史残留的无扩展名或重复项（如 opencode vs opencode.cmd）
        int countBefore = Config.Tools.Count;
        CleanLegacyDuplicates(Config);
        if (Config.Tools.Count != countBefore)
        {
            ConfigStorageService.SaveConfig(Config);
        }

        // 清理历史残留的 AI 编程工具默认图标（按需求去掉默认图标值）
        foreach (var f in Config.Folders)
        {
            if (f.Name.Contains("AI") && f.Icon == "shell32.dll,305")
            {
                f.Icon = null;
            }
        }

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
            HasSelectedNode = false;
            return;
        }

        if (value.IsFolder && value.Folder != null)
        {
            IsEditingFolder = true;
            IsEditingTool = false;
            HasSelectedNode = true;
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
            HasSelectedNode = true;
            ToolEditor.Load(value.Tool, Config.Folders, () =>
            {
                value.Title = value.Tool.Name;
                value.IconPath = value.Tool.Icon;
                value.RefreshIcon();
                UpdateLivePreview();
            });
        }
        else
        {
            IsEditingFolder = false;
            IsEditingTool = false;
            HasSelectedNode = false;
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
            var existingNames = new HashSet<string>(
                Config.Tools.Select(t => t.Name),
                StringComparer.OrdinalIgnoreCase);

            _unconfiguredDetectedTools.Clear();
            foreach (var d in detected)
            {
                if (!existingExes.Contains(d.ExecutablePath) &&
                    !existingNames.Contains(d.Name) &&
                    !existingNames.Contains(d.RecommendedDisplayName ?? ""))
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
                Icon = null, // 去掉默认图标值，保持为空
                Order = 10,
                Enabled = true
            };
            Config.Folders.Add(folder);
        }
        else if (folder.Icon == "shell32.dll,305")
        {
            // 若为历史遗留的默认值则清除
            folder.Icon = null;
        }

        int startOrder = Config.Tools.Count > 0 ? Config.Tools.Max(t => t.Order) : 0;
        foreach (var d in _unconfiguredDetectedTools)
        {
            string toolName = d.RecommendedDisplayName ?? d.Name;
            bool alreadyExists = Config.Tools.Any(t =>
                string.Equals(t.Executable, d.ExecutablePath, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(t.ParentId, folder.Id, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase)));

            if (alreadyExists)
            {
                continue;
            }

            startOrder += 10;

            // 智能识别最佳图标路径（若为 .cmd/.bat 则优先探查同级同名 .exe）
            string? iconPath = null;
            if (!string.IsNullOrWhiteSpace(d.ExecutablePath))
            {
                string exe = d.ExecutablePath;
                if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                    exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                {
                    string siblingExe = Path.ChangeExtension(exe, ".exe");
                    if (File.Exists(siblingExe))
                    {
                        iconPath = siblingExe;
                    }
                }
                else if (File.Exists(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    iconPath = exe;
                }
            }

            Config.Tools.Add(new ToolItem
            {
                Name = toolName,
                Executable = d.ExecutablePath,
                Icon = iconPath,
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

    /// <summary>
    /// 删除确认委托（可由测试注入以模拟用户选择，避免阻塞自动化测试）。
    /// </summary>
    public Func<string, string, bool>? ConfirmDeleteHandler { get; set; }

    [RelayCommand]
    private void DeleteCurrent()
    {
        if (SelectedNode == null)
        {
            return;
        }

        string title = SelectedNode.IsFolder ? "删除文件夹确认" : "删除 CLI 工具确认";
        string itemName = SelectedNode.Title;
        string message = SelectedNode.IsFolder
            ? $"确定要删除文件夹「{itemName}」及其包含的所有 CLI 工具吗？\n\n此操作在保存并同步后将从 Windows 右键菜单中移除。"
            : $"确定要删除 CLI 工具「{itemName}」吗？\n\n此操作在保存并同步后将从 Windows 右键菜单中移除。";

        bool confirmed = ConfirmDeleteHandler != null
            ? ConfirmDeleteHandler(message, title)
            : System.Windows.MessageBox.Show(
                message,
                title,
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;

        if (!confirmed)
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
        int folderCount = RootNodes.Count(n => n.IsFolder);
        int directToolCount = RootNodes.Count(n => !n.IsFolder);
        int totalTools = Config.Tools.Count;

        if (RootNodes.Count == 0)
        {
            LivePreviewText = "📌 暂无右键配置项（点击上方按钮新建分组或添加工具）";
            LivePreviewToolTip = "暂无配置项，请点击上方按钮新建分组或添加工具";
            return;
        }

        // 底部单行精简概览：清晰、短小、绝不溢出截断
        string summary;
        if (folderCount > 0 && directToolCount > 0)
        {
            summary = $"{folderCount} 个分组、{directToolCount} 个一级工具（共 {totalTools} 项）";
        }
        else if (folderCount > 0)
        {
            summary = $"{folderCount} 个分组（共 {totalTools} 项）";
        }
        else
        {
            summary = $"{directToolCount} 个一级工具";
        }

        LivePreviewText = $"📊 菜单概览：{summary}";

        // 鼠标悬浮气泡：展示清晰的树形菜单层级预览
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("右键菜单层级结构预览：");
        sb.AppendLine("空白处右击");
        foreach (var node in RootNodes)
        {
            if (node.IsFolder)
            {
                sb.AppendLine($"  📂 {node.Title} ({node.Children.Count} 项)");
                foreach (var child in node.Children)
                {
                    sb.AppendLine($"     └─ 🚀 {child.Title}");
                }
            }
            else
            {
                sb.AppendLine($"  🚀 {node.Title}");
            }
        }
        LivePreviewToolTip = sb.ToString().TrimEnd();
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

    /// <summary>
    /// 清理历史导入残留的无扩展名或重复工具项（如 opencode 与 opencode.cmd 重复）。
    /// </summary>
    public static void CleanLegacyDuplicates(CliConfig config)
    {
        var validExtensions = new[] { ".exe", ".cmd", ".bat", ".ps1" };
        var toRemove = new List<ToolItem>();

        foreach (var tool in config.Tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Executable))
            {
                continue;
            }

            string ext = Path.GetExtension(tool.Executable).ToLowerInvariant();
            // 如果可执行文件缺少有效 Windows 扩展名（例如 node 生成的 Linux shell 脚本）
            if (!validExtensions.Contains(ext))
            {
                // 检查是否已经存在带 .cmd 或 .exe 的有效对应项
                bool hasValidSibling = config.Tools.Any(other =>
                    other != tool &&
                    !string.IsNullOrWhiteSpace(other.Executable) &&
                    (string.Equals(other.Executable, tool.Executable + ".cmd", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(other.Executable, tool.Executable + ".exe", StringComparison.OrdinalIgnoreCase) ||
                     (string.Equals(other.Name, tool.Name, StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(other.ParentId, tool.ParentId, StringComparison.OrdinalIgnoreCase) &&
                      validExtensions.Contains(Path.GetExtension(other.Executable).ToLowerInvariant()))));

                if (hasValidSibling)
                {
                    toRemove.Add(tool);
                }
                else
                {
                    // 若无有效项，但磁盘上存在对应的 .cmd 或 .exe，则升级其路径为 Windows 可执行脚本
                    string siblingCmd = tool.Executable + ".cmd";
                    string siblingExe = tool.Executable + ".exe";
                    if (File.Exists(siblingExe))
                    {
                        tool.Executable = siblingExe;
                    }
                    else if (File.Exists(siblingCmd))
                    {
                        tool.Executable = siblingCmd;
                    }
                }
            }
        }

        foreach (var tool in toRemove)
        {
            config.Tools.Remove(tool);
        }
    }
}
