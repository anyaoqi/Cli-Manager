using System.Collections.ObjectModel;
using CliManager.App.Services;
using CliManager.Core.Detection;
using CliManager.Core.Icons;
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

    internal readonly List<DetectedTool> _unconfiguredDetectedTools = [];

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

    partial void OnSelectedNodeChanged(TreeNodeViewModel? oldValue, TreeNodeViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue == null)
        {
            IsEditingFolder = false;
            IsEditingTool = false;
            HasSelectedNode = false;
            return;
        }

        newValue.IsSelected = true;
        if (newValue.ParentNode != null)
        {
            newValue.ParentNode.IsExpanded = true;
        }

        if (newValue.IsFolder && newValue.Folder != null)
        {
            IsEditingFolder = true;
            IsEditingTool = false;
            HasSelectedNode = true;
            FolderEditor.Load(newValue.Folder, () =>
            {
                newValue.Title = newValue.Folder.Name;
                newValue.IconPath = newValue.Folder.Icon;
                newValue.RefreshIcon();
                UpdateLivePreview();
            });
        }
        else if (!newValue.IsFolder && newValue.Tool != null)
        {
            IsEditingFolder = false;
            IsEditingTool = true;
            HasSelectedNode = true;
            ToolEditor.Load(newValue.Tool, Config.Folders, () =>
            {
                // 检测是否在右侧属性面板中切换了"归属文件夹"：若发生变动，立即重建树以保持左右完全同步
                string? currentParentId = newValue.ParentNode?.Id;
                string? newParentId = newValue.Tool.ParentId;
                if (!string.Equals(currentParentId, newParentId, StringComparison.OrdinalIgnoreCase))
                {
                    BuildTree();
                    SelectedNode = FindNode(newValue.Tool.Id);
                    return;
                }

                newValue.Title = newValue.Tool.Name;
                newValue.IconPath = newValue.Tool.Icon;
                newValue.RefreshIcon();
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

            RefreshDetectedBanner();
        }
        catch
        {
            HasDetectedTools = false;
        }
    }

    /// <summary>
    /// 根据剩余未配置的探测结果刷新顶部横幅。
    /// </summary>
    private void RefreshDetectedBanner()
    {
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

    /// <summary>
    /// 扫描并导入弹框委托（可由测试注入以模拟用户勾选，避免阻塞自动化测试）。
    /// 入参为候选工具与现有分组；返回 null 表示用户取消。
    /// </summary>
    public Func<IReadOnlyList<DetectedTool>, IReadOnlyList<FolderItem>, ImportSelectionResult?>? ImportDialogHandler { get; set; }

    /// <summary>
    /// 扫描并导入：弹出勾选框，由用户选择要导入的工具与目标分组（已有分组 / 新建分组 / 根层级）。
    /// 不再默认创建【AI 编程工具】分组；未选择分组时导入到根层级。
    /// </summary>
    [RelayCommand]
    private void ImportDetectedTools()
    {
        if (_unconfiguredDetectedTools.Count == 0)
        {
            return;
        }

        IReadOnlyList<DetectedTool> candidates = _unconfiguredDetectedTools.ToList();
        ImportSelectionResult? result = ImportDialogHandler != null
            ? ImportDialogHandler(candidates, Config.Folders)
            : ShowImportDialog(candidates, Config.Folders);

        if (result == null || result.SelectedTools.Count == 0)
        {
            return;
        }

        // 解析目标分组：显式新建分组 > 已选分组 > 根层级
        string? parentId = null;
        FolderItem? createdFolder = null;
        if (!string.IsNullOrWhiteSpace(result.NewFolderName))
        {
            createdFolder = new FolderItem
            {
                Name = result.NewFolderName.Trim(),
                Icon = null,
                Order = Config.Folders.Count > 0 ? Config.Folders.Max(f => f.Order) + 10 : 10,
                Enabled = true
            };
            Config.Folders.Add(createdFolder);
            parentId = createdFolder.Id;
        }
        else if (!string.IsNullOrEmpty(result.TargetFolderId) &&
                 result.TargetFolderId != ImportToolsViewModel.NewFolderOptionId &&
                 Config.Folders.Any(f => f.Id == result.TargetFolderId))
        {
            parentId = result.TargetFolderId;
        }

        int startOrder = Config.Tools.Count > 0 ? Config.Tools.Max(t => t.Order) : 0;
        int importedCount = 0;
        string? firstImportedToolId = null;
        foreach (var d in result.SelectedTools)
        {
            string toolName = d.RecommendedDisplayName ?? d.Name;
            bool alreadyExists = Config.Tools.Any(t =>
                string.Equals(t.Executable, d.ExecutablePath, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(t.ParentId, parentId, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase)));

            // 已存在的视为已配置，同样从候选横幅中移除
            _unconfiguredDetectedTools.Remove(d);
            if (alreadyExists)
            {
                continue;
            }

            startOrder += 10;
            importedCount++;

            var newTool = new ToolItem
            {
                Name = toolName,
                Executable = d.ExecutablePath,
                Icon = ResolveDetectedToolIcon(d),
                Host = d.RecommendedHost ?? TerminalHosts.WindowsTerminal,
                ParentId = parentId,
                Order = startOrder,
                Enabled = true
            };
            Config.Tools.Add(newTool);
            firstImportedToolId ??= newTool.Id;
        }

        RefreshDetectedBanner();
        BuildTree();
        UpdateLivePreview();

        // 优先选中新建的文件夹；若未建文件夹则聚焦到首个导入的工具并展开所属父级
        if (createdFolder != null)
        {
            SelectedNode = RootNodes.FirstOrDefault(n => n.Id == createdFolder.Id);
            if (SelectedNode != null)
            {
                SelectedNode.IsExpanded = true;
            }
        }
        else if (firstImportedToolId != null)
        {
            SelectedNode = FindNode(firstImportedToolId);
        }

        string targetName = createdFolder?.Name
            ?? Config.Folders.FirstOrDefault(f => f.Id == parentId)?.Name
            ?? "根层级";
        ShowInfoBar("导入成功", $"已导入 {importedCount} 个 CLI 工具至【{targetName}】，点击右下角【保存并同步】即可生效。");
    }

    /// <summary>
    /// 弹出导入选择窗口（默认实现，测试中通过 ImportDialogHandler 替代）。
    /// </summary>
    private static ImportSelectionResult? ShowImportDialog(IReadOnlyList<DetectedTool> candidates, IReadOnlyList<FolderItem> folders)
    {
        var viewModel = new ImportToolsViewModel(candidates, folders);
        var window = new Views.ImportToolsWindow(viewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        return window.ShowDialog() == true ? viewModel.BuildResult() : null;
    }

    /// <summary>
    /// 智能识别最佳图标路径（.cmd/.bat 优先探查同级同名 .exe）。
    /// </summary>
    private static string? ResolveDetectedToolIcon(DetectedTool d)
    {
        if (string.IsNullOrWhiteSpace(d.ExecutablePath))
        {
            return null;
        }

        string exe = d.ExecutablePath;
        if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            string siblingExe = Path.ChangeExtension(exe, ".exe");
            return File.Exists(siblingExe) ? siblingExe : null;
        }

        return File.Exists(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : null;
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
            var toolsInFolder = Config.Tools.Where(t => t.ParentId == SelectedNode.Folder.Id).ToList();
            foreach (var t in toolsInFolder)
            {
                RecordDeletedHklmKey(t);
            }
            Config.Tools.RemoveAll(t => t.ParentId == SelectedNode.Folder.Id);
            Config.Folders.Remove(SelectedNode.Folder);
        }
        else if (!SelectedNode.IsFolder && SelectedNode.Tool != null)
        {
            RecordDeletedHklmKey(SelectedNode.Tool);
            Config.Tools.Remove(SelectedNode.Tool);
        }

        SelectedNode = null;
        BuildTree();
        UpdateLivePreview();
    }

    private void RecordDeletedHklmKey(ToolItem tool)
    {
        string? hklmKey = tool.OriginalHklmKey;
        if (string.IsNullOrWhiteSpace(hklmKey))
        {
            hklmKey = FindMatchingHklmKey(tool);
        }

        if (!string.IsNullOrWhiteSpace(hklmKey))
        {
            if (!Config.Settings.HiddenHklmKeys.Contains(hklmKey, StringComparer.OrdinalIgnoreCase))
            {
                Config.Settings.HiddenHklmKeys.Add(hklmKey);
            }
        }
    }

    private static string? FindMatchingHklmKey(ToolItem tool)
    {
        try
        {
            using var hklmShell = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(RegistryConstants.DefaultBackgroundShellPath);
            if (hklmShell == null) return null;

            string? toolExeName = !string.IsNullOrWhiteSpace(tool.Executable)
                ? System.IO.Path.GetFileName(tool.Executable)
                : null;

            foreach (string subName in hklmShell.GetSubKeyNames())
            {
                using var sub = hklmShell.OpenSubKey(subName);
                if (sub == null) continue;

                if (subName.Equals(tool.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return subName;
                }

                string? mui = sub.GetValue(RegistryConstants.MuiVerbValueName) as string;
                string? def = sub.GetValue("") as string;
                string hklmName = IndirectStringResolver.Resolve(!string.IsNullOrWhiteSpace(mui) ? mui : def, subName);
                if (tool.Name.Equals(hklmName, StringComparison.OrdinalIgnoreCase))
                {
                    return subName;
                }

                if (!string.IsNullOrWhiteSpace(toolExeName))
                {
                    using var cmdKey = sub.OpenSubKey("command");
                    string? cmd = cmdKey?.GetValue("") as string;
                    if (!string.IsNullOrWhiteSpace(cmd) && cmd.Contains(toolExeName, StringComparison.OrdinalIgnoreCase))
                    {
                        return subName;
                    }
                }
            }
        }
        catch
        {
            // 忽略非特权异常
        }

        return null;
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

    /// <summary>
    /// 拖拽落下后调用：重建树、按 Id 重新选中新节点并刷新概览。
    /// 必须重新按 Id 查找节点：BuildTree 会重建全部节点，旧节点引用已脱离树；
    /// 若把 SelectedNode 设回旧引用则属性不变、选中事件不触发，右侧属性面板
    /// 不会重新加载，保存时 ApplyToModel 会用面板里过期的"归属文件夹"把本次
    /// 拖拽的 ParentId 改动覆盖回去。
    /// </summary>
    public void CompleteDragReorder(TreeNodeViewModel source)
    {
        BuildTree();
        SelectedNode = FindNode(source.Id);
        UpdateLivePreview();
    }

    /// <summary>
    /// 拖拽落点的统一移动入口（在模型空间完成搬移后重建树）。
    /// - targetFolder 非空：把工具移入该文件夹末尾（拖到文件夹行上）；
    /// - targetSibling 非空：插入到该节点上/下方（文件源会自动映射为根级参照）；
    /// - 两者皆空：移到根级末尾（拖到空白区域）。
    /// 移动后会对受影响的兄弟列表统一重排 Order，避免出现重复序号。
    /// </summary>
    public void MoveDraggedNode(TreeNodeViewModel source, TreeNodeViewModel? targetSibling, bool insertAbove, TreeNodeViewModel? targetFolder)
    {
        if (source == null)
        {
            return;
        }

        // 落点是自己：保持原地不动
        if (targetSibling != null && ReferenceEquals(targetSibling, source))
        {
            return;
        }
        if (targetFolder != null && ReferenceEquals(targetFolder, source))
        {
            return;
        }

        // 文件夹只能参与根级排序：参照物统一映射为根级节点
        if (source.IsFolder)
        {
            if (targetSibling == null && targetFolder != null)
            {
                targetSibling = targetFolder;
                insertAbove = false;
            }
            if (targetSibling?.ParentNode?.IsFolder == true)
            {
                targetSibling = targetSibling.ParentNode; // 参照物是文件夹内的工具 → 用其所属文件夹
            }
            targetFolder = null;

            // 映射后参照物可能是自身（如拖到自己子工具上）：原地不动
            if (targetSibling != null && ReferenceEquals(targetSibling, source))
            {
                return;
            }
        }

        // 工具源：目标容器由参照物的父级决定
        string? destParentId = null;
        if (!source.IsFolder)
        {
            if (targetSibling != null)
            {
                destParentId = targetSibling.ParentNode?.Id; // 根级参照 → null；文件夹内参照 → 文件夹 Id
            }
            else if (targetFolder?.Folder != null)
            {
                destParentId = targetFolder.Folder.Id;
            }
        }

        if (source.IsFolder && source.Folder != null)
        {
            // 文件夹在根级混排列表中重排
            var rootItems = BuildMergedRootItems();
            object sourceModel = source.Folder;
            object? refModel = targetSibling == null ? null : ModelOf(targetSibling);

            rootItems.Remove(sourceModel);
            int insertIndex = refModel == null ? rootItems.Count : ComputeInsertIndex(rootItems, refModel, insertAbove);
            rootItems.Insert(Math.Clamp(insertIndex, 0, rootItems.Count), sourceModel);

            for (int i = 0; i < rootItems.Count; i++)
            {
                SetModelOrder(rootItems[i], (i + 1) * 10);
            }
        }
        else if (source.Tool != null)
        {
            string? oldParentId = source.Tool.ParentId;
            object? refModel = targetSibling == null ? null : ModelOf(targetSibling);

            // 组装目标容器列表（已排除 source 自身）
            List<object> destItems;
            if (destParentId == null)
            {
                destItems = BuildMergedRootItems();
                destItems.Remove(source.Tool);
            }
            else
            {
                destItems = Config.Tools
                    .Where(t => t.ParentId == destParentId)
                    .OrderBy(t => t.Order)
                    .Cast<object>()
                    .ToList();
                destItems.Remove(source.Tool);
            }

            int insertIndex = refModel == null ? destItems.Count : ComputeInsertIndex(destItems, refModel, insertAbove);
            destItems.Insert(Math.Clamp(insertIndex, 0, destItems.Count), source.Tool);

            // 回写归属与次序
            source.Tool.ParentId = destParentId;
            for (int i = 0; i < destItems.Count; i++)
            {
                SetModelOrder(destItems[i], (i + 1) * 10);
            }

            // 跨容器搬移时收紧旧容器的次序
            if (oldParentId != destParentId)
            {
                if (oldParentId == null)
                {
                    var rootItems = BuildMergedRootItems();
                    for (int i = 0; i < rootItems.Count; i++)
                    {
                        SetModelOrder(rootItems[i], (i + 1) * 10);
                    }
                }
                else
                {
                    var oldSiblings = Config.Tools
                        .Where(t => t.ParentId == oldParentId)
                        .OrderBy(t => t.Order)
                        .ToList();
                    for (int i = 0; i < oldSiblings.Count; i++)
                    {
                        oldSiblings[i].Order = (i + 1) * 10;
                    }
                }
            }
        }

        CompleteDragReorder(source);
    }

    /// <summary>根级混排列表：文件夹 + 未归属工具，按 Order 升序。</summary>
    private List<object> BuildMergedRootItems()
    {
        var items = new List<object>();
        items.AddRange(Config.Folders);
        items.AddRange(Config.Tools.Where(t => string.IsNullOrEmpty(t.ParentId)));
        items.Sort((a, b) => GetModelOrder(a).CompareTo(GetModelOrder(b)));
        return items;
    }

    private static object? ModelOf(TreeNodeViewModel? node) =>
        node == null ? null : node.IsFolder ? node.Folder : (object?)node.Tool;

    private static int GetModelOrder(object model) => model switch
    {
        FolderItem folder => folder.Order,
        ToolItem tool => tool.Order,
        _ => int.MaxValue
    };

    private static void SetModelOrder(object model, int order)
    {
        switch (model)
        {
            case FolderItem folder:
                folder.Order = order;
                break;
            case ToolItem tool:
                tool.Order = order;
                break;
        }
    }

    private static int ComputeInsertIndex(List<object> list, object refModel, bool insertAbove)
    {
        int refIndex = list.IndexOf(refModel);
        return refIndex < 0 ? list.Count : insertAbove ? refIndex : refIndex + 1;
    }

    [RelayCommand]
    private async Task SaveAndSyncAsync()
    {
        try
        {
            LivePreviewText = "⏳ 正在保存配置并同步注册表...";

            // 确保编辑面板的内容已回写到模型
            ToolEditor.ApplyToModel();
            FolderEditor.ApplyToModel();

            // 0. 解析网站地址图标：后台线程下载 favicon 至本地缓存，绝不阻塞 UI 线程
            try
            {
                string iconCacheDir = ConfigStorageService.GetIconCacheDirectory();
                int resolved = await Task.Run(() => FaviconService.ResolveConfigIcons(Config, iconCacheDir));
                if (resolved > 0)
                {
                    // 若当前选中的节点图标刚刚完成下载，刷新其图标和属性面板文本框
                    if (SelectedNode != null)
                    {
                        if (SelectedNode.Tool != null)
                        {
                            SelectedNode.IconPath = SelectedNode.Tool.Icon;
                            ToolEditor.Icon = SelectedNode.Tool.Icon;
                        }
                        else if (SelectedNode.Folder != null)
                        {
                            SelectedNode.IconPath = SelectedNode.Folder.Icon;
                            FolderEditor.Icon = SelectedNode.Folder.Icon;
                        }
                        SelectedNode.RefreshIcon();
                    }
                }
            }
            catch
            {
                // 图标下载失败不阻塞主同步
            }

            // 1. 持久化到本地配置文件
            ConfigStorageService.SaveConfig(Config);

            // 2. 探测 wt.exe 路径
            string? wtPath = CliDiscoveryService.DetectWindowsTerminalPath();
            string backupDir = ConfigStorageService.GetBackupDirectory();

            // 3. 执行注册表原子 Diff & Apply（附带程序基目录，便于写入默认图标）
            var result = await Task.Run(() => _syncEngine.Sync(Config, wtPath, backupDir, AppDomain.CurrentDomain.BaseDirectory));

            if (result.Success)
            {
                UpdateLivePreview();
                if (result.Warnings.Count > 0)
                {
                    string warnSummary = string.Join("；", result.Warnings);
                    LivePreviewText = $"⚠️ [同步完成有警告] 生效 {result.AddedOrUpdatedCount} 项，跳过 {result.Warnings.Count} 项 - {DateTime.Now:HH:mm:ss}";
                    ShowInfoBar(
                        "同步完成（部分项已跳过）",
                        $"已写入注册表（生效 {result.AddedOrUpdatedCount} 项，清理 {result.DeletedCount} 项）。以下项因配置不完整已跳过：{warnSummary}",
                        InfoBarSeverity.Warning);
                }
                else
                {
                    LivePreviewText = $"✅ [同步成功] 生效 {result.AddedOrUpdatedCount} 项，清理 {result.DeletedCount} 项 - {DateTime.Now:HH:mm:ss}";
                    ShowInfoBar(
                        "保存并同步成功",
                        $"已成功写入注册表！生效项: {result.AddedOrUpdatedCount}，清理旧项: {result.DeletedCount}。现在可在任意目录空白处右键查看效果！",
                        InfoBarSeverity.Success);
                }
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

    public void UpdateLivePreview()
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
