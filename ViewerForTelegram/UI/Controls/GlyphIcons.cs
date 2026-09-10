using System.Drawing.Drawing2D;

namespace ViewerForTelegram.UI.Controls;

/// <summary>
/// Small vector icons drawn straight onto a control with GDI+ so they sit
/// pixel-centred and share one visual weight - font glyphs (⚙ ⭳ …) render
/// off-centre and inconsistently across the symbol fonts Windows falls back to.
/// Every method centres its drawing inside <paramref name="bounds"/>.
/// </summary>
internal static class GlyphIcons
{
    public static void DrawGear(Graphics g, Rectangle bounds, Color color)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(bounds.Width, bounds.Height);
        float cx = bounds.Left + bounds.Width / 2f;
        float cy = bounds.Top + bounds.Height / 2f;

        const int teeth = 8;
        float outer = s * 0.38f;
        float root = s * 0.29f;
        float hole = s * 0.145f;
        float step = (float)(Math.PI * 2 / teeth);
        const float tipHalf = 0.13f;   // fraction of one tooth period
        const float rootHalf = 0.20f;
        var rootRect = new RectangleF(cx - root, cy - root, root * 2, root * 2);

        using var path = new GraphicsPath { FillMode = FillMode.Alternate };
        for (int i = 0; i < teeth; i++)
        {
            float a = i * step;
            path.AddLine(Polar(cx, cy, root, a - step * rootHalf), Polar(cx, cy, outer, a - step * tipHalf));
            path.AddLine(Polar(cx, cy, outer, a - step * tipHalf), Polar(cx, cy, outer, a + step * tipHalf));
            path.AddLine(Polar(cx, cy, outer, a + step * tipHalf), Polar(cx, cy, root, a + step * rootHalf));
            // rounded valley along the root circle to the next tooth
            float v0 = a + step * rootHalf;
            float v1 = a + step * (1f - rootHalf);
            path.AddArc(rootRect, Deg(v0), Deg(v1 - v0));
        }
        path.CloseFigure();
        path.AddEllipse(cx - hole, cy - hole, hole * 2, hole * 2);

        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRefresh(Graphics g, Rectangle bounds, Color color)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(bounds.Width, bounds.Height);
        float cx = bounds.Left + bounds.Width / 2f;
        float cy = bounds.Top + bounds.Height / 2f;
        float r = s * 0.28f;
        float thick = Math.Max(2f, s * 0.11f);

        const float startDeg = -35f, sweepDeg = 280f;
        using (var pen = new Pen(color, thick) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
        {
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, startDeg, sweepDeg);
        }

        // filled arrowhead at the far end of the arc, pointing along the sweep
        float endRad = (startDeg + sweepDeg) * (float)Math.PI / 180f;
        var end = new PointF(cx + r * (float)Math.Cos(endRad), cy + r * (float)Math.Sin(endRad));
        float tanRad = endRad + (float)Math.PI / 2f;   // clockwise tangent
        float ah = s * 0.20f;
        var tip = new PointF(end.X + ah * (float)Math.Cos(tanRad), end.Y + ah * (float)Math.Sin(tanRad));
        float baseRad = tanRad + (float)Math.PI / 2f;
        float bw = ah * 0.9f;
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, new[]
        {
            tip,
            new PointF(end.X + bw * (float)Math.Cos(baseRad), end.Y + bw * (float)Math.Sin(baseRad)),
            new PointF(end.X - bw * (float)Math.Cos(baseRad), end.Y - bw * (float)Math.Sin(baseRad)),
        });
    }

    public static void DrawDownload(Graphics g, Rectangle bounds, Color color)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(bounds.Width, bounds.Height);
        float cx = bounds.Left + bounds.Width / 2f;
        float cy = bounds.Top + bounds.Height / 2f;

        float shaftHalf = Math.Max(1.2f, s * 0.07f);
        float shaftTop = cy - s * 0.30f;
        float shoulderY = cy - s * 0.06f;
        float headHalf = s * 0.19f;
        float tipY = cy + s * 0.16f;

        using var brush = new SolidBrush(color);

        // Shaft + arrow head as one polygon. Two separate fills (a rectangle and
        // a triangle) butting together leave a faint antialiased seam across the
        // arrow; a single fill has no interior edge.
        g.FillPolygon(brush, new[]
        {
            new PointF(cx - shaftHalf, shaftTop),
            new PointF(cx + shaftHalf, shaftTop),
            new PointF(cx + shaftHalf, shoulderY),
            new PointF(cx + headHalf, shoulderY),
            new PointF(cx, tipY),
            new PointF(cx - headHalf, shoulderY),
            new PointF(cx - shaftHalf, shoulderY),
        });

        // tray under it
        float barHalf = s * 0.28f;
        float barH = Math.Max(2f, s * 0.09f);
        g.FillRectangle(brush, cx - barHalf, cy + s * 0.26f, barHalf * 2, barH);
    }

    public static void DrawFolder(Graphics g, Rectangle bounds, Color color)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(bounds.Width, bounds.Height);
        float cx = bounds.Left + bounds.Width / 2f;
        float cy = bounds.Top + bounds.Height / 2f;

        float w = s * 0.62f;
        float h = s * 0.46f;
        float tabH = s * 0.11f;
        float left = cx - w / 2f;
        float top = cy - h / 2f;

        using var brush = new SolidBrush(color);
        // back tab
        g.FillRectangle(brush, left, top, w * 0.44f, tabH * 2f);
        // body
        var body = new RectangleF(left, top + tabH, w, h - tabH);
        g.FillRectangle(brush, body);
    }

    public static void DrawPlay(Graphics g, Rectangle bounds, Color color)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(bounds.Width, bounds.Height);
        float cx = bounds.Left + bounds.Width / 2f;
        float cy = bounds.Top + bounds.Height / 2f;

        // base nudged left so the triangle's visual mass sits on the centre
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, new[]
        {
            new PointF(cx - s * 0.17f, cy - s * 0.26f),
            new PointF(cx - s * 0.17f, cy + s * 0.26f),
            new PointF(cx + s * 0.27f, cy),
        });
    }

    public static void DrawPause(Graphics g, Rectangle bounds, Color color)
    {
        float s = Math.Min(bounds.Width, bounds.Height);
        float cx = bounds.Left + bounds.Width / 2f;
        float cy = bounds.Top + bounds.Height / 2f;

        float barW = Math.Max(3f, s * 0.15f);
        float barH = s * 0.52f;
        float gap = s * 0.16f;

        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, cx - gap / 2 - barW, cy - barH / 2, barW, barH);
        g.FillRectangle(brush, cx + gap / 2, cy - barH / 2, barW, barH);
    }

    public static void DrawCancel(Graphics g, Rectangle bounds, Color color)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(bounds.Width, bounds.Height);
        float cx = bounds.Left + bounds.Width / 2f;
        float cy = bounds.Top + bounds.Height / 2f;
        float d = s * 0.22f;

        using var pen = new Pen(color, Math.Max(2f, s * 0.12f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
        g.DrawLine(pen, cx - d, cy + d, cx + d, cy - d);
    }

    private static float Deg(float radians) => radians * 180f / (float)Math.PI;

    private static PointF Polar(float cx, float cy, float r, float angle) =>
        new((float)(cx + r * Math.Cos(angle)), (float)(cy + r * Math.Sin(angle)));
}
