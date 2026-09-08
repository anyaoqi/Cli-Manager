using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CliManager.App.ViewModels;
using CliManager.App.Views;
using Wpf.Ui.Controls;

namespace CliManager.App;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    private Point _dragStartPoint;
    private TreeNodeViewModel? _draggedItem;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        DataContext = ViewModel;
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeNodeViewModel node)
        {
            ViewModel.SelectedNode = node;
        }
    }

    private void OnTreePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _draggedItem = GetNodeFromElement(e.OriginalSource as DependencyObject);
    }

    private void OnTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedItem == null)
        {
            return;
        }

        Point currentPoint = e.GetPosition(this);
        Vector diff = _dragStartPoint - currentPoint;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var data = new DataObject("TreeNodeViewModel", _draggedItem);
            DragDrop.DoDragDrop(MenuTree, data, DragDropEffects.Move);
            _draggedItem = null;
        }
    }

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("TreeNodeViewModel"))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TreeNodeViewModel"))
        {
            return;
        }

        if (e.Data.GetData("TreeNodeViewModel") is not TreeNodeViewModel source)
        {
            return;
        }

        var targetNode = GetNodeFromElement(e.OriginalSource as DependencyObject);

        // 如果拖拽到目标文件夹：将工具放入该文件夹
        if (targetNode != null && targetNode.IsFolder && !source.IsFolder)
        {
            if (source.Tool != null && targetNode.Folder != null)
            {
                source.Tool.ParentId = targetNode.Folder.Id;
                ViewModel.BuildTree();
                ViewModel.SelectedNode = source;
            }
        }
        else if (targetNode != null && targetNode != source)
        {
            // 如果拖拽到同级工具：将 source 挪到 target 所在的父级
            string? targetParentId = targetNode.ParentNode?.Id;
            if (source.Tool != null)
            {
                source.Tool.ParentId = targetParentId;
                // 次序微调
                source.Tool.Order = targetNode.Order + 1;
                ViewModel.BuildTree();
                ViewModel.SelectedNode = source;
            }
        }
        else if (targetNode == null && !source.IsFolder && source.Tool != null)
        {
            // 拖到空白区域：移至顶级根菜单直出
            source.Tool.ParentId = null;
            ViewModel.BuildTree();
            ViewModel.SelectedNode = source;
        }

        e.Handled = true;
    }

    private static TreeNodeViewModel? GetNodeFromElement(DependencyObject? element)
    {
        while (element != null && element is not TreeView)
        {
            if (element is System.Windows.Controls.TreeViewItem tvi && tvi.Header is TreeNodeViewModel node)
            {
                return node;
            }
            if (element is FrameworkElement fe && fe.DataContext is TreeNodeViewModel vmNode)
            {
                return vmNode;
            }
            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        var settingsVm = new SettingsViewModel(ViewModel.Config);
        var win = new SettingsWindow(settingsVm)
        {
            Owner = this
        };
        win.ShowDialog();

        // 弹窗关闭后，刷新主界面可能已迁移的数据
        ViewModel.BuildTree();
    }
}