using System.Collections.ObjectModel;
using CliManager.Core.Detection;
using CliManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CliManager.App.ViewModels;

/// <summary>
/// 扫描并导入弹框的用户选择结果。
/// TargetFolderId 与 NewFolderName 均为空时表示导入到根层级（一级菜单直出）。
/// </summary>
public sealed class ImportSelectionResult
{
    /// <summary>用户勾选要导入的工具（始终为候选列表的子集）。</summary>
    public List<DetectedTool> SelectedTools { get; init; } = [];

    /// <summary>目标已有分组 ID；null 表示根层级（除非指定 NewFolderName）。</summary>
    public string? TargetFolderId { get; init; }

    /// <summary>非空表示需要新建同名分组并导入其中。</summary>
    public string? NewFolderName { get; init; }
}

/// <summary>弹框中单个候选工具行。</summary>
public partial class ImportToolItemViewModel : ObservableObject
{
    public ImportToolItemViewModel(DetectedTool tool)
    {
        Tool = tool;
    }

    public DetectedTool Tool { get; }

    public string Name => Tool.RecommendedDisplayName ?? Tool.Name;

    public string ExecutablePath => Tool.ExecutablePath;

    /// <summary>是否命中内置 AI CLI 预设（用于界面角标提示）。</summary>
    public bool IsAiTool => !string.IsNullOrEmpty(Tool.MatchedPresetId);

    public string SourceLabel => Tool.Source switch
    {
        "local_bin" => "~/.local/bin",
        "npm" => "npm 全局",
        "node" => "Node.js 目录",
        "path" => "PATH",
        _ => Tool.Source
    };

    [ObservableProperty]
    private bool _isSelected = true;

    public event EventHandler? IsSelectedChanged;

    partial void OnIsSelectedChanged(bool value) => IsSelectedChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// 扫描并导入弹框 ViewModel：勾选要导入的 CLI 工具，并选择目标分组（已有分组 / 新建分组 / 根层级）。
/// 默认不创建任何分组，不勾选任何分组时工具导入到根层级。
/// </summary>
public partial class ImportToolsViewModel : ObservableObject
{
    /// <summary>"新建分组" 选项的哨兵 ID。</summary>
    public const string NewFolderOptionId = "__new_folder__";

    public ObservableCollection<ImportToolItemViewModel> Tools { get; } = [];

    public ObservableCollection<FolderOption> FolderOptions { get; } = [];

    [ObservableProperty]
    private FolderOption? _selectedFolder;

    [ObservableProperty]
    private string _newFolderName = string.Empty;

    [ObservableProperty]
    private bool _isNewFolderSelected;

    [ObservableProperty]
    private int _selectedCount;

    public int TotalCount => Tools.Count;

    public ImportToolsViewModel(IEnumerable<DetectedTool> detectedTools, IEnumerable<FolderItem> existingFolders)
    {
        foreach (var tool in detectedTools)
        {
            var item = new ImportToolItemViewModel(tool);
            item.IsSelectedChanged += (_, _) => RefreshSelectionState();
            Tools.Add(item);
        }

        // 目标分组选项：根层级 + 已有分组 + 新建分组
        FolderOptions.Add(new FolderOption(null, "（根层级，不放入分组）"));
        foreach (var folder in existingFolders)
        {
            FolderOptions.Add(new FolderOption(folder.Id, $"📂 {folder.Name}"));
        }
        FolderOptions.Add(new FolderOption(NewFolderOptionId, "➕ 新建分组…"));

        SelectedFolder = FolderOptions[0];
        RefreshSelectionState();
    }

    partial void OnSelectedFolderChanged(FolderOption? value)
    {
        IsNewFolderSelected = value?.Id == NewFolderOptionId;
        ImportCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewFolderNameChanged(string value) => ImportCommand.NotifyCanExecuteChanged();

    private void RefreshSelectionState()
    {
        SelectedCount = Tools.Count(t => t.IsSelected);
        OnPropertyChanged(nameof(TotalCount));
        ImportCommand.NotifyCanExecuteChanged();
    }

    private bool CanImport() =>
        SelectedCount > 0 && (!IsNewFolderSelected || !string.IsNullOrWhiteSpace(NewFolderName));

    /// <summary>由窗口确认按钮触发（Command 校验 + Click 关闭窗口）。</summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import()
    {
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var tool in Tools)
        {
            tool.IsSelected = true;
        }
    }

    [RelayCommand]
    private void UnselectAll()
    {
        foreach (var tool in Tools)
        {
            tool.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SelectAiOnly()
    {
        foreach (var tool in Tools)
        {
            tool.IsSelected = tool.IsAiTool;
        }
    }

    /// <summary>汇总当前勾选状态为导入结果。</summary>
    public ImportSelectionResult BuildResult()
    {
        bool createNewFolder = IsNewFolderSelected && !string.IsNullOrWhiteSpace(NewFolderName);
        string? targetFolderId = IsNewFolderSelected ? null : SelectedFolder?.Id;
        return new ImportSelectionResult
        {
            SelectedTools = Tools.Where(t => t.IsSelected).Select(t => t.Tool).ToList(),
            TargetFolderId = targetFolderId,
            NewFolderName = createNewFolder ? NewFolderName.Trim() : null
        };
    }
}
