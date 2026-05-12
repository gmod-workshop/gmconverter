using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GMConverter.UI.Views;

internal enum PreviewGizmoView
{
    PositiveX,
    NegativeX,
    PositiveY,
    NegativeY,
    PositiveZ,
    NegativeZ
}

internal sealed class PreviewCameraGizmo : Control
{
    private const double GizmoRadius = 34.0;
    private const double EndpointRadius = 9.0;

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromArgb(120, 15, 17, 20));
    private static readonly Pen BackgroundPen = new(new SolidColorBrush(Color.FromArgb(85, 255, 255, 255)), 1.0);
    private static readonly IBrush CenterBrush = new SolidColorBrush(Color.FromArgb(220, 230, 232, 236));
    private static readonly Pen EndpointFrontPen = new(Brushes.White, 1.3);
    private static readonly Pen EndpointBackPen = new(new SolidColorBrush(Color.FromArgb(210, 246, 248, 252)), 1.0);

    private readonly List<GizmoHitTarget> hitTargets = [];
    private float heading;
    private float attitude;

    public event EventHandler<PreviewGizmoView>? ViewRequested;

    public PreviewCameraGizmo()
    {
        Width = 96;
        Height = 96;
        IsHitTestVisible = true;
    }

    public void SetCamera(float heading, float attitude)
    {
        this.heading = heading;
        this.attitude = attitude;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        hitTargets.Clear();

        var center = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
        if (center.X < 40.0 || center.Y < 40.0)
        {
            return;
        }

        context.DrawEllipse(BackgroundBrush, BackgroundPen, center, 44.0, 44.0);

        var endpoints = BuildEndpoints(center);
        DrawAxis(context, endpoints, PreviewGizmoView.NegativeX, PreviewGizmoView.PositiveX, Color.FromRgb(224, 84, 84));
        DrawAxis(context, endpoints, PreviewGizmoView.NegativeY, PreviewGizmoView.PositiveY, Color.FromRgb(86, 204, 126));
        DrawAxis(context, endpoints, PreviewGizmoView.NegativeZ, PreviewGizmoView.PositiveZ, Color.FromRgb(90, 156, 255));

        context.DrawEllipse(CenterBrush, null, center, 3.0, 3.0);

        foreach (var endpoint in endpoints.OrderBy(endpoint => endpoint.Depth))
        {
            DrawEndpoint(context, endpoint);
            hitTargets.Add(new GizmoHitTarget(endpoint.View, endpoint.HitBounds));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetPosition(this);
        var target = HitTest(point);
        if (target is null)
        {
            return;
        }

        ViewRequested?.Invoke(this, target.View);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Cursor = HitTest(e.GetPosition(this)) is null ? null : HandCursor;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Cursor = null;
    }

    private GizmoEndpoint[] BuildEndpoints(Point center)
    {
        var transform = CreateTransform();
        return
        [
            CreateEndpoint(center, transform, Vector3.UnitX, "X", PreviewGizmoView.PositiveX, Color.FromRgb(224, 84, 84)),
            CreateEndpoint(center, transform, -Vector3.UnitX, "-X", PreviewGizmoView.NegativeX, Color.FromRgb(170, 74, 74)),
            CreateEndpoint(center, transform, Vector3.UnitY, "Y", PreviewGizmoView.PositiveY, Color.FromRgb(86, 204, 126)),
            CreateEndpoint(center, transform, -Vector3.UnitY, "-Y", PreviewGizmoView.NegativeY, Color.FromRgb(64, 154, 96)),
            CreateEndpoint(center, transform, Vector3.UnitZ, "Z", PreviewGizmoView.PositiveZ, Color.FromRgb(90, 156, 255)),
            CreateEndpoint(center, transform, -Vector3.UnitZ, "-Z", PreviewGizmoView.NegativeZ, Color.FromRgb(64, 112, 190))
        ];
    }

    private Matrix4x4 CreateTransform()
    {
        var headingRadians = DegreesToRadians(-heading);
        var attitudeRadians = DegreesToRadians(attitude);
        return Matrix4x4.CreateRotationY(headingRadians) * Matrix4x4.CreateRotationX(attitudeRadians);
    }

    private static GizmoEndpoint CreateEndpoint(
        Point center,
        Matrix4x4 transform,
        Vector3 axis,
        string label,
        PreviewGizmoView view,
        Color color)
    {
        var transformed = Vector3.TransformNormal(axis, transform);
        var projected = new Avalonia.Vector(transformed.X, -transformed.Y);
        var length = Math.Sqrt(projected.X * projected.X + projected.Y * projected.Y);
        if (length > 0.000001)
        {
            projected /= length;
        }
        else
        {
            projected = default;
        }

        var radius = EndpointRadius + (transformed.Z > 0 ? 1.5 : 0.0);
        var position = new Point(
            center.X + projected.X * GizmoRadius,
            center.Y + projected.Y * GizmoRadius);
        var hitRadius = radius + 4.0;
        var hitBounds = new Rect(
            position.X - hitRadius,
            position.Y - hitRadius,
            hitRadius * 2.0,
            hitRadius * 2.0);

        return new GizmoEndpoint(view, label, position, transformed.Z, radius, new SolidColorBrush(color), hitBounds);
    }

    private static void DrawAxis(
        DrawingContext context,
        IReadOnlyList<GizmoEndpoint> endpoints,
        PreviewGizmoView negativeView,
        PreviewGizmoView positiveView,
        Color color)
    {
        var negative = endpoints.First(endpoint => endpoint.View == negativeView);
        var positive = endpoints.First(endpoint => endpoint.View == positiveView);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(160, color.R, color.G, color.B)), 1.6);
        context.DrawLine(pen, negative.Position, positive.Position);
    }

    private static void DrawEndpoint(DrawingContext context, GizmoEndpoint endpoint)
    {
        var opacity = endpoint.Depth >= 0 ? 1.0 : 0.74;
        var fill = new SolidColorBrush(((SolidColorBrush)endpoint.Fill).Color, opacity);
        var border = endpoint.Depth >= 0 ? EndpointFrontPen : EndpointBackPen;
        context.DrawEllipse(fill, border, endpoint.Position, endpoint.Radius, endpoint.Radius);

        var fontSize = endpoint.Label.Length > 1 ? 8.5 : 10.0;
        var text = new FormattedText(
            endpoint.Label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
            fontSize,
            Brushes.White);
        var textPoint = new Point(
            endpoint.Position.X - text.Width / 2.0,
            endpoint.Position.Y - text.Height / 2.0);
        context.DrawText(text, textPoint);
    }

    private GizmoHitTarget? HitTest(Point point)
    {
        for (var i = hitTargets.Count - 1; i >= 0; i--)
        {
            var target = hitTargets[i];
            if (target.Bounds.Contains(point))
            {
                return target;
            }
        }

        return null;
    }

    private static float DegreesToRadians(float degrees)
    {
        return degrees * MathF.PI / 180.0f;
    }

    private sealed record GizmoEndpoint(
        PreviewGizmoView View,
        string Label,
        Point Position,
        double Depth,
        double Radius,
        IBrush Fill,
        Rect HitBounds);

    private sealed record GizmoHitTarget(PreviewGizmoView View, Rect Bounds);
}
