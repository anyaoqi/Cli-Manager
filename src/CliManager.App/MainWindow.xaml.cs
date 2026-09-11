using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CliManager.App.Controls;
using CliManager.App.ViewModels;
using CliManager.App.Views;
using Wpf.Ui.Controls;

namespace CliManager.App;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    private Point _dragStartPoint;
    private TreeNodeViewModel? _draggedItem;

    // 拖拽过程视觉反馈：幻影、自动滚动、预计算落点
    private DragGhostAdorner? _dragGhost;
    private DispatcherTimer? _autoScrollTimer;
    private ScrollViewer? _treeScrollViewer;

    private TreeNodeViewModel? _pendingSibling;     // 插入参照节点
    private TreeNodeViewModel? _pendingTargetFolder; // 移入目标文件夹（拖到文件夹行）
    private bool _pendingInsertAbove;                // 插入到参照节点上方
    private bool _pendingSelfDrop;                   // 落点是自己：取消
    private TreeNodeViewModel? _feedbackNode;        // 当前携带落点视觉状态的节点

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        DataContext = ViewModel;

        // 文件夹投递高亮画刷：取系统强调色并压低透明度（缺省回退为蓝色）
        var accent = Application.Current.TryFindResource("SystemAccentColor") is Color color
            ? color
            : Color.FromRgb(0x00, 0x78, 0xD4);
        Resources["DropTargetFillBrush"] = new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B));
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeNodeViewModel node)
        {
            ViewModel.SelectedNode = node;
        }
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && ViewModel.DeleteCurrentCommand.CanExecute(null))
        {
            ViewModel.DeleteCurrentCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnTreePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d &&
            (FindAncestor<System.Windows.Controls.Primitives.ToggleButton>(d) != null ||
             FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(d) != null))
        {
            _draggedItem = null;
            return;
        }

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
            var source = _draggedItem;
            var data = new DataObject("TreeNodeViewModel", source);
            BeginDragVisual(source);
            try
            {
                DragDrop.DoDragDrop(MenuTree, data, DragDropEffects.Move);
            }
            finally
            {
                EndDragVisual();
                _draggedItem = null;
            }
        }
    }

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        if (_draggedItem == null || !e.Data.GetDataPresent("TreeNodeViewModel"))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        var target = GetNodeFromElement(e.OriginalSource as DependencyObject);
        UpdateDropFeedback(_draggedItem, target, e);
    }

    private void OnTreeDragLeave(object sender, DragEventArgs e)
    {
        ClearDropFeedback();
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        if (_draggedItem == null || _pendingSelfDrop)
        {
            ClearDropFeedback();
            e.Handled = true;
            return;
        }

        ViewModel.MoveDraggedNode(_draggedItem, _pendingSibling, _pendingInsertAbove, _pendingTargetFolder);
        ClearDropFeedback();
        e.Handled = true;
    }

    #region 拖拽视觉反馈

    private void BeginDragVisual(TreeNodeViewModel source)
    {
        source.IsDragging = true;

        var layer = AdornerLayer.GetAdornerLayer(MenuTree);
        if (layer != null)
        {
            _dragGhost = new DragGhostAdorner(MenuTree, BuildGhostContent(source));
            layer.Add(_dragGhost);
            var startPos = GetGhostPosition();
            if (startPos.HasValue)
            {
                _dragGhost.SetPosition(startPos.Value);
            }
        }

        // OLE 拖拽期间普通鼠标事件被吞掉，GiveFeedback 是唯一持续跟随光标的时机
        MenuTree.GiveFeedback += OnTreeGiveFeedback;

        _treeScrollViewer ??= FindDescendant<ScrollViewer>(MenuTree);
        _autoScrollTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _autoScrollTimer.Tick += OnAutoScrollTick;
        _autoScrollTimer.Start();
    }

    private void EndDragVisual()
    {
        _autoScrollTimer?.Stop();
        _autoScrollTimer = null;

        MenuTree.GiveFeedback -= OnTreeGiveFeedback;

        if (_dragGhost != null)
        {
            AdornerLayer.GetAdornerLayer(MenuTree)?.Remove(_dragGhost);
            _dragGhost = null;
        }

        if (_draggedItem != null)
        {
            _draggedItem.IsDragging = false;
        }

        ClearDropFeedback();
    }

    private void OnTreeGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        var position = GetGhostPosition();
        if (position.HasValue)
        {
            _dragGhost?.SetPosition(position.Value);
        }
    }

    private void OnAutoScrollTick(object? sender, EventArgs e)
    {
        if (_dragGhost == null)
        {
            return;
        }

        var ghostPos = GetGhostPosition();
        if (ghostPos.HasValue)
        {
            _dragGhost.SetPosition(ghostPos.Value);
        }

        // 靠近上下边缘时自动滚动，长列表也能拖到可视区外
        if (_treeScrollViewer == null)
        {
            return;
        }

        const double edge = 32.0;
        const double maxSpeed = 14.0;
        var p = GetCursorPosition(_treeScrollViewer);
        if (p.Y < 0)
        {
            return;
        }

        // 仅当光标在上下边缘附近（含略越出边界一小段）时滚动，离开区域即停止
        double height = _treeScrollViewer.ActualHeight;
        if (p.Y >= -edge && p.Y < edge)
        {
            double factor = Math.Clamp(1.0 - Math.Abs(p.Y) / edge, 0, 1);
            _treeScrollViewer.ScrollToVerticalOffset(_treeScrollViewer.VerticalOffset - maxSpeed * factor);
        }
        else if (p.Y <= height + edge && p.Y > height - edge)
        {
            double factor = Math.Clamp(1.0 - Math.Abs(p.Y - height) / edge, 0, 1);
            _treeScrollViewer.ScrollToVerticalOffset(_treeScrollViewer.VerticalOffset + maxSpeed * factor);
        }
    }

    private void UpdateDropFeedback(TreeNodeViewModel source, TreeNodeViewModel? target, DragEventArgs e)
    {
        _pendingSelfDrop = false;
        _pendingSibling = null;
        _pendingTargetFolder = null;
        _pendingInsertAbove = false;

        // 空白区域：移到根级末尾，不显示行级指示
        if (target == null)
        {
            ClearDropFeedback();
            return;
        }

        // 落回自己：取消
        if (ReferenceEquals(target, source))
        {
            ClearDropFeedback();
            _pendingSelfDrop = true;
            return;
        }

        if (source.IsFolder)
        {
            // 文件夹源：只能在根级重排，参照物映射为根级节点（文件夹内的工具 → 其所属文件夹）
            var sibling = target.IsFolder ? target : target.ParentNode ?? target;
            if (ReferenceEquals(sibling, source))
            {
                // 悬停在自己的子项上：落点无效，不显示指示
                ClearDropFeedback();
                _pendingSelfDrop = true;
                return;
            }

            bool above = IsPointerInUpperHalf(target, e);
            SetDropFeedback(sibling, intoFolder: false, above: above);
            _pendingSibling = sibling;
            _pendingInsertAbove = above;
        }
        else if (target.IsFolder)
        {
            // 工具源 + 文件夹目标：整行高亮，移入该文件夹
            SetDropFeedback(target, intoFolder: true, above: false);
            _pendingTargetFolder = target;
        }
        else
        {
            // 工具源 + 工具目标：按光标在行内的上下半区决定插入上/下方
            bool above = IsPointerInUpperHalf(target, e);
            SetDropFeedback(target, intoFolder: false, above: above);
            _pendingSibling = target;
            _pendingInsertAbove = above;
        }
    }

    private void SetDropFeedback(TreeNodeViewModel node, bool intoFolder, bool above)
    {
        if (!ReferenceEquals(_feedbackNode, node))
        {
            ClearDropFeedback();
            _feedbackNode = node;
        }

        _feedbackNode.IsDropTarget = intoFolder;
        _feedbackNode.IsDropAbove = !intoFolder && above;
        _feedbackNode.IsDropBelow = !intoFolder && !above;
    }

    private void ClearDropFeedback()
    {
        if (_feedbackNode != null)
        {
            _feedbackNode.IsDropTarget = false;
            _feedbackNode.IsDropAbove = false;
            _feedbackNode.IsDropBelow = false;
            _feedbackNode = null;
        }

        _pendingSibling = null;
        _pendingTargetFolder = null;
        _pendingInsertAbove = false;
        _pendingSelfDrop = false;
    }

    /// <summary>光标是否位于目标行内容区的上半部分（决定插入到上/下方）。</summary>
    private bool IsPointerInUpperHalf(TreeNodeViewModel node, DragEventArgs e)
    {
        var tvi = FindTreeViewItem(e.OriginalSource as DependencyObject);
        if (tvi == null)
        {
            return false;
        }

        var row = FindRowRoot(tvi);
        var p = row != null ? e.GetPosition(row) : e.GetPosition(tvi);
        double height = row?.ActualHeight ?? tvi.ActualHeight;
        return height > 0 && p.Y < height / 2;
    }

    /// <summary>
    /// 从命中元素向上找最内层 TreeViewItem（GetNodeFromElement 的容器对应物）。
    /// </summary>
    private static System.Windows.Controls.TreeViewItem? FindTreeViewItem(DependencyObject? element)
    {
        while (element != null && element is not System.Windows.Controls.TreeViewItem)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        return element as System.Windows.Controls.TreeViewItem;
    }

    /// <summary>
    /// 定位 TreeViewItem 的行根元素（DataTemplate 最外层 Border），用于把光标 Y
    /// 换算成"插入到上半 / 下半"。层级：TreeViewItem → 模板 → ContentPresenter → 行 Border。
    /// </summary>
    private static FrameworkElement? FindRowRoot(System.Windows.Controls.TreeViewItem tvi)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(tvi);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            bool isHeaderPresenter =
                current is ContentPresenter { Content: TreeNodeViewModel } ||
                current is ContentControl { Content: TreeNodeViewModel };
            if (isHeaderPresenter &&
                VisualTreeHelper.GetChildrenCount(current) > 0 &&
                VisualTreeHelper.GetChild(current, 0) is FrameworkElement row)
            {
                return row;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
        }

        return null;
    }

    private static UIElement BuildGhostContent(TreeNodeViewModel node)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        if (node.IconSource != null)
        {
            panel.Children.Add(new System.Windows.Controls.Image
            {
                Source = node.IconSource,
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = node.Title,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 280,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = TryFindBrush("TextFillColorPrimaryBrush", Colors.Black)
        });

        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Background = TryFindBrush("CardBackgroundFillColorDefaultBrush", Color.FromRgb(0xFB, 0xFB, 0xFB)),
            BorderBrush = TryFindBrush("AccentFillColorDefaultBrush", Color.FromRgb(0x00, 0x78, 0xD4)),
            BorderThickness = new Thickness(1),
            Opacity = 0.95,
            Effect = new DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 2,
                Direction = 270,
                Opacity = 0.35
            },
            Child = panel
        };
    }

    private Point? GetGhostPosition()
    {
        var p = GetCursorPosition(MenuTree);
        if (p.X < 0)
        {
            return null;
        }

        p.Offset(14, 14); // 右下偏移，避免遮挡光标焦点
        return p;
    }

    /// <summary>取光标位置并换算到指定元素坐标；失败时返回 (-1, -1)。</summary>
    private Point GetCursorPosition(FrameworkElement relativeTo)
    {
        if (GetCursorPos(out NativePoint pt))
        {
            try
            {
                return relativeTo.PointFromScreen(new Point(pt.X, pt.Y));
            }
            catch
            {
                // 窗口未渲染完成等场景
            }
        }

        return new Point(-1, -1);
    }

    private static Brush TryFindBrush(string key, Color fallback) =>
        Application.Current.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var result = FindDescendant<T>(child);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    #endregion

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

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T match)
            {
                return match;
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
        ViewModel.UpdateLivePreview();
    }
}
