using System.Drawing.Drawing2D;

namespace GreenFolder;

internal static class UiTheme
{
    public static readonly Color Canvas = Color.FromArgb(245, 247, 251), Ink = Color.FromArgb(29, 41, 57), Muted = Color.FromArgb(102, 112, 133), Border = Color.FromArgb(226, 232, 240), Accent = Color.FromArgb(37, 99, 235);
    public static GraphicsPath Round(RectangleF rect, float radius)
    {
        var p = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0) { p.AddRectangle(rect); return p; }
        p.AddArc(rect.Left, rect.Top, d, d, 180, 90); p.AddArc(rect.Right-d, rect.Top, d, d, 270, 90);
        p.AddArc(rect.Right-d, rect.Bottom-d, d, d, 0, 90); p.AddArc(rect.Left, rect.Bottom-d, d, d, 90, 90); p.CloseFigure(); return p;
    }
}
public class RoundedPanel : Panel
{
    [System.ComponentModel.DefaultValue(14)] public int Radius { get; set; } = 14;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Color BorderColor { get; set; } = UiTheme.Border;
    public RoundedPanel() { DoubleBuffered = true; BackColor = Color.White; ResizeRedraw = true; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Canvas); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = UiTheme.Round(new RectangleF(.5f,.5f,Math.Max(1,Width-1),Math.Max(1,Height-1)), Radius*DeviceDpi/96f);
        using var fill = new SolidBrush(BackColor); using var pen = new Pen(BorderColor);
        e.Graphics.FillPath(fill,shape); e.Graphics.DrawPath(pen,shape);
    }
}
public enum ButtonTone { Primary, Secondary, Quiet, Danger }
public sealed class ModernButton : Button
{
    bool hover, pressed;
    ButtonTone tone = ButtonTone.Secondary;
    [System.ComponentModel.DefaultValue(ButtonTone.Secondary)] public ButtonTone Tone { get => tone; set { tone=value; Invalidate(); } }
    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; Cursor=Cursors.Hand; UseMnemonic=false;
    }
    protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover=pressed=false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed=true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed=false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor=Enabled?Cursors.Hand:Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Color.White); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color fill=Color.White, ink=UiTheme.Ink, border=UiTheme.Border;
        if (!Enabled) { fill=Color.FromArgb(241,244,248); ink=Color.FromArgb(156,166,181); border=fill; }
        else if(tone==ButtonTone.Primary) { fill=pressed?Color.FromArgb(29,78,216):hover?Color.FromArgb(30,87,223):UiTheme.Accent; ink=Color.White; border=fill; }
        else if(tone==ButtonTone.Danger) { fill=hover?Color.FromArgb(255,241,242):Color.White; ink=Color.FromArgb(190,46,62); border=Color.FromArgb(244,203,209); }
        else { fill=hover?Color.FromArgb(239,244,252):Color.White; if(tone==ButtonTone.Quiet) border=fill; }
        using var shape=UiTheme.Round(new RectangleF(.5f,.5f,Math.Max(1,Width-1),Math.Max(1,Height-1)),8*DeviceDpi/96f);
        using var brush=new SolidBrush(fill); using var pen=new Pen(border);
        e.Graphics.FillPath(brush,shape); e.Graphics.DrawPath(pen,shape);
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        if(Focused && ShowFocusCues) { var focus=Rectangle.Inflate(ClientRectangle,-5,-5); ControlPaint.DrawFocusRectangle(e.Graphics,focus,ink,fill); }
    }
}
