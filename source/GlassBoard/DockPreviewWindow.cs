using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace GlassBoard;

public sealed class ApplicationPageChoice
{
    public string Title;
    public IntPtr Window;
    internal AutomationElement Element;
    public void Select() {
        RunningWindows.ActivateHandle(Window);
        try { if(Element.TryGetCurrentPattern(SelectionItemPattern.Pattern,out object pattern))((SelectionItemPattern)pattern).Select(); }
        catch(ElementNotAvailableException) {throw new InvalidOperationException("页面已经关闭，请重新悬停刷新列表");}
    }
}

public static class ApplicationPageChoices
{
    private static readonly ConcurrentDictionary<IntPtr,List<ApplicationPageChoice>> cache=new ConcurrentDictionary<IntPtr,List<ApplicationPageChoice>>();
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr h);
    public static async void Warm(IEnumerable<RunningWindow> windows) {
        try { await ReadAsync(windows.Where(w=>!IsIconic(w.Handle)).ToList()); }
        catch(Exception ex){Store.Trace("DOCK page warm: "+ex.Message);}
    }
    private static readonly ConcurrentDictionary<IntPtr,SemaphoreSlim> gates=new ConcurrentDictionary<IntPtr,SemaphoreSlim>();
    public static List<ApplicationPageChoice> Cached(IEnumerable<RunningWindow> windows) {
        return windows.SelectMany(w=>cache.TryGetValue(w.Handle,out var saved)?saved:new List<ApplicationPageChoice>()).ToList();
    }
    public static async Task<List<ApplicationPageChoice>> ReadAsync(IEnumerable<RunningWindow> windows) {
        var snapshot=windows.ToList();
        var results=await Task.WhenAll(snapshot.Select(ReadWindowAsync));return results.SelectMany(x=>x).ToList();
    }
    private static async Task<List<ApplicationPageChoice>> ReadWindowAsync(RunningWindow window) {
        if(IsIconic(window.Handle) && cache.TryGetValue(window.Handle,out var saved))return saved;
        var gate=gates.GetOrAdd(window.Handle,_=>new SemaphoreSlim(1,1));
        if(!await gate.WaitAsync(0))return Cached(new[]{window});
        // Each provider has its own gate. A stalled application never blocks another one's pages.
        var pending=Task.Run(()=> {
            try {
                var found=new List<ApplicationPageChoice>();
                var root=AutomationElement.FromHandle(window.Handle);
                var tabs=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TabItem));
                foreach(AutomationElement tab in tabs) {
                    string title=tab.Current.Name;
                    if(!string.IsNullOrWhiteSpace(title) && tab.TryGetCurrentPattern(SelectionItemPattern.Pattern,out object pattern))
                        found.Add(new ApplicationPageChoice{Title=title,Window=window.Handle,Element=tab});
                }
                if(found.Count>0){cache[window.Handle]=found;return found;}
                return Cached(new[]{window});
            }catch(ElementNotAvailableException){return Cached(new[]{window});}
            catch(InvalidOperationException){return Cached(new[]{window});}
            catch(COMException){return Cached(new[]{window});}
            finally{gate.Release();}
        });
        if(await Task.WhenAny(pending,Task.Delay(1500))!=pending)return Cached(new[]{window});
        return await pending;
    }

}

