using System.Windows;
using System.Windows.Media;

namespace GlassBoard;

public static class TaskManagerIcon
{
    // Vector artwork stays crisp at any Dock icon size or Windows display scale.
    public static ImageSource Create()
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            var background = new LinearGradientBrush(Color.FromRgb(38,150,226), Color.FromRgb(25,78,160), 70);
            dc.DrawRoundedRectangle(background, new Pen(new SolidColorBrush(Color.FromArgb(110,255,255,255)), 0.8), new Rect(2,2,44,44), 11,11);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(48,8,35,73)), new Pen(new SolidColorBrush(Color.FromArgb(145,225,248,255)),1.2), new Rect(8,10,32,25), 3,3);
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(40,230,250,255)),0.6);
            for(int x=14;x<40;x+=6) dc.DrawLine(grid,new Point(x,11),new Point(x,34));
            for(int y=16;y<35;y+=6) dc.DrawLine(grid,new Point(9,y),new Point(39,y));
            var curve=Geometry.Parse("M 10,27 L 16,27 20,19 24,30 29,16 33,23 38,23");
            dc.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromRgb(198,255,246)),2.2) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},curve);
            var stand = new Pen(new SolidColorBrush(Color.FromRgb(226,248,255)),1.8) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
            dc.DrawLine(stand,new Point(24,36),new Point(24,39));
            dc.DrawLine(stand,new Point(18,39),new Point(30,39));
        }
        drawing.Freeze();var image=new DrawingImage(drawing);image.Freeze();return image;
    }
}
