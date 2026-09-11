using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CliManager.App.Controls;

/// <summary>
/// 拖拽幻影装饰器：把被拖动节点的轻量快照（图标 + 标题卡片）渲染在
/// 装饰层上，并跟随光标移动，让用户拖动时能"看见"自己拖的是什么。
/// </summary>
public sealed class DragGhostAdorner : Adorner
{
    private readonly VisualCollection _visuals;
    private readonly ContentPresenter _presenter;
    private Point _position;

    public DragGhostAdorner(UIElement adornedElement, object content)
        : base(adornedElement)
    {
        _visuals = new VisualCollection(this);
        _presenter = new ContentPresenter
        {
            Content = content,
            IsHitTestVisible = false
        };
        _visuals.Add(_presenter);
        IsHitTestVisible = false;
    }

    /// <summary>更新幻影左上角位置（相对被装饰元素的坐标）。</summary>
    public void SetPosition(Point position)
    {
        _position = position;
        InvalidateArrange();
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size MeasureOverride(Size constraint)
    {
        _presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _presenter.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _presenter.Arrange(new Rect(_position, _presenter.DesiredSize));
        return finalSize;
    }
}
