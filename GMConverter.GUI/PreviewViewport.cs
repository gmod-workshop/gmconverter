using System.Drawing.Drawing2D;
using System.Numerics;
using GMConverter.Geometry;
using Bounds = GMConverter.Geometry.Bounds;

namespace GMConverter.GUI;

internal sealed class PreviewViewport : Control
{
    private const float SourceUnitsPerMeter = 39.3700787f;
    private const float GizmoRadius = 34.0f;
    private const float GizmoEndpointRadius = 9.0f;
    private const float FitPadding = 0.88f;
    private const float DefaultYaw = -0.75f;
    private const float DefaultPitch = 0.45f;
    private PreviewScene? _scene;
    private readonly List<GizmoHitTarget> _gizmoHitTargets = [];
    private float _yaw = DefaultYaw;
    private float _pitch = DefaultPitch;
    private float _zoom = 1.0f;
    private Point _lastMouse;
    private bool _dragging;

    public PreviewViewport()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(26, 28, 32);
        ForeColor = Color.FromArgb(220, 224, 230);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public void SetScene(Model model, IReadOnlyList<Mesh> physicsMeshes)
    {
        _scene = PreviewScene.From(model, physicsMeshes);
        _yaw = DefaultYaw;
        _pitch = DefaultPitch;
        _zoom = 1.0f;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);

        if (_scene is null)
        {
            DrawCenteredText(e.Graphics, "No preview loaded");
            return;
        }

        if (_scene.Triangles.Count == 0)
        {
            DrawCenteredText(e.Graphics, "No renderable geometry");
            return;
        }

        var viewport = ClientRectangle;
        viewport.Inflate(-12, -12);
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        var transform = CreateViewTransform();
        var fit = CalculateFit(viewport, transform);

