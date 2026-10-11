using System.Drawing;
using System.Windows.Forms;

namespace MonitorPilot;
internal sealed class TrayColors : ProfessionalColorTable
{
    public TrayColors(){UseSystemColors=false;}
    internal static Color Card=>Convert(Theme.Card);
    internal static Color Text=>Convert(Theme.Text);
    private static Color Convert(System.Windows.Media.SolidColorBrush brush)=>Color.FromArgb(brush.Color.R,brush.Color.G,brush.Color.B);
    public override Color ToolStripDropDownBackground=>Card;
    public override Color ImageMarginGradientBegin=>Card;
    public override Color ImageMarginGradientMiddle=>Card;
    public override Color ImageMarginGradientEnd=>Card;
    public override Color MenuItemSelected=>Convert(Theme.Soft);
    public override Color MenuItemBorder=>Convert(Theme.Accent);
    public override Color MenuBorder=>Convert(Theme.Border);
    public override Color SeparatorDark=>Convert(Theme.Border);
    public override Color SeparatorLight=>Card;
}

internal sealed class TrayRenderer : ToolStripProfessionalRenderer
{
    public TrayRenderer():base(new TrayColors()){RoundedEdges=false;}
    private static Color C(System.Windows.Media.SolidColorBrush brush)=>Color.FromArgb(brush.Color.R,brush.Color.G,brush.Color.B);
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){using var brush=new SolidBrush(TrayColors.Card);e.Graphics.FillRectangle(brush,e.AffectedBounds);}
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){using var brush=new SolidBrush(TrayColors.Card);e.Graphics.FillRectangle(brush,e.AffectedBounds);}
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var bounds=new Rectangle(2,0,e.Item.Width-4,e.Item.Height);using var brush=new SolidBrush(C(e.Item.Selected&&e.Item.Enabled?Theme.Soft:Theme.Card));e.Graphics.FillRectangle(brush,bounds);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=C(e.Item.Enabled?Theme.Text:Theme.Muted);base.OnRenderItemText(e);}
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e){e.ArrowColor=C(Theme.Muted);base.OnRenderArrow(e);}
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using var pen=new Pen(C(Theme.Border));e.Graphics.DrawLine(pen,8,e.Item.Height/2,e.Item.Width-8,e.Item.Height/2);}
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){using var pen=new Pen(C(Theme.Border));e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r=e.ImageRectangle;using var pen=new Pen(C(Theme.Accent),2);e.Graphics.DrawLines(pen,new[]{new Point(r.Left+2,r.Top+r.Height/2),new Point(r.Left+6,r.Bottom-3),new Point(r.Right-2,r.Top+3)});
    }
    public static void Apply(ToolStrip strip)
    {
        strip.Renderer=new TrayRenderer();strip.BackColor=TrayColors.Card;strip.ForeColor=TrayColors.Text;
        if(strip is ToolStripDropDownMenu menu){menu.ShowImageMargin=false;menu.ShowCheckMargin=true;}
        foreach(ToolStripItem item in strip.Items){item.ForeColor=TrayColors.Text;if(item is ToolStripMenuItem sub&&sub.HasDropDownItems)Apply(sub.DropDown);}
    }
}
