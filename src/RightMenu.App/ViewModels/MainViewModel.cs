using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RightMenu.App.Services;
using RightMenu.Core.Discovery;
using RightMenu.Core.Registry;

namespace RightMenu.App.ViewModels;

/// <summary>筛选（规划 03 §2）。</summary>
public enum VerbFilter
{
    All,
    Managed,
    Existing,
    ReadOnly,
}

/// <summary>视图层对话框：确认与文件选择由 View 实现（规划 03 §5）。</summary>
public interface IUiDialogs
{
    /// <summary>删除确认：显示名称、来源、完整键路径；默认焦点取消。</summary>
    Task<bool> ConfirmDeleteAsync(string displayName, string source, string keyPath);

    /// <summary>迁移确认：说明需要一次管理员确认。</summary>
    Task<bool> ConfirmMigrateAsync(string displayName);

    string? BrowseForExecutable();
}

/// <summary>主窗口：单列表 + 位置 + Inspector（规划 03 §1）。</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly StaticShellProvider _provider;
    private readonly ShellVerbWriter _writer;
    private readonly RegistrySnapshotStore _snapshots;
    private readonly CliDiscoveryService _discovery = new();
    private readonly IUiDialogs _dialogs;

    public MainViewModel(IUiDialogs dialogs)
    {
        _dialogs = dialogs;
        var accessor = new WindowsRegistryAccessor();
        var registryWriter = new WindowsRegistryWriter();
        _provider = new StaticShellProvider(accessor, IndirectStringResolver.Resolve);
        _snapshots = new RegistrySnapshotStore(accessor, registryWriter);
        _writer = new ShellVerbWriter(accessor, registryWriter, _snapshots);
        ClassicMenu = new ClassicMenuService(accessor, registryWriter, _snapshots);

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = o => o is VerbItemViewModel item && PassesFilter(item);
    }

    public ClassicMenuService ClassicMenu { get; }

    public RegistrySnapshotStore Snapshots => _snapshots;

    /// <summary>P0 唯一位置，只显示文字不放下拉框（规划 03 §1）。</summary>
    public string LocationName => ContextLocation.DirectoryBackground.DisplayName;

    public ObservableCollection<VerbItemViewModel> Items { get; } = [];

    public ICollectionView ItemsView { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial int FilterIndex { get; set; }

    [ObservableProperty]
    public partial bool ShowAdvanced { get; set; }

    [ObservableProperty]
    public partial VerbItemViewModel? SelectedItem { get; set; }

    /// <summary>当前 Inspector 编辑器；null 表示关闭。</summary>
    [ObservableProperty]
    public partial EditorViewModel? Editor { get; set; }

    [ObservableProperty]
    public partial string StatusSummary { get; set; } = "";

    /// <summary>状态栏操作结果 + 撤销（规划 03 §5：不弹成功框）。</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool CanUndo { get; set; }

    public bool IsInspectorOpen => Editor is not null;

    partial void OnSearchTextChanged(string value) => ItemsView.Refresh();

    partial void OnFilterIndexChanged(int value) => ItemsView.Refresh();

    partial void OnShowAdvancedChanged(bool value) => ItemsView.Refresh();

    partial void OnEditorChanged(EditorViewModel? value) => OnPropertyChanged(nameof(IsInspectorOpen));

    private bool PassesFilter(VerbItemViewModel item)
    {
        if (!ShowAdvanced && item.IsAdvanced)
        {
            return false;
        }

        var byKind = (VerbFilter)FilterIndex switch
        {
            VerbFilter.Managed => item.Verb.IsManaged || item.IsLegacyManaged,
            VerbFilter.Existing => !item.Verb.IsManaged &&
                item.Verb.Capability is EditCapability.Simple or EditCapability.Migratable,
            VerbFilter.ReadOnly => item.Verb.Capability == EditCapability.ReadOnly,
            _ => true,
        };

        var query = SearchText.Trim();
        return byKind && (query.Length == 0 || item.Matches(query));
    }

    /// <summary>扫描并刷新；保留当前筛选和选中项（规划 03 §5）。</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        var selectedKey = SelectedItem is { } s ? (s.Verb.Source, s.Verb.KeyName) : default;

        var verbs = await Task.Run(() => _provider.Scan(ContextLocation.DirectoryBackground));

        Items.Clear();
        foreach (var verb in verbs)
        {
            Items.Add(new VerbItemViewModel(verb));
        }

        UpdateSummary();
        CanUndo = _snapshots.List().Count > 0;

        if (selectedKey != default)
        {
            SelectedItem = Items.FirstOrDefault(i =>
                i.Verb.Source == selectedKey.Source &&
                string.Equals(i.Verb.KeyName, selectedKey.KeyName, StringComparison.OrdinalIgnoreCase));
        }

        await LoadIconsAsync(Items.ToList());
    }

    /// <summary>主动作：添加 CLI（规划 03 §6，首屏为发现列表）。</summary>
    [RelayCommand]
    private async Task AddCliAsync()
    {
        var discovered = await Task.Run(() => _discovery.DiscoverKnown()
            .SelectMany(pair => pair.Value.Count == 0
                ? [new DiscoveredCliViewModel(pair.Key.DisplayName, null, null)]
                : pair.Value.Select(c => new DiscoveredCliViewModel(
                    pair.Value.Count > 1 ? $"{pair.Key.DisplayName}（{Path.GetExtension(c.FullPath)}）" : pair.Key.DisplayName,
                    c.FullPath, c.SourceDescription)))
            .ToList());

        Editor = new EditorViewModel(EditorMode.AddCli, null, discovered);
    }

    /// <summary>打开编辑/查看 Inspector（双击、Enter 或行尾菜单）。</summary>
    [RelayCommand]
    private void OpenEditor(VerbItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var mode = item.Verb.Capability switch
        {
            EditCapability.Full => EditorMode.EditManaged,
            EditCapability.Simple => EditorMode.EditSimple,
            EditCapability.Migratable => EditorMode.Migratable,
            _ => EditorMode.ViewOnly,
        };
        Editor = new EditorViewModel(mode, item);
    }

    [RelayCommand]
    private void CloseEditor() => Editor = null;

    /// <summary>从发现列表选中一个 CLI 预填表单。</summary>
    [RelayCommand]
    private void PickDiscovered(DiscoveredCliViewModel? cli)
    {
        if (Editor is null || cli is null)
        {
            return;
        }

        if (cli.Path is null)
        {
            BrowseExecutable();
            return;
        }

        Editor.ExecutablePath = cli.Path;
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        if (Editor is not null && _dialogs.BrowseForExecutable() is { } path)
        {
            Editor.ExecutablePath = path;
        }
    }

    /// <summary>保存 Inspector（新建/编辑/转换）。</summary>
    [RelayCommand]
    private async Task SaveEditorAsync()
    {
        if (Editor is null || !Editor.Validate())
        {
            return;
        }

        var appPath = Environment.ProcessPath ?? "RightMenu.App.exe";
        var location = ContextLocation.DirectoryBackground;

        try
        {
            switch (Editor.Mode)
            {
                case EditorMode.AddCli:
                    _writer.CreateManagedEntry(location, Editor.DisplayName.Trim(),
                        Editor.ToLaunchSpec(), Editor.EffectiveIconSpec(), appPath);
                    StatusMessage = $"已添加 “{Editor.DisplayName.Trim()}”";
                    break;

                case EditorMode.EditManaged:
                    _writer.UpdateManagedEntry(location, Editor.Target!.KeyName, Editor.Target.Verb.Raw,
                        Editor.DisplayName.Trim(), Editor.ToLaunchSpec(), Editor.EffectiveIconSpec(), appPath);
                    StatusMessage = "已保存";
                    break;

                case EditorMode.EditSimple:
                    _writer.UpdateSimpleEntry(location, Editor.Target!.KeyName, Editor.Target.Verb.Raw,
                        newDisplayName: Editor.DisplayName.Trim(),
                        newIconSpec: Editor.IconSpec.Trim(),
                        newCommand: Editor.RawCommand.Trim());
                    StatusMessage = "已保存";
                    break;
            }
        }
        catch (RegistryConflictException ex)
        {
            Editor.ValidationError = $"检测到并发修改，请刷新后重试：{ex.Message}";
            return;
        }

        Editor = null;
        await RefreshAsync();
    }

    /// <summary>启停（Space / 行开关）。</summary>
    [RelayCommand]
    private async Task ToggleEnabledAsync(VerbItemViewModel? item)
    {
        if (item is null || item.Verb.Capability is not (EditCapability.Full or EditCapability.Simple))
        {
            return;
        }

        try
        {
            _writer.SetEnabled(ContextLocation.DirectoryBackground, item.KeyName, item.Verb.Raw, !item.Verb.IsEnabled);
            StatusMessage = item.Verb.IsEnabled ? $"已禁用 “{item.DisplayName}”" : $"已启用 “{item.DisplayName}”";
        }
        catch (RegistryConflictException)
        {
            StatusMessage = "检测到并发修改，已刷新";
        }

        await RefreshAsync();
    }

    /// <summary>删除：ContentDialog 确认，默认焦点取消（规划 03 §5）。</summary>
    [RelayCommand]
    private async Task DeleteAsync(VerbItemViewModel? item)
    {
        if (item is null || item.Verb.Capability is not (EditCapability.Full or EditCapability.Simple))
        {
            return;
        }

        var source = item.Verb.Source == RegistryHiveSource.Hkcu ? "当前用户" : "系统";
        if (!await _dialogs.ConfirmDeleteAsync(item.DisplayName, source, item.FullKeyPath))
        {
            return;
        }

        try
        {
            _writer.Delete(ContextLocation.DirectoryBackground, item.KeyName, item.Verb.Raw);
            StatusMessage = $"已删除 “{item.DisplayName}”";
            Editor = null;
        }
        catch (RegistryConflictException)
        {
            StatusMessage = "检测到并发修改，已刷新";
        }

        await RefreshAsync();
    }

    /// <summary>HKLM 简单项迁移到当前用户（规划 02 §7.4）。</summary>
    [RelayCommand]
    private async Task MigrateAsync(VerbItemViewModel? item)
    {
        if (item is null || item.Verb.Capability != EditCapability.Migratable)
        {
            return;
        }

        if (!await _dialogs.ConfirmMigrateAsync(item.DisplayName))
        {
            return;
        }

        try
        {
            var outcome = _writer.MigrateFromHklm(
                ContextLocation.DirectoryBackground, item.KeyName, item.Verb.Raw,
                () => ElevatedDeleteHelper.InvokeElevated(ContextLocation.DirectoryBackground, item.KeyName));

            StatusMessage = outcome switch
            {
                MigrationOutcome.Success => $"已迁移 “{item.DisplayName}” 到当前用户",
                MigrationOutcome.CopiedButOriginalRemains =>
                    $"已复制到当前用户，原系统项仍存在（已被遮蔽），可稍后重试删除",
                _ => "当前用户已存在同名项，未执行迁移",
            };
        }
        catch (RegistryConflictException ex)
        {
            StatusMessage = $"迁移中止：{ex.Message}";
        }

        Editor = null;
        await RefreshAsync();
    }

    /// <summary>旧版遗留项/简单项升级为受管 CLI。</summary>
    [RelayCommand]
    private async Task ConvertToManagedAsync()
    {
        if (Editor is not { Target: { } target } || !Editor.Validate())
        {
            return;
        }

        try
        {
            _writer.ConvertToManaged(ContextLocation.DirectoryBackground, target.KeyName, target.Verb.Raw,
                Editor.DisplayName.Trim(), Editor.ToLaunchSpec(), Editor.EffectiveIconSpec(),
                Environment.ProcessPath ?? "RightMenu.App.exe", isLegacyUpgrade: target.IsLegacyManaged);
            StatusMessage = $"已{(target.IsLegacyManaged ? "升级" : "转换")} “{Editor.DisplayName.Trim()}”，原命令已备份到操作历史";
        }
        catch (RegistryConflictException)
        {
            StatusMessage = "检测到并发修改，已刷新";
        }

        Editor = null;
        await RefreshAsync();
    }

    /// <summary>撤销最近一次操作；冲突时不覆盖（规划 02 §7.3）。</summary>
    [RelayCommand]
    private async Task UndoLastAsync()
    {
        var last = _snapshots.List().FirstOrDefault();
        if (last is null)
        {
            return;
        }

        StatusMessage = _snapshots.Undo(last) switch
        {
            UndoResult.Success => $"已撤销：{last.Description}",
            UndoResult.Conflict => "无法撤销：目标键已被其他程序修改，未覆盖",
            _ => "该操作涉及系统范围，暂不支持自动撤销",
        };
        await RefreshAsync();
    }

    private void UpdateSummary()
    {
        var total = Items.Count;
        var editable = Items.Count(i => i.Verb.Capability is EditCapability.Full or EditCapability.Simple);
        var migratable = Items.Count(i => i.Verb.Capability == EditCapability.Migratable);
        StatusSummary = $"{total} 项 · {editable} 个可编辑 · {migratable} 个可迁移";
    }

    private static async Task LoadIconsAsync(IReadOnlyList<VerbItemViewModel> items)
    {
        foreach (var item in items)
        {
            if (item.Icon is not null)
            {
                continue;
            }

            var icon = await Task.Run(() => ShellIconService.Load(item.Verb.IconSpec, item.CommandExecutablePath));
            item.Icon = icon;
        }
    }
}