        DrawModel(e.Graphics, transform, fit);
        DrawPhysicsMeshes(e.Graphics, transform, fit);
        DrawReferenceHeight(e.Graphics, transform, fit);
        DrawOrientationGizmo(e.Graphics, transform);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button is MouseButtons.Left)
        {
            if (TryActivateGizmo(e.Location))
            {
                return;
            }

            _dragging = true;
            _lastMouse = e.Location;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_dragging)
        {
            Cursor = HitTestGizmo(e.Location) is null ? Cursors.Default : Cursors.Hand;
        }

        if (!_dragging)
        {
            return;
        }

        var dx = e.X - _lastMouse.X;
        var dy = e.Y - _lastMouse.Y;
        _lastMouse = e.Location;

        _yaw += dx * 0.01f;
        _pitch = Math.Clamp(_pitch + dy * 0.01f, -1.35f, 1.35f);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button is MouseButtons.Left)
        {
            _dragging = false;
            Capture = false;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1f : 0.9f), 0.1f, 20f);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (!_dragging)
        {
            Cursor = Cursors.Default;
        }
    }

    private void DrawModel(Graphics graphics, Matrix4x4 transform, ViewFit fit)
    {
        var projected = _scene!.Triangles
            .Select(triangle => ProjectTriangle(triangle, transform, fit))
            .OrderByDescending(triangle => triangle.Depth)
            .ToArray();

        using var edgePen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f);

        foreach (var triangle in projected)
        {
            using var fill = new SolidBrush(triangle.Color);
            graphics.FillPolygon(fill, triangle.Points);
            graphics.DrawPolygon(edgePen, triangle.Points);
        }
    }

    private void DrawPhysicsMeshes(Graphics graphics, Matrix4x4 transform, ViewFit fit)
    {
        if (_scene!.PhysicsTriangles.Count == 0)
        {
            return;
        }

        var projected = _scene.PhysicsTriangles
            .Select(triangle => ProjectTriangle(triangle, transform, fit))
            .OrderByDescending(triangle => triangle.Depth)
            .ToArray();

        using var pen = new Pen(Color.FromArgb(235, 255, 194, 64), 1.5f);
        pen.LineJoin = LineJoin.Round;

        foreach (var triangle in projected)
        {
            using var fill = new SolidBrush(Color.FromArgb(42, triangle.Color));
            graphics.FillPolygon(fill, triangle.Points);
            graphics.DrawPolygon(pen, triangle.Points);
        }
    }

    private void DrawReferenceHeight(Graphics graphics, Matrix4x4 transform, ViewFit fit)
    {
        var ruler = _scene!.ReferenceRuler;
        var bottom = ProjectPoint(ruler.Bottom, transform, fit);
        var top = ProjectPoint(ruler.Top, transform, fit);
        var tick = Math.Clamp(Math.Min(fit.Viewport.Width, fit.Viewport.Height) * 0.018f, 7f, 14f);
        var direction = new PointF(top.X - bottom.X, top.Y - bottom.Y);
        var length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);

        if (length <= 0.001f)
        {
            return;
        }

        var normal = new PointF(-direction.Y / length * tick, direction.X / length * tick);
        var label = $"1 m / {SourceUnitsPerMeter:0.##}u";
        using var pen = new Pen(Color.FromArgb(245, 255, 214, 92), 2f);
        using var brush = new SolidBrush(Color.FromArgb(245, 255, 214, 92));
        using var labelBack = new SolidBrush(Color.FromArgb(170, 18, 20, 24));

        graphics.DrawLine(pen, bottom, top);
        graphics.DrawLine(pen, Offset(bottom, normal), Offset(bottom, Negate(normal)));
        graphics.DrawLine(pen, Offset(top, normal), Offset(top, Negate(normal)));

        var labelSize = graphics.MeasureString(label, Font);
        var labelPoint = new PointF(
            top.X + normal.X + 6f,
            top.Y + normal.Y - labelSize.Height / 2.0f);
        var labelRect = new RectangleF(
            labelPoint.X - 4f,
            labelPoint.Y - 2f,
            labelSize.Width + 8f,
            labelSize.Height + 4f);

        graphics.FillRectangle(labelBack, labelRect);
        graphics.DrawString(label, Font, brush, labelPoint);
    }

    private void DrawOrientationGizmo(Graphics graphics, Matrix4x4 transform)
    {
        _gizmoHitTargets.Clear();

        var center = new PointF(ClientRectangle.Right - 58f, ClientRectangle.Top + 54f);
        if (center.X < 52f || center.Y < 52f)
        {
            return;
        }

        var endpoints = BuildGizmoEndpoints(center, transform);
        using var background = new SolidBrush(Color.FromArgb(115, 15, 17, 20));
        using var borderPen = new Pen(Color.FromArgb(85, 255, 255, 255), 1f);
        graphics.FillEllipse(background, center.X - 44f, center.Y - 44f, 88f, 88f);
        graphics.DrawEllipse(borderPen, center.X - 44f, center.Y - 44f, 88f, 88f);

        DrawGizmoAxis(graphics, center, endpoints, GizmoView.NegativeX, GizmoView.PositiveX, Color.FromArgb(224, 84, 84));
        DrawGizmoAxis(graphics, center, endpoints, GizmoView.NegativeY, GizmoView.PositiveY, Color.FromArgb(86, 204, 126));
        DrawGizmoAxis(graphics, center, endpoints, GizmoView.NegativeZ, GizmoView.PositiveZ, Color.FromArgb(90, 156, 255));

        foreach (var endpoint in endpoints.OrderBy(endpoint => endpoint.Depth))
        {
            DrawGizmoEndpoint(graphics, endpoint);
            _gizmoHitTargets.Add(new GizmoHitTarget(endpoint.View, endpoint.HitBounds));
        }
    }

    private static GizmoEndpoint[] BuildGizmoEndpoints(PointF center, Matrix4x4 transform)
    {
        return
        [
            CreateGizmoEndpoint(center, transform, Vector3.UnitX, "X", GizmoView.PositiveX, Color.FromArgb(224, 84, 84)),
            CreateGizmoEndpoint(center, transform, -Vector3.UnitX, "-X", GizmoView.NegativeX, Color.FromArgb(170, 74, 74)),
            CreateGizmoEndpoint(center, transform, Vector3.UnitY, "Y", GizmoView.PositiveY, Color.FromArgb(86, 204, 126)),
            CreateGizmoEndpoint(center, transform, -Vector3.UnitY, "-Y", GizmoView.NegativeY, Color.FromArgb(64, 154, 96)),
            CreateGizmoEndpoint(center, transform, Vector3.UnitZ, "Z", GizmoView.PositiveZ, Color.FromArgb(90, 156, 255)),
            CreateGizmoEndpoint(center, transform, -Vector3.UnitZ, "-Z", GizmoView.NegativeZ, Color.FromArgb(64, 112, 190))
        ];
    }

    private static GizmoEndpoint CreateGizmoEndpoint(
        PointF center,
        Matrix4x4 transform,
        Vector3 axis,
        string label,
        GizmoView view,
        Color color)
    {
        var transformed = Vector3.TransformNormal(axis, transform);
        var projected = new PointF(transformed.X, -transformed.Z);
        var length = MathF.Sqrt(projected.X * projected.X + projected.Y * projected.Y);

        if (length <= 0.000001f)
        {
            projected = new PointF(0f, 0f);
        }
        else
        {
            projected = new PointF(projected.X / length, projected.Y / length);
        }

        var radius = GizmoEndpointRadius + (transformed.Y > 0 ? 1.5f : 0f);
        var position = new PointF(
            center.X + projected.X * GizmoRadius,
            center.Y + projected.Y * GizmoRadius);
        var hitBounds = new RectangleF(
            position.X - radius - 3f,
            position.Y - radius - 3f,
            (radius + 3f) * 2f,
            (radius + 3f) * 2f);

        return new GizmoEndpoint(view, label, position, transformed.Y, radius, color, hitBounds);
    }

    private static void DrawGizmoAxis(
        Graphics graphics,
        PointF center,
        IReadOnlyList<GizmoEndpoint> endpoints,
        GizmoView negativeView,
        GizmoView positiveView,
        Color color)
    {
        var negative = endpoints.First(endpoint => endpoint.View == negativeView);
        var positive = endpoints.First(endpoint => endpoint.View == positiveView);
        using var pen = new Pen(Color.FromArgb(160, color), 1.6f);

        graphics.DrawLine(pen, negative.Position, positive.Position);
        using var centerBrush = new SolidBrush(Color.FromArgb(210, 230, 232, 236));
        graphics.FillEllipse(centerBrush, center.X - 3f, center.Y - 3f, 6f, 6f);
    }

    private static void DrawGizmoEndpoint(Graphics graphics, GizmoEndpoint endpoint)
    {
        using var fill = new SolidBrush(Color.FromArgb(endpoint.Depth >= 0 ? 245 : 190, endpoint.Color));
        using var border = new Pen(Color.FromArgb(235, 246, 248, 252), endpoint.Depth >= 0 ? 1.3f : 1.0f);
        using var textBrush = new SolidBrush(Color.White);

        graphics.FillEllipse(
            fill,
            endpoint.Position.X - endpoint.Radius,
            endpoint.Position.Y - endpoint.Radius,
            endpoint.Radius * 2f,
            endpoint.Radius * 2f);
        graphics.DrawEllipse(
            border,
            endpoint.Position.X - endpoint.Radius,
            endpoint.Position.Y - endpoint.Radius,
            endpoint.Radius * 2f,
            endpoint.Radius * 2f);

        using var font = new Font(FontFamily.GenericSansSerif, endpoint.Label.Length > 1 ? 6.5f : 7.5f, FontStyle.Bold);
        var size = graphics.MeasureString(endpoint.Label, font);
        graphics.DrawString(
            endpoint.Label,
            font,
            textBrush,
            endpoint.Position.X - size.Width / 2.0f,
            endpoint.Position.Y - size.Height / 2.0f);
    }

    private ProjectedTriangle ProjectTriangle(PreviewTriangle triangle, Matrix4x4 transform, ViewFit fit)
    {
        var a = Transform(triangle.A, transform);
        var b = Transform(triangle.B, transform);
        var c = Transform(triangle.C, transform);
        var depth = (a.Y + b.Y + c.Y) / 3.0f;
        var normal = Vector3.Cross(b - a, c - a);
        var normalLength = normal.Length();
        normal = normalLength <= 0.000001f ? Vector3.UnitZ : normal / normalLength;
        var light = Math.Clamp(Math.Abs(Vector3.Dot(normal, Vector3.Normalize(new Vector3(0.4f, -0.7f, 0.8f)))), 0.25f, 1.0f);

        return new ProjectedTriangle(
            [
                ToScreen(a, fit),
                ToScreen(b, fit),
                ToScreen(c, fit)
            ],
            depth,
            Shade(triangle.Color, light));
    }

    private PointF ProjectPoint(Vector3 point, Matrix4x4 transform, ViewFit fit)
    {
        return ToScreen(Transform(point, transform), fit);
    }

    private Vector3 Transform(Vector3 point, Matrix4x4 transform)
    {
        return Vector3.Transform(point - _scene!.Center, transform);
    }

    private Matrix4x4 CreateViewTransform()
    {
        return Matrix4x4.CreateRotationZ(_yaw) * Matrix4x4.CreateRotationX(_pitch);
    }

    private ViewFit CalculateFit(Rectangle viewport, Matrix4x4 transform)
    {
        var projected = _scene!.FitPoints
            .Select(point => ProjectToPlane(Transform(point, transform)))
            .ToArray();

        if (projected.Length == 0)
        {
            return new ViewFit(viewport, 1.0f, new PointF(viewport.Left + viewport.Width / 2.0f, viewport.Top + viewport.Height / 2.0f));
        }

        var minX = projected[0].X;
        var minY = projected[0].Y;
        var maxX = projected[0].X;
        var maxY = projected[0].Y;

        foreach (var point in projected.Skip(1))
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        var width = Math.Max(maxX - minX, 0.000001f);
        var height = Math.Max(maxY - minY, 0.000001f);
        var scale = Math.Min(viewport.Width / width, viewport.Height / height) * FitPadding * _zoom;
        var projectedCenter = new PointF((minX + maxX) / 2.0f, (minY + maxY) / 2.0f);
        var screenCenter = new PointF(viewport.Left + viewport.Width / 2.0f, viewport.Top + viewport.Height / 2.0f);
        var offset = new PointF(
            screenCenter.X - projectedCenter.X * scale,
            screenCenter.Y - projectedCenter.Y * scale);

        return new ViewFit(viewport, scale, offset);
    }

    private static PointF Offset(PointF point, PointF offset)
    {
        return new PointF(point.X + offset.X, point.Y + offset.Y);
    }

    private static PointF Negate(PointF point)
    {
        return new PointF(-point.X, -point.Y);
    }

    private bool TryActivateGizmo(Point location)
    {
        var target = HitTestGizmo(location);
        if (target is null)
        {
            return false;
        }

        SetGizmoView(target.View);
        return true;
    }

    private GizmoHitTarget? HitTestGizmo(Point location)
    {
        for (var i = _gizmoHitTargets.Count - 1; i >= 0; i--)
        {
            var target = _gizmoHitTargets[i];
            if (target.Bounds.Contains(location))
            {
                return target;
            }
        }

        return null;
    }

    private void SetGizmoView(GizmoView view)
    {
        switch (view)
        {
            case GizmoView.PositiveX:
                _yaw = MathF.PI / 2.0f;
                _pitch = 0f;
                break;

            case GizmoView.NegativeX:
                _yaw = -MathF.PI / 2.0f;
                _pitch = 0f;
                break;

            case GizmoView.PositiveY:
                _yaw = 0f;
                _pitch = 0f;
                break;

            case GizmoView.NegativeY:
                _yaw = MathF.PI;
                _pitch = 0f;
                break;

            case GizmoView.PositiveZ:
                _yaw = 0f;
                _pitch = MathF.PI / 2.0f;
                break;

            case GizmoView.NegativeZ:
                _yaw = 0f;
                _pitch = -MathF.PI / 2.0f;
                break;
        }

        Cursor = Cursors.Hand;
        Invalidate();
    }

    private static PointF ToScreen(Vector3 point, ViewFit fit)
    {
        var projected = ProjectToPlane(point);
        return new PointF(
            fit.Offset.X + projected.X * fit.Scale,
            fit.Offset.Y + projected.Y * fit.Scale);
    }

    private static PointF ProjectToPlane(Vector3 point)
    {
        return new PointF(point.X, -point.Z);
    }

    private static Color Shade(Color color, float light)
    {
        return Color.FromArgb(
            185,
            Math.Clamp((int)(color.R * light), 0, 255),
            Math.Clamp((int)(color.G * light), 0, 255),
            Math.Clamp((int)(color.B * light), 0, 255));
    }

    private void DrawCenteredText(Graphics graphics, string text)
    {
        using var brush = new SolidBrush(ForeColor);
        var size = graphics.MeasureString(text, Font);
        graphics.DrawString(
            text,
            Font,
            brush,
            (Width - size.Width) / 2.0f,
            (Height - size.Height) / 2.0f);
    }

    private sealed record PreviewScene(
        IReadOnlyList<PreviewTriangle> Triangles,
        IReadOnlyList<PreviewTriangle> PhysicsTriangles,
        ReferenceRuler ReferenceRuler,
        IReadOnlyList<Vector3> FitPoints,
        Vector3 Center)
    {
        public static PreviewScene From(Model model, IReadOnlyList<Mesh> physicsMeshes)
        {
            var modelBounds = model.Bounds();
            var referenceRuler = ReferenceRuler.Create(modelBounds);
            var triangles = BuildTriangles(model.Meshes, materialColors: true);
            var physicsTriangles = BuildTriangles(physicsMeshes, materialColors: false);
            var fitPoints = model.Meshes
                .SelectMany(mesh => mesh.Positions)
                .Concat([referenceRuler.Bottom, referenceRuler.Top])
                .ToArray();

            return new PreviewScene(
                triangles,
                physicsTriangles,
                referenceRuler,
                fitPoints,
                (modelBounds.Min + modelBounds.Max) / 2.0f);
        }

        private static PreviewTriangle[] BuildTriangles(IEnumerable<Mesh> meshes, bool materialColors)
        {
            return meshes
                .SelectMany((mesh, meshIndex) => mesh.Submeshes.SelectMany(submesh =>
                {
                    var color = materialColors
                        ? MaterialColor(submesh.MaterialName)
                        : PhysicsColor(meshIndex);
                    return submesh.Triangles.Select(triangle => new PreviewTriangle(
                        mesh.Vertices[triangle.A].Position,
                        mesh.Vertices[triangle.B].Position,
                        mesh.Vertices[triangle.C].Position,
                        color));
                }))
                .ToArray();
        }

        private static Color MaterialColor(string? materialName)
        {
            var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(materialName ?? "default");
            return Color.FromArgb(
                90 + Math.Abs(hash & 0x7f),
                100 + Math.Abs((hash >> 8) & 0x7f),
                110 + Math.Abs((hash >> 16) & 0x7f));
        }

        private static Color PhysicsColor(int index)
        {
            var colors = new[]
            {
                Color.FromArgb(255, 194, 64),
                Color.FromArgb(82, 220, 164),
                Color.FromArgb(96, 174, 255),
                Color.FromArgb(240, 120, 160)
            };

            return colors[index % colors.Length];
        }
    }

    private sealed record ReferenceRuler(Vector3 Bottom, Vector3 Top)
    {
        public static ReferenceRuler Create(Bounds modelBounds)
        {
            var size = modelBounds.Max - modelBounds.Min;
            var maxDimension = Math.Max(size.X, Math.Max(size.Y, size.Z));
            var gap = Math.Max(maxDimension * 0.08f, 6.0f);
            var bottom = new Vector3(
                modelBounds.Max.X + gap,
                (modelBounds.Min.Y + modelBounds.Max.Y) / 2.0f,
                modelBounds.Min.Z);

            return new ReferenceRuler(bottom, bottom + Vector3.UnitZ * SourceUnitsPerMeter);
        }

    }

    private enum GizmoView
    {
        PositiveX,
        NegativeX,
        PositiveY,
        NegativeY,
        PositiveZ,
        NegativeZ
    }

    private sealed record GizmoEndpoint(
        GizmoView View,
        string Label,
        PointF Position,
        float Depth,
        float Radius,
        Color Color,
        RectangleF HitBounds);

    private sealed record GizmoHitTarget(GizmoView View, RectangleF Bounds);

    private sealed record ViewFit(Rectangle Viewport, float Scale, PointF Offset);

    private sealed record PreviewTriangle(Vector3 A, Vector3 B, Vector3 C, Color Color);

    private sealed record ProjectedTriangle(PointF[] Points, float Depth, Color Color);
}
