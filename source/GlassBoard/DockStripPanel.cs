using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GlassBoard;

// Keep the system controls at their normal size. Only application slots overlap.
public sealed class DockStripPanel : StackPanel
{
    public static readonly DependencyProperty IsApplicationProperty = DependencyProperty.RegisterAttached("IsApplication",typeof(bool),typeof(DockStripPanel),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static void SetIsApplication(UIElement child,bool value) { child.SetValue(IsApplicationProperty,value); }
    public static bool GetIsApplication(UIElement child) { return (bool)child.GetValue(IsApplicationProperty); }
    public double Budget = double.PositiveInfinity;
    private UIElement focus;
    private readonly Dictionary<UIElement,double> previousX=new Dictionary<UIElement,double>();
    public bool AnimateMoves=true;
    public void FocusApplication(UIElement child) { if(focus==child)return;focus=child;InvalidateArrange(); }
    public DockStripPanel() { MouseLeave += delegate { FocusApplication(null); }; }

    public static double[] Positions(int count,double width,double budget,int hovered)
    {
        var result=new double[Math.Max(0,count)];
        if(count<2)return result;
        double span=Math.Max(0,Math.Min(width*(count-1),budget-width));
        double step=span/(count-1);
        for(int i=0;i<count;i++)result[i]=i*step;
        if(hovered>=0 && hovered<count && step<width) {
            // Keep the hovered slot anchored; redistribute its neighbours on each side.
            double left=result[hovered];
            if(hovered>0){double end=Math.Max(0,left-width);for(int i=0;i<hovered;i++)result[i]=hovered==1?end:i*end/(hovered-1);}
            if(hovered<count-1){double start=Math.Min(span,left+width);int n=count-hovered-1;for(int i=hovered+1;i<count;i++)result[i]=n==1?start:start+(i-hovered-1)*(span-start)/(n-1);}
        }
        return result;
    }
    protected override Size MeasureOverride(Size constraint)
    {
        double width=0,height=0;
        foreach(UIElement child in InternalChildren){child.Measure(new Size(double.PositiveInfinity,constraint.Height));width+=child.DesiredSize.Width;height=Math.Max(height,child.DesiredSize.Height);}
        return new Size(Math.Min(width,Budget),height);
    }
    protected override Size ArrangeOverride(Size size)
    {
        var apps=InternalChildren.Cast<UIElement>().Where(c=>c is Button).ToList();
        double fixedWidth=InternalChildren.Cast<UIElement>().Where(c=>!(c is Button)).Sum(c=>c.DesiredSize.Width);
        double width=apps.Count==0?0:apps.Max(c=>c.DesiredSize.Width);
        double budget=Math.Max(width,Math.Min(apps.Count*width,size.Width-fixedWidth));
        int hovered=apps.IndexOf(focus);
        var positions=Positions(apps.Count,width,budget,hovered);
        double x=0;int index=0;
        foreach(UIElement child in InternalChildren) {
            double advance=child.DesiredSize.Width;
            if(child is Button) {
                double slot=index<apps.Count-1?positions[index+1]-positions[index]:width;
                // A run's relative positions include expansion of the focused item.
                advance=slot;
                SetZIndex(child,child==focus?10000:index+1);
                index++;
            } else SetZIndex(child,20000);
            if(AnimateMoves && previousX.TryGetValue(child,out double previous) && Math.Abs(previous-x)>.5 && child is FrameworkElement element && element.RenderTransform is TransformGroup group) {
                var shift=group.Children.OfType<TranslateTransform>().LastOrDefault();
                if(shift!=null)shift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(previous+shift.X-x,0,TimeSpan.FromMilliseconds(140)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
            }
            previousX[child]=x;
            child.Arrange(new Rect(x,Math.Max(0,(size.Height-child.DesiredSize.Height)/2),child.DesiredSize.Width,child.DesiredSize.Height));
            x+=advance;
        }
        foreach(var old in previousX.Keys.Where(c=>!InternalChildren.Contains(c)).ToList())previousX.Remove(old);
        return size;
    }
}
