using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using RightMenu.App.ViewModels;
using RightMenu.Core.Registry;
using Wpf.Ui.Controls;

namespace RightMenu.App;

/// <summary>
/// 设置对话框（规划 03 §2）：经典菜单实验性开关、操作历史、导出注册表。
/// </summary>
internal static class SettingsDialog
{
    internal static async Task ShowAsync(MainWindow owner, MainViewModel viewModel)
    {
        var panel = new StackPanel { Margin = new Thickness(4) };

        // 经典右键菜单（实验性）
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "经典右键菜单（实验性）",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
        });
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = $"非官方兼容方式，可能随 Windows 更新失效。当前系统：{GetWindowsVersion()}",
            FontSize = 12,
            Opacity = 0.62,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 6),
        });

        var toggle = new ToggleSwitch
        {
            IsChecked = viewModel.ClassicMenu.IsClassicMenuEnabled(),
            Content = "恢复 Windows 10 样式完整右键菜单",
            FontSize = 13,
        };
        var toggleHint = new System.Windows.Controls.TextBlock
        {
            FontSize = 12,
            Opacity = 0.62,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        toggle.Checked += (_, _) => ApplyClassicMenu(viewModel, true, toggleHint);
        toggle.Unchecked += (_, _) => ApplyClassicMenu(viewModel, false, toggleHint);
        panel.Children.Add(toggle);
        panel.Children.Add(toggleHint);

        var restartButton = new Wpf.Ui.Controls.Button
        {
            Content = "重启 Explorer 使其生效…",
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 12,
        };
        restartButton.Click += async (_, _) => await ConfirmRestartExplorerAsync();
        panel.Children.Add(restartButton);

        // 操作历史
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "最近操作",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 4),
        });
        var history = viewModel.Snapshots.List();
        var historyList = new System.Windows.Controls.ListBox { MaxHeight = 180, FontSize = 12 };
        foreach (var record in history.Take(15))
        {
            historyList.Items.Add($"{record.Timestamp:MM-dd HH:mm}  {record.Description}");
        }

        if (history.Count == 0)
        {
            historyList.Items.Add("（暂无操作记录）");
        }

        panel.Children.Add(historyList);

        // 导出 .reg（显式动作，规划 02 §7.3）
        var exportButton = new Wpf.Ui.Controls.Button
        {
            Content = "导出当前位置为 .reg 文件…",
            Margin = new Thickness(0, 12, 0, 0),
            FontSize = 12,
        };
        exportButton.Click += (_, _) => ExportReg(owner, viewModel);
        panel.Children.Add(exportButton);

        var messageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "设置",
            Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 480,
            },
            CloseButtonText = "关闭",
            MinWidth = 440,
        };
        await messageBox.ShowDialogAsync();
    }

    private static void ApplyClassicMenu(MainViewModel viewModel, bool enable, System.Windows.Controls.TextBlock hint)
    {
        if (enable == viewModel.ClassicMenu.IsClassicMenuEnabled())
        {
            return;
        }

        viewModel.ClassicMenu.SetClassicMenu(enable);
        hint.Text = "已写入注册表，重启 Explorer 后生效。若菜单未变化，说明当前 Windows 版本不支持此方式。";
        viewModel.CanUndo = true;
    }

    private static async Task ConfirmRestartExplorerAsync()
    {
        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = "重启 Explorer",
            Content = "将重启 Windows 资源管理器：所有文件管理器窗口会被关闭，桌面会短暂消失后自动恢复。",
            PrimaryButtonText = "重启 Explorer",
            CloseButtonText = "取消",
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // 进程已退出
            }
        }

        // Windows 通常会自动重启 shell；兜底显式启动
        await Task.Delay(1500);
        if (Process.GetProcessesByName("explorer").Length == 0)
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
    }

    private static void ExportReg(MainWindow owner, MainViewModel viewModel)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出注册表",
            Filter = "注册表文件 (*.reg)|*.reg",
            FileName = $"DirectoryBackground-shell-{DateTime.Now:yyyyMMdd-HHmmss}.reg",
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        var keyPath = $@"HKCU\Software\Classes\{ContextLocation.DirectoryBackground.RelativeShellPath}";
        var startInfo = new ProcessStartInfo
        {
            FileName = "reg.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("export");
        startInfo.ArgumentList.Add(keyPath);
        startInfo.ArgumentList.Add(dialog.FileName);
        startInfo.ArgumentList.Add("/y");

        using var process = Process.Start(startInfo);
        process?.WaitForExit();
        viewModel.StatusMessage = process?.ExitCode == 0
            ? $"已导出到 {dialog.FileName}"
            : "导出失败（reg.exe 返回错误）";
    }

    private static string GetWindowsVersion()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var displayVersion = key?.GetValue("DisplayVersion") as string ?? "?";
        var build = key?.GetValue("CurrentBuildNumber") as string ?? "?";
        return $"Windows {displayVersion}（Build {build}）";
    }
}
