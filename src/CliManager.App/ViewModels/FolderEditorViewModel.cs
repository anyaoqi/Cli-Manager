using System.Windows.Media;
using CliManager.App.Services;
using CliManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CliManager.App.ViewModels;

/// <summary>
/// 菜单文件夹编辑面板 ViewModel。
/// </summary>
public partial class FolderEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _icon;

    [ObservableProperty]
    private ImageSource? _iconSource;

    [ObservableProperty]
    private bool _enabled = true;

    private FolderItem? _currentFolder;
    private Action? _onChangedCallback;

    public void Load(FolderItem folder, Action? onChanged = null)
    {
        _currentFolder = folder;
        _onChangedCallback = onChanged;

        Name = folder.Name;
        Icon = folder.Icon;
        Enabled = folder.Enabled;

        RefreshIcon();
    }

    public void ApplyToModel()
    {
        if (_currentFolder == null)
        {
            return;
        }

        _currentFolder.Name = Name.Trim();
        _currentFolder.Icon = string.IsNullOrWhiteSpace(Icon) ? null : Icon.Trim();
        _currentFolder.Enabled = Enabled;
    }

    partial void OnNameChanged(string value) => OnFieldChanged();
    partial void OnIconChanged(string? value) { RefreshIcon(); OnFieldChanged(); }
    partial void OnEnabledChanged(bool value) => OnFieldChanged();

    private void OnFieldChanged()
    {
        ApplyToModel();
        _onChangedCallback?.Invoke();
    }

    [RelayCommand]
    private void BrowseIcon()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择文件夹图标",
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

    private void RefreshIcon()
    {
        IconSource = ImageHelper.GetIconSource(Icon);
    }
}
