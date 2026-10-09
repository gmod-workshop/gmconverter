using Avalonia;
using Avalonia.Controls;

namespace GMConverter.UI.Controls;

public partial class ScrollProgress : UserControl
{
    public static readonly StyledProperty<object?> ScrollContentProperty =
        AvaloniaProperty.Register<ScrollProgress, object?>(nameof(ScrollContent));

    public ScrollProgress()
    {
        InitializeComponent();
        UpdateScrollProgress();
    }

    public object? ScrollContent
    {
        get => GetValue(ScrollContentProperty);
        set => SetValue(ScrollContentProperty, value);
    }

    private void ContentScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateScrollProgress();
    }

    private void ScrollProgressTrack_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateScrollProgress();
    }

    private void UpdateScrollProgress()
    {
        var scrollableHeight = ContentScrollViewer.Extent.Height - ContentScrollViewer.Viewport.Height;
        var hasScrollableContent = scrollableHeight > 1;
        ScrollProgressTrack.IsVisible = hasScrollableContent;

        if (!hasScrollableContent)
        {
            ScrollProgressFill.Width = 0;
            return;
        }

        var progress = Math.Clamp((double)(ContentScrollViewer.Offset.Y / scrollableHeight), 0, 1);
        ScrollProgressFill.Width = ScrollProgressTrack.Bounds.Width * progress;
    }
}
