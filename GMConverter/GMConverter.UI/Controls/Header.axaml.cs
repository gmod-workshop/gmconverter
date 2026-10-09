using Avalonia;
using Avalonia.Controls;

namespace GMConverter.UI.Controls.Common;

public partial class Header : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<Header, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<Header, string>(nameof(Description), string.Empty);

    public static readonly StyledProperty<object?> RightContentProperty =
        AvaloniaProperty.Register<Header, object?>(nameof(RightContent));

    public Header()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }
}