// Native DWM thumbnails hosted in a taskbar-style, non-activating flyout.
public sealed class DockPreviewWindow : Window
{
    [StructLayout(LayoutKind.Sequential)] private struct R { public int L,T,Right,B; }
    [StructLayout(LayoutKind.Sequential)] private struct S { public int W,H; }
    [StructLayout(LayoutKind.Sequential)] private struct Props { public uint Flags; public R Dest,Source; public byte Opacity; [MarshalAs(UnmanagedType.Bool)]public bool Visible; [MarshalAs(UnmanagedType.Bool)]public bool ClientOnly; }
    [DllImport("dwmapi.dll")]private static extern int DwmRegisterThumbnail(IntPtr dest,IntPtr source,out IntPtr thumbnail);
    [DllImport("dwmapi.dll")]private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
    [DllImport("dwmapi.dll")]private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail,ref Props props);
    [DllImport("dwmapi.dll")]private static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail,out S size);
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]private static extern int GetWindowLong(IntPtr h,int i);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")]private static extern int SetWindowLong(IntPtr h,int i,int value);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out R rect);
    private sealed class Card {public RunningWindow Window; public Border Canvas,Frame;public IntPtr Thumbnail;public Button Pages;}
    private readonly List<Card> cards=new List<Card>();
    private readonly StackPanel strip=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(5)};
    private readonly StackPanel body=new StackPanel();
    private readonly List<ContextMenu> pageMenus=new List<ContextMenu>();
    private List<ApplicationPageChoice> knownPages=new List<ApplicationPageChoice>();
    private List<RunningWindow> allWindows=new List<RunningWindow>();
    private int generation,offset;
    private bool dark;
    public bool HasThumbnail {get{return cards.Any(c=>c.Thumbnail!=IntPtr.Zero);}}
    public int TabCount {get;private set;}
    private bool PointerIn(IntPtr handle) {
        if(handle==IntPtr.Zero || !GetWindowRect(handle,out R r))return false;
        var p=System.Windows.Forms.Cursor.Position;return p.X>=r.L && p.X<r.Right && p.Y>=r.T && p.Y<r.B;
    }
    public bool ContainsPointer() {
        return PointerIn(new WindowInteropHelper(this).Handle) || pageMenus.Where(m=>m.IsOpen).Any(m=>PointerIn((PresentationSource.FromVisual(m) as HwndSource)?.Handle??IntPtr.Zero));
    }
    public bool MenuHovered {get{return pageMenus.Where(m=>m.IsOpen).Any(m=>PointerIn((PresentationSource.FromVisual(m) as HwndSource)?.Handle??IntPtr.Zero));}}
    public event Action PointerLeft;
    public event Action Chosen;
    public DockPreviewWindow(Window owner,bool light=true) {
        Owner=owner;Title="GlassBoard 窗口预览";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        ShowInTaskbar=false;ShowActivated=false;Topmost=true;SizeToContent=SizeToContent.WidthAndHeight;
        FontFamily=new FontFamily("Segoe UI");FontSize=12;
        dark=!light;
        Background=new SolidColorBrush(dark?Color.FromRgb(40,40,40):Color.FromRgb(244,244,244));
        Foreground=dark?Brushes.White:Brushes.Black;
        body.Children.Add(strip);Content=body;
        SourceInitialized+=delegate {
            var handle=new WindowInteropHelper(this).Handle;
            SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x08000080);
            int corner=2;DwmSetWindowAttribute(handle,33,ref corner,4);
            int border=dark?0x505050:0xD9D9D9;DwmSetWindowAttribute(handle,34,ref border,4);
            HwndSource.FromHwnd(handle).AddHook((IntPtr h,int m,IntPtr w,IntPtr l,ref bool handled)=>{if(m==33){handled=true;return new IntPtr(3);}return IntPtr.Zero;});
        };
        Closed+=delegate {generation++;ReleaseThumbnails();foreach(var menu in pageMenus)menu.IsOpen=false;};
        SizeChanged+=delegate {UpdateThumbnails();};
    }
    private void ReleaseThumbnails(){foreach(var card in cards)if(card.Thumbnail!=IntPtr.Zero){DwmUnregisterThumbnail(card.Thumbnail);card.Thumbnail=IntPtr.Zero;}}
    private Button FlatButton(object content) {
        var button=new Button{Content=content,Padding=new Thickness(6),Background=Brushes.Transparent,Foreground=Foreground,BorderThickness=new Thickness(0),Cursor=System.Windows.Input.Cursors.Arrow};
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(presenter);button.Template=new ControlTemplate(typeof(Button)){VisualTree=border};
        button.MouseEnter+=delegate{button.Background=new SolidColorBrush(dark?Color.FromArgb(24,255,255,255):Color.FromArgb(18,0,0,0));};
        button.MouseLeave+=delegate{button.Background=Brushes.Transparent;};return button;
    }
    public static string CleanTitle(string title) {
        return System.Text.RegularExpressions.Regex.Replace(title??"",@"\s*-\s*内存使用率\s*-.*$","").Trim();
    }
    private Card AddCard(RunningWindow window) {
        var card=new Card{Window=window};
        var panel=new StackPanel();
        var header=new Grid{Height=34,Margin=new Thickness(7,0,3,0)};
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(20)});header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});
        var icon=new Image{Source=RunningWindows.Icon(window),Width=16,Height=16};header.Children.Add(icon);
        var title=new TextBlock{Text=CleanTitle(window.Title),TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(5,0,4,0)};Grid.SetColumn(title,1);header.Children.Add(title);
        var close=FlatButton(new TextBlock{Text="\uE8BB",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=10});close.Width=26;close.Height=26;Grid.SetColumn(close,2);header.Children.Add(close);
        System.Windows.Automation.AutomationProperties.SetName(close,"关闭 "+CleanTitle(window.Title));
        close.MouseEnter+=delegate{close.Background=new SolidColorBrush(Color.FromRgb(196,43,28));close.Foreground=Brushes.White;};close.MouseLeave+=delegate{close.Foreground=Foreground;};
        close.Click+=delegate{ApplicationTasks.CloseWindow(window);Chosen?.Invoke();};panel.Children.Add(header);
        card.Canvas=new Border{Height=150,Margin=new Thickness(7,0,7,7),Background=new SolidColorBrush(dark?Color.FromRgb(32,32,32):Color.FromRgb(232,232,232))};
        card.Canvas.MouseLeftButtonUp+=delegate{RunningWindows.ActivateHandle(window.Handle);Chosen?.Invoke();};panel.Children.Add(card.Canvas);
        card.Pages=FlatButton("");card.Pages.Visibility=Visibility.Collapsed;card.Pages.HorizontalAlignment=HorizontalAlignment.Stretch;card.Pages.HorizontalContentAlignment=HorizontalAlignment.Left;card.Pages.Margin=new Thickness(7,0,7,4);panel.Children.Add(card.Pages);
        Action openPages=()=>{var menu=card.Pages.ContextMenu;if(menu==null)return;menu.PlacementTarget=card.Pages;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Top;menu.IsOpen=true;};
        card.Pages.Click+=delegate{openPages();};card.Pages.MouseEnter+=delegate{openPages();};
        card.Frame=new Border{Width=270,CornerRadius=new CornerRadius(5),Margin=new Thickness(1),Child=panel,Background=Brushes.Transparent};
        card.Frame.MouseEnter+=delegate{card.Frame.Background=new SolidColorBrush(dark?Color.FromArgb(15,255,255,255):Color.FromArgb(10,0,0,0));};card.Frame.MouseLeave+=delegate{card.Frame.Background=Brushes.Transparent;};
        strip.Children.Add(card.Frame);cards.Add(card);return card;
    }
    private void RenderCards() {
        ReleaseThumbnails();cards.Clear();strip.Children.Clear();foreach(var menu in pageMenus)menu.IsOpen=false;pageMenus.Clear();
        while(body.Children.Count>1)body.Children.RemoveAt(1);
        int capacity=Math.Max(1,Math.Min(4,(int)((SystemParameters.WorkArea.Width-30)/272)));
        foreach(var window in allWindows.Skip(offset).Take(capacity))AddCard(window);
        if(allWindows.Count>capacity) {
            var nav=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
            var prev=FlatButton("‹");prev.IsEnabled=offset>0;prev.Click+=delegate{offset=Math.Max(0,offset-capacity);RenderCards();};
            var next=FlatButton("›");next.IsEnabled=offset+capacity<allWindows.Count;next.Click+=delegate{offset+=capacity;RenderCards();};
            nav.Children.Add(prev);nav.Children.Add(new TextBlock{Text=(offset+1)+"–"+Math.Min(allWindows.Count,offset+capacity)+" / "+allWindows.Count,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,8,0)});nav.Children.Add(next);body.Children.Add(nav);
        }
        UpdatePageMenus();UpdateLayout();UpdateThumbnails();
    }
    // Used by isolated native-thumbnail checks before Populate is called.
    public void Preview(IntPtr source) {
        if(cards.Count==0){allWindows=new List<RunningWindow>{new RunningWindow{Handle=source,Title=""}};RenderCards();}
        UpdateThumbnails();
    }
    private void UpdateThumbnails() {
        var host=new WindowInteropHelper(this).Handle;if(host==IntPtr.Zero || !IsVisible)return;
        foreach(var card in cards) {
            if(card.Thumbnail==IntPtr.Zero && DwmRegisterThumbnail(host,card.Window.Handle,out card.Thumbnail)!=0){card.Thumbnail=IntPtr.Zero;continue;}
            DwmQueryThumbnailSourceSize(card.Thumbnail,out S size);
            if(card.Canvas.ActualWidth<1 || size.W<1 || size.H<1)continue;
            double ratio=Math.Min(card.Canvas.ActualWidth/size.W,card.Canvas.ActualHeight/size.H);
            var origin=card.Canvas.TranslatePoint(new Point(0,0),this);var transform=PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
            Point a=transform.Transform(new Point(origin.X+(card.Canvas.ActualWidth-size.W*ratio)/2,origin.Y+(card.Canvas.ActualHeight-size.H*ratio)/2));
            Point b=transform.Transform(new Point(origin.X+(card.Canvas.ActualWidth+size.W*ratio)/2,origin.Y+(card.Canvas.ActualHeight+size.H*ratio)/2));
            var props=new Props{Flags=1|4|8|16,Dest=new R{L=(int)a.X,T=(int)a.Y,Right=(int)b.X,B=(int)b.Y},Opacity=255,Visible=true,ClientOnly=false};
            DwmUpdateThumbnailProperties(card.Thumbnail,ref props);
        }
    }
    private void UpdatePageMenus() {
            foreach(var menu in pageMenus)menu.IsOpen=false;
            pageMenus.Clear();
            foreach(var card in cards) {
                card.Pages.ContextMenu=null;card.Pages.Visibility=Visibility.Collapsed;
                var choices=knownPages.Where(t=>t.Window==card.Window.Handle).ToList();if(choices.Count<2)continue;
                var menu=MenuTheme.Create(!dark);pageMenus.Add(menu);menu.MouseLeave+=delegate{PointerLeft?.Invoke();};
                foreach(var tab in choices) {
                    var selected=tab;var item=new MenuItem{Header=CleanTitle(tab.Title),MaxWidth=420};
                    item.Click+=async delegate{try{await Task.Run(()=>selected.Select());Chosen?.Invoke();}catch(Exception ex){Store.Trace("DOCK page select: "+ex.Message);}};menu.Items.Add(item);
                }
                card.Pages.Content=choices.Count+" 个标签页  ▾";card.Pages.Visibility=Visibility.Visible;card.Pages.ContextMenu=menu;
            }
    }
    public async void Populate(List<RunningWindow> windows) {
        int version=++generation;knownPages=ApplicationPageChoices.Cached(windows);TabCount=knownPages.Count;allWindows=windows;offset=0;RenderCards();
        try {
            var pending=ApplicationPageChoices.ReadAsync(windows);
            if(await Task.WhenAny(pending,Task.Delay(2500))!=pending)return;
            var tabs=await pending;if(version!=generation || !IsVisible)return;TabCount=tabs.Count;
            if(string.Join("|",knownPages.Select(t=>t.Window+":"+t.Title))==string.Join("|",tabs.Select(t=>t.Window+":"+t.Title)))return;
            knownPages=tabs;UpdatePageMenus();UpdateLayout();UpdateThumbnails();
        }catch(Exception ex){Store.Trace("DOCK page preview: "+ex.Message);}
    }
}
