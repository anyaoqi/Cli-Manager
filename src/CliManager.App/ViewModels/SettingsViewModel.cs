using System.Collections.ObjectModel;
using CliManager.App.Services;
using CliManager.Core.Migration;
using CliManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CliManager.App.ViewModels;

/// <summary>
/// 设置与迁移向导 ViewModel。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly CliConfig _config;

    [ObservableProperty]
    private string _configPath = string.Empty;

    [ObservableProperty]
    private bool _isClassicMenuEnabled;

    [ObservableProperty]
    private string _classicMenuStatusText = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<LegacyMenuItem> ScannedLegacyItems { get; } = [];

    public SettingsViewModel(CliConfig config)
    {
        _config = config;
        ConfigPath = ConfigStorageService.GetConfigFilePath();
        RefreshClassicMenuStatus();
    }

    public void RefreshClassicMenuStatus()
    {
        IsClassicMenuEnabled = ClassicMenuService.IsClassicMenuEnabled();
        ClassicMenuStatusText = IsClassicMenuEnabled
            ? "✅ Win10 经典菜单已启用（所有右键项直接显示在一级菜单中）"
            : "ℹ️ Win11 默认新版菜单生效中（静态项位于【显示更多选项】中）";
    }

    [RelayCommand]
    private void ToggleClassicMenu()
    {
        if (IsClassicMenuEnabled)
        {
            if (ClassicMenuService.RestoreWin11ModernMenu())
            {
                StatusMessage = "已切换为 Win11 默认菜单，请重启资源管理器生效。";
            }
        }
        else
        {
            if (ClassicMenuService.EnableClassicMenu())
            {
                StatusMessage = "已启用 Win10 经典菜单，请重启资源管理器生效。";
            }
        }

        RefreshClassicMenuStatus();
    }

    [RelayCommand]
    private void RestartExplorer()
    {
        ClassicMenuService.RestartExplorer();
        StatusMessage = "资源管理器已重新启动。";
    }

    [RelayCommand]
    public void ScanLegacyItems()
    {
        ScannedLegacyItems.Clear();
        var items = MigrationScanner.ScanAll();
        foreach (var item in items)
        {
            ScannedLegacyItems.Add(item);
        }

        StatusMessage = ScannedLegacyItems.Count > 0
            ? $"发现 {ScannedLegacyItems.Count} 个存量项（含旧版 RightMenu 与第三方配置）。"
            : "未发现可迁移的存量项。";
    }

    [RelayCommand]
    public void ImportSelectedLegacyItems()
    {
        int imported = MigrationService.MigrateSelected(_config, [.. ScannedLegacyItems]);
        ConfigStorageService.SaveConfig(_config);
        ScanLegacyItems();
        StatusMessage = $"成功迁移接管 {imported} 个存量右键项！";
    }
}
