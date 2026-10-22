namespace GreenFolder;

// Draw directly so progress remains visible during rapid updates and in exported previews.
public sealed class ProgressDisplay : Control
{
    readonly System.Windows.Forms.Timer animation = new() { Interval = 35 };
    int value, offset;
    ProgressBarStyle style;
    [System.ComponentModel.DefaultValue(0)] public int Minimum { get; set; }
    [System.ComponentModel.DefaultValue(1000)] public int Maximum { get; set; } = 1000;
    [System.ComponentModel.DefaultValue(0)] public int Value { get => value; set { this.value = Math.Clamp(value, Minimum, Maximum); Invalidate(); } }
    [System.ComponentModel.DefaultValue(ProgressBarStyle.Blocks)] public ProgressBarStyle Style
    {
        get => style;
        set { style = value; if (value == ProgressBarStyle.Marquee) animation.Start(); else animation.Stop(); Invalidate(); }
    }
    public ProgressDisplay()
    {
        DoubleBuffered = true; ResizeRedraw = true;
        AccessibleName = "任务进度"; AccessibleRole = AccessibleRole.ProgressBar;
        animation.Tick += (_, _) => { offset = (offset + 12) % Math.Max(1, Width + Width / 4); Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Parent?.BackColor ?? Color.White); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var rect = new RectangleF(0, 0, Math.Max(1, Width), Math.Max(1, Height));
        using var background = new SolidBrush(Color.FromArgb(232, 237, 245));
        using var foreground = new SolidBrush(UiTheme.Accent);
        using var track = UiTheme.Round(rect, Height / 2f);
        e.Graphics.FillPath(background, track);
        e.Graphics.SetClip(track);
        if (style == ProgressBarStyle.Marquee)
        {
            int width = Math.Max(12, Width / 4);
            e.Graphics.FillRectangle(foreground, new Rectangle(offset - width, 1, width, Math.Max(0, Height - 2)));
        }
        else
        {
            int filled = (int)((Width - 2d) * (value - Minimum) / Math.Max(1, Maximum - Minimum));
            if (filled > 0) e.Graphics.FillRectangle(foreground, 1, 1, filled, Math.Max(0, Height - 2));
        }
        e.Graphics.ResetClip();
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}
