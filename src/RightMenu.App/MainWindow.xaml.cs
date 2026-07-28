using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using RightMenu.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace RightMenu.App;

public partial class MainWindow : FluentWindow, IUiDialogs
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(this);
        DataContext = _viewModel;

        // 先把全部主题资源切到当前系统主题（App.xaml 中的初始值仅为占位），
        // 再监听后续变化；否则窗口背景与画刷资源会处于混合主题状态（规划 03 §8）
        ApplicationThemeManager.ApplySystemTheme();
        SystemThemeWatcher.Watch(this);

        Loaded += async (_, _) =>
        {
            LogStartup();
            await _viewModel.RefreshAsync();
        };
    }

    // ---- IUiDialogs ----

    public async Task<bool> ConfirmDeleteAsync(string displayName, string source, string keyPath)
    {
        var messageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "删除菜单项",
            Content = $"将删除 “{displayName}”（{source}）。\n\n{keyPath}\n\n可在删除后通过“撤销”恢复。",
            PrimaryButtonText = "删除",
            PrimaryButtonAppearance = ControlAppearance.Danger,
            CloseButtonText = "取消",
        };
        return await messageBox.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    public async Task<bool> ConfirmMigrateAsync(string displayName)
    {
        var messageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "迁移到当前用户",
            Content = $"将把 “{displayName}” 复制到当前用户（HKCU），随后需要一次管理员确认以删除系统范围（HKLM）中的原项。\n\n" +
                      "如果拒绝管理员确认，副本仍会生效（原项被遮蔽），可稍后重试删除。",
            PrimaryButtonText = "迁移",
            CloseButtonText = "取消",
        };
        return await messageBox.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    public string? BrowseForExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 CLI 可执行文件",
            Filter = "可执行文件 (*.exe;*.com;*.cmd;*.bat)|*.exe;*.com;*.cmd;*.bat|所有文件 (*.*)|*.*",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    // ---- 交互 ----

    private void VerbList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedItem is { } item)
        {
            _viewModel.OpenEditorCommand.Execute(item);
        }
    }

    /// <summary>行尾 ⋯ 打开上下文菜单；Tag 传 VM 供 MenuItem 绑定命令。</summary>
    private void RowMenu_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button { ContextMenu: { } menu } button)
        {
            button.Tag = _viewModel;
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private async void Settings_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await SettingsDialog.ShowAsync(this, _viewModel);
    }

    /// <summary>快捷键（规划 03 §5）。</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var inTextInput = Keyboard.FocusedElement is System.Windows.Controls.TextBox or Wpf.Ui.Controls.TextBox;

        switch (e.Key)
        {
            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                SearchBox.Focus();
                e.Handled = true;
                return;
            case Key.N when Keyboard.Modifiers == ModifierKeys.Control:
                _viewModel.AddCliCommand.Execute(null);
                e.Handled = true;
                return;
            case Key.F5:
                _ = _viewModel.RefreshAsync();
                e.Handled = true;
                return;
            case Key.Escape when _viewModel.Editor is not null:
                _viewModel.CloseEditorCommand.Execute(null);
                VerbList.Focus();
                e.Handled = true;
                return;
            case Key.Enter when !inTextInput && _viewModel.Editor is null && _viewModel.SelectedItem is not null:
                _viewModel.OpenEditorCommand.Execute(_viewModel.SelectedItem);
                e.Handled = true;
                return;
            case Key.Space when !inTextInput && _viewModel.Editor is null && _viewModel.SelectedItem is not null:
                _viewModel.ToggleEnabledCommand.Execute(_viewModel.SelectedItem);
                e.Handled = true;
                return;
            case Key.Delete when !inTextInput && _viewModel.Editor is null && _viewModel.SelectedItem is not null:
                _viewModel.DeleteCommand.Execute(_viewModel.SelectedItem);
                e.Handled = true;
                return;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>冷启动打点（规划 04 Phase 0，仅本地日志）。</summary>
    private static void LogStartup()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RightMenu", "logs");
            Directory.CreateDirectory(logDir);
            var startupMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            File.AppendAllText(
                Path.Combine(logDir, "startup.log"),
                $"{DateTime.Now:O} window loaded {startupMs:F0}ms{Environment.NewLine}");
        }
        catch (IOException)
        {
            // 打点失败不影响使用
        }
    }
}
