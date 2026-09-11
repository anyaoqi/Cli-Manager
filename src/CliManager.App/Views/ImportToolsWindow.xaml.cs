using System.Windows;
using CliManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace CliManager.App.Views;

/// <summary>
/// 扫描并导入弹框：勾选要导入的 CLI 工具并选择目标分组（已有 / 新建 / 根层级）。
/// </summary>
public partial class ImportToolsWindow : FluentWindow
{
    public ImportToolsWindow(ImportToolsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnImportClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is ImportToolsViewModel vm && !vm.ImportCommand.CanExecute(null))
        {
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
