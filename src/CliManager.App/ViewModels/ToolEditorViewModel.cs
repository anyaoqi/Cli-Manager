using System.Collections.ObjectModel;
using System.Windows.Media;
using CliManager.App.Services;
using CliManager.Core.Launch;
using CliManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CliManager.App.ViewModels;

public sealed record FolderOption(string? Id, string DisplayName);

/// <summary>
/// 工具配置编辑面板 ViewModel。
/// </summary>
public partial class ToolEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _icon;

    [ObservableProperty]
    private ImageSource? _iconSource;

    [ObservableProperty]
    private string _executable = string.Empty;

    [ObservableProperty]
    private string _argsText = string.Empty;

    [ObservableProperty]
    private string _host = TerminalHosts.WindowsTerminal;

    [ObservableProperty]
    private string? _customTemplate;

    [ObservableProperty]
    private bool _keepOpen;

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private FolderOption? _selectedFolder;

    [ObservableProperty]
    private string _compiledPreview = string.Empty;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private bool _isCustomHost;

    public ObservableCollection<FolderOption> FolderOptions { get; } = [];

    public ObservableCollection<string> HostOptions { get; } =
        new(TerminalHosts.All);

    public ObservableCollection<EnvVarItemViewModel> EnvList { get; } = [];

    private ToolItem? _currentTool;
    private Action? _onChangedCallback;
    private bool _isLoading;

    public void Load(ToolItem tool, IEnumerable<FolderItem> folders, Action? onChanged = null)
    {
        _isLoading = true;
        try
        {
            _currentTool = tool;
            _onChangedCallback = onChanged;

            Name = tool.Name;
            Icon = tool.Icon;
            Executable = tool.Executable;
            ArgsText = string.Join(" ", tool.Args);
            Host = tool.Host;
            CustomTemplate = tool.CustomTemplate;
            KeepOpen = tool.KeepOpen;
            Enabled = tool.Enabled;
            IsCustomHost = string.Equals(Host, TerminalHosts.Custom, StringComparison.OrdinalIgnoreCase);

            // 加载文件夹下拉项
            FolderOptions.Clear();
            FolderOptions.Add(new FolderOption(null, "📁 一级根菜单直出"));
            foreach (var f in folders)
            {
                FolderOptions.Add(new FolderOption(f.Id, $"📂 {f.Name}"));
            }

            SelectedFolder = FolderOptions.FirstOrDefault(opt => opt.Id == tool.ParentId) ?? FolderOptions[0];

            // 环境变量
            EnvList.Clear();
            foreach (var (k, v) in tool.Env)
            {
                EnvList.Add(new EnvVarItemViewModel(k, v));
            }

            RefreshIcon();
            RecomputePreview();
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void ApplyToModel()
    {
        if (_currentTool == null)
        {
            return;
        }

        _currentTool.Name = Name.Trim();
        _currentTool.Icon = string.IsNullOrWhiteSpace(Icon) ? null : Icon.Trim();
        _currentTool.Executable = Executable.Trim();
        _currentTool.Host = Host;
        _currentTool.CustomTemplate = CustomTemplate;
        _currentTool.KeepOpen = KeepOpen;
        _currentTool.Enabled = Enabled;
        _currentTool.ParentId = SelectedFolder?.Id;

        // 解析参数列表
        _currentTool.Args = ParseArgs(ArgsText);

        // 环境变量
        _currentTool.Env.Clear();
        foreach (var item in EnvList)
        {
            if (!string.IsNullOrWhiteSpace(item.Key))
            {
                _currentTool.Env[item.Key.Trim()] = item.Value;
            }
        }
    }

    partial void OnNameChanged(string value) => OnFieldChanged();
    partial void OnIconChanged(string? value) { RefreshIcon(); OnFieldChanged(); }
    partial void OnExecutableChanged(string value) { RefreshIcon(); OnFieldChanged(); }
    partial void OnArgsTextChanged(string value) => OnFieldChanged();
    partial void OnKeepOpenChanged(bool value) => OnFieldChanged();
    partial void OnEnabledChanged(bool value) => OnFieldChanged();
    partial void OnCustomTemplateChanged(string? value) => OnFieldChanged();
    partial void OnSelectedFolderChanged(FolderOption? value) => OnFieldChanged();

    partial void OnHostChanged(string value)
    {
        IsCustomHost = string.Equals(value, TerminalHosts.Custom, StringComparison.OrdinalIgnoreCase);
        OnFieldChanged();
    }

    private void OnFieldChanged()
    {
        if (_isLoading)
        {
            return;
        }

        ApplyToModel();
        RecomputePreview();
        _onChangedCallback?.Invoke();
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择可执行程序",
            Filter = "程序与脚本 (*.exe;*.cmd;*.bat;*.ps1)|*.exe;*.cmd;*.bat;*.ps1|所有文件 (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            Executable = dlg.FileName;
            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }
    }

    [RelayCommand]
    private void BrowseIcon()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择图标文件",
            Filter = "图标文件 (*.ico;*.exe;*.png)|*.ico;*.exe;*.png|所有文件 (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            Icon = dlg.FileName;
        }
    }

    [RelayCommand]
    private void ClearIcon()
    {
        Icon = null;
    }

    [RelayCommand]
    private void AddEnvVar()
    {
        EnvList.Add(new EnvVarItemViewModel("HTTP_PROXY", "http://127.0.0.1:7899"));
        OnFieldChanged();
    }

    [RelayCommand]
    private void RemoveEnvVar(EnvVarItemViewModel item)
    {
        EnvList.Remove(item);
        OnFieldChanged();
    }

    private void RefreshIcon()
    {
        if (!string.IsNullOrWhiteSpace(Icon))
        {
            ImageHelper.InvalidateCache(Icon);
        }
        if (!string.IsNullOrWhiteSpace(Executable))
        {
            ImageHelper.InvalidateCache(Executable);
        }

        IconSource = ImageHelper.GetToolIcon(Icon, Executable);
    }

    public void RecomputePreview()
    {
        if (_currentTool == null || string.IsNullOrWhiteSpace(Executable))
        {
            CompiledPreview = "(请输入可执行程序路径以预览启动命令)";
            ValidationMessage = null;
            return;
        }

        var tempTool = new ToolItem
        {
            Name = Name,
            Executable = Executable,
            Args = ParseArgs(ArgsText),
            Host = Host,
            CustomTemplate = CustomTemplate,
            KeepOpen = KeepOpen,
            Enabled = Enabled
        };

        foreach (var item in EnvList)
        {
            if (!string.IsNullOrWhiteSpace(item.Key))
            {
                tempTool.Env[item.Key.Trim()] = item.Value;
            }
        }

        var res = LaunchCompiler.Compile(tempTool);
        if (res.Success && !string.IsNullOrEmpty(res.Command))
        {
            CompiledPreview = res.Command;
        }
        else
        {
            CompiledPreview = "(命令编译不完全)";
        }

        if (res.Validation.Errors.Count > 0)
        {
            ValidationMessage = "❌ " + string.Join("；", res.Validation.Errors);
        }
        else if (res.Validation.Warnings.Count > 0)
        {
            ValidationMessage = "⚠️ " + string.Join("；", res.Validation.Warnings);
        }
        else
        {
            ValidationMessage = null;
        }
    }

    private static List<string> ParseArgs(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var list = new List<string>();
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        list.AddRange(parts);
        return list;
    }
}
