using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using GlassBoard;

class Program
{
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,int message,IntPtr w,IntPtr l);
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
 // Shell extensions can retain native teardown threads. Exit only this isolated test process after all checks and application cleanup.
 [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
 [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process,uint code);
 static int checks;
 static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
 static string root = Path.Combine(Path.GetTempPath(), "GlassBoard-tests", "fixture-" + Guid.NewGuid().ToString("N"));
 [STAThread] static int Main()
 {
  int exit = 1;
  var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
  app.Dispatcher.InvokeAsync(async () => {
   try { await Run(); Console.WriteLine("PASS " + checks + " checks; fixture=" + root); exit = 0; }
   catch (Exception ex) { Console.WriteLine(ex); }
   finally { Console.Out.Flush(); Console.Error.Flush(); TerminateProcess(GetCurrentProcess(), (uint)exit); }
  });
  app.Run(); return exit;
 }
 static async Task Run()
 {
  Directory.CreateDirectory(root);
  typeof(Store).GetField("Folder").SetValue(null, Path.Combine(root,"app-config"));
  typeof(Store).GetField("FileName").SetValue(null, Path.Combine(root,"app-config","layout.json"));
  string before = Path.Combine(root, "原文件夹");
  string child = Path.Combine(before, "子目录", "内容.txt");
  Directory.CreateDirectory(Path.GetDirectoryName(child)); File.WriteAllText(child, "preserve me");
  var p = new Preferences();
  p.Groups = new List<string> { "工作" };
  p.Apps = new List<Entry> {
   new Entry { Name="原文件夹", Path=before, IsFolder=true, Group="工作", Pinned=true },
   new Entry { Name="内容", Path=child, LaunchTarget=child, IconPath=child, Group="工作" },
   new Entry { Name="相似前缀", Path=before+"-other\\file.txt", Group="工作" }
  };
  p.DockOrder = new List<string> { before }; p.Dock.ItemOrder = new List<string> { before };
  p.Workspaces.Add(new Workspace {Name="测试", Paths=new List<string>{child}});
  string after=Path.Combine(root,"新文件夹");
  string saved=Path.Combine(root,"layout.json");
  Action<Preferences> persist = candidate => { using(var f=File.Create(saved)) new DataContractJsonSerializer(typeof(Preferences)).WriteObject(f,candidate); };
  var renamed=await FolderRename.RenameAsync(p,before,"新文件夹",persist);
  Check(!Directory.Exists(before) && Directory.Exists(after),"disk rename");
  Check(File.ReadAllText(Path.Combine(after,"子目录","内容.txt"))=="preserve me","contents preserved");
  Check(renamed.Apps[0].Name=="新文件夹" && renamed.Apps[0].Path==after,"root display/path");
  Check(renamed.Apps[1].LaunchTarget.StartsWith(after+"\\") && renamed.Apps[1].IconPath.StartsWith(after+"\\"),"descendant paths");
  Check(renamed.Apps[2].Path==p.Apps[2].Path,"prefix boundary");
  Check(renamed.DockOrder[0]==after && renamed.Dock.ItemOrder[0]==after && renamed.Workspaces[0].Paths[0].StartsWith(after+"\\"),"dock/workspace");
  using(var f=File.OpenRead(saved)) Check(((Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(f)).Apps[0].Path==after,"saved readback");
  Directory.CreateDirectory(Path.Combine(root,"已存在"));
  foreach(string bad in new[]{"已存在","CON","../bad","bad.","bad ",""}) {
   bool rejected=false; try { await FolderRename.RenameAsync(renamed,after,bad,persist); } catch {rejected=true;}
   Check(rejected && Directory.Exists(after),"reject "+bad);
  }
  bool rollback=false;
  try { await FolderRename.RenameAsync(renamed,after,"保存失败",candidate=>{throw new IOException("simulated failure");}); } catch(IOException){rollback=true;}
  Check(rollback && Directory.Exists(after) && !Directory.Exists(Path.Combine(root,"保存失败")),"save rollback");
  Check(renamed.Apps[0].Path==after,"original preferences unchanged on failure");
  var same=await FolderRename.RenameAsync(renamed,after,"新文件夹",persist); Check(object.ReferenceEquals(same,renamed),"no-op");
  string caseOld=Path.Combine(root,"CaseFolder"); Directory.CreateDirectory(caseOld);
  var casePrefs=new Preferences(); casePrefs.Apps.Add(new Entry {Name="CaseFolder",Path=caseOld,IsFolder=true});
  var caseResult=await FolderRename.RenameAsync(casePrefs,caseOld,"casefolder",persist);
  Check(Directory.GetDirectories(root).Any(path=>Path.GetFileName(path)=="casefolder") && caseResult.Apps[0].Name=="casefolder","case-only rename");
  var c=(Controller)Activator.CreateInstance(typeof(Controller),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{false},null);
  renamed.Apps[1].IconPath=""; renamed.Hotkeys.ForEach(h=>h.Enabled=false); renamed.BoardHideSearch=true; c.Prefs=renamed;
  Check(c.Menu(renamed.Apps[0]).Items.OfType<MenuItem>().Any(m=>(string)m.Header=="重命名文件夹（同步磁盘）"),"real-folder menu");
  Check(!c.Menu(renamed.Apps[1]).Items.OfType<MenuItem>().Any(m=>(string)m.Header=="重命名文件夹（同步磁盘）"),"file excluded");
  c.Board.Open(); c.Board.SyncGroups(); c.Board.Render();
  var bar=(WrapPanel)typeof(BoardWindow).GetField("folderBar",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.Board);
  Check(bar.Children.OfType<Border>().Any(b=>(string)b.Tag=="工作" && b.ContextMenu.Items.OfType<MenuItem>().Any(m=>(string)m.Header=="重命名分组")),"group context menu");
  Check(bar.Children.OfType<Border>().First(b=>(string)b.Tag=="未分组").ContextMenu!=null,"unassigned context menu enabled");
  var groupBox=bar.Children.OfType<Border>().First(b=>(string)b.Tag=="工作");
  var label=((StackPanel)groupBox.Child).Children.OfType<TextBlock>().First();
  var rightClick=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Right){RoutedEvent=UIElement.PreviewMouseRightButtonUpEvent};
  rightClick.Source=label; typeof(BoardWindow).GetMethod("BoardBlankMenu",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c.Board,new object[]{c.Board,rightClick});
  Check(!rightClick.Handled,"group right click not intercepted by new-file menu");
  foreach(var builtin in bar.Children.OfType<Border>().Where(b=>(string)b.Tag=="全部" || (string)b.Tag=="未分组")){
   var builtinText=((StackPanel)builtin.Child).Children.OfType<TextBlock>().First();
   var builtinClick=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Right){RoutedEvent=UIElement.PreviewMouseRightButtonUpEvent};
   builtinClick.Source=builtinText; typeof(BoardWindow).GetMethod("BoardBlankMenu",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c.Board,new object[]{c.Board,builtinClick}); Check(builtinClick.Handled == ((string)builtin.Tag=="全部"),"group right click follows its menu");
   Check(typeof(BoardWindow).GetField("iconMenu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.Board)==null,"no new-file menu on builtin");
  }
  groupBox.ContextMenu.IsOpen=false; SubmitNextDialog("项目资料");
  bar.Children.OfType<Border>().First(b=>(string)b.Tag=="工作").ContextMenu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
  Check(c.Prefs.Groups.Contains("项目资料") && c.Board.CurrentGroup=="项目资料" && c.Prefs.Apps[0].Group=="项目资料","group menu submit and refresh");
  var heading=(TextBlock)typeof(BoardWindow).GetField("groupHeading",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.Board);
  Check(heading.Text=="项目资料","heading follows group rename");
  c.Board.SelectRenamedGroup("未分组"); Check(heading.Text=="未分组","heading follows tab switch");
  c.Board.SelectRenamedGroup("项目资料");
  SubmitNextDialog("界面改名成功");
  c.Menu(c.Prefs.Apps[0]).Items.OfType<MenuItem>().First(m=>(string)m.Header=="重命名文件夹（同步磁盘）").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
  for(int i=0;i<100 && c.Board.ModalBusy;i++) await Task.Delay(30);
  Console.WriteLine("UI result busy="+c.Board.ModalBusy+" enabled="+c.Board.IsEnabled+" dock="+c.Dock.IsEnabled+" name="+c.Prefs.Apps[0].Name+" path="+c.Prefs.Apps[0].Path); Check(!c.Board.ModalBusy && c.Board.IsEnabled && c.Dock.IsEnabled && c.Prefs.Apps[0].Name=="界面改名成功" && Directory.Exists(c.Prefs.Apps[0].Path),"folder menu submit and refresh");
  c.Prefs.Groups.AddRange(new[]{"AI","网盘"}); c.Board.SyncGroups(); c.Board.Render();
  c.ReorderGroup("网盘","项目资料",true);
  Check(c.Prefs.Groups.SequenceEqual(new[]{"网盘","项目资料","AI"}),"group move before");
  c.ReorderGroup("网盘","AI",false);
  Check(c.Prefs.Groups.SequenceEqual(new[]{"项目资料","AI","网盘"}) && c.Prefs.Apps[0].Group=="项目资料","group move after preserves members");
  c.UndoBoard(); Check(c.Prefs.Groups[0]=="网盘","group move undo");
  c.RedoBoard(); Check(c.Prefs.Groups[2]=="网盘","group move redo");
  c.ReorderGroup("全部","AI",false);
  Check(c.Prefs.Groups.SequenceEqual(new[]{"项目资料","AI","网盘"}),"all view remains fixed");
  c.Board.UpdateLayout(); var targetBox=bar.Children.OfType<Border>().First(b=>(string)b.Tag=="项目资料");
  var dragData=new DataObject("GlassBoard.GroupReorder","网盘");
  var dragArgs=(DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{dragData,DragDropKeyStates.LeftMouseButton,DragDropEffects.Move,targetBox,new Point(1,1)},null);
  dragArgs.RoutedEvent=DragDrop.DragOverEvent; targetBox.RaiseEvent(dragArgs);
  Console.WriteLine("drag handled="+dragArgs.Handled+" effect="+dragArgs.Effects+" border="+targetBox.BorderThickness+" width="+targetBox.ActualWidth+" pos="+dragArgs.GetPosition(targetBox)); Check(dragArgs.Handled && dragArgs.Effects==DragDropEffects.Move && targetBox.BorderThickness.Left==3,"group drag insertion feedback");
  dragArgs.Handled=false; dragArgs.RoutedEvent=DragDrop.DropEvent; targetBox.RaiseEvent(dragArgs);
  Check(c.Prefs.Groups[0]=="网盘","group drop routed handler");
  persist(c.Prefs);
  using(var f=File.OpenRead(saved)) {
   var reloaded=(Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(f); reloaded.Normalize();
   Check(reloaded.Groups.SequenceEqual(c.Prefs.Groups),"group order survives reload/normalize");
  }
  Check(!typeof(Controller).GetMethods().Any(m=>m.Name=="BuildCreatorSupport"),"creator page removed");
  Check(!typeof(Controller).Assembly.GetManifestResourceNames().Any(n=>n.EndsWith("c1.dat") || n.EndsWith("c2.dat")),"payment resources removed");
  var menuMethod=typeof(BoardWindow).GetMethod("MoreMenu",BindingFlags.Instance|BindingFlags.NonPublic);
  menuMethod.Invoke(c.Board,null);
  Check(!Application.Current.Windows.OfType<Window>().SelectMany(w=>Descendants(w)).OfType<TextBlock>().Any(t=>t.Text.Contains("森霖") || t.Text.Contains("赞助")),"no creator footer");
  typeof(BoardWindow).GetMethod("NewFileMenu",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c.Board,null);
  var newMenu=(ContextMenu)typeof(BoardWindow).GetField("iconMenu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.Board);
  var createGroupItem=newMenu.Items.OfType<MenuItem>().First(m=>(string)m.Header=="新建分组");
  Check(createGroupItem!=null,"blank menu create-group option");
  int oldCount=c.Prefs.Groups.Count;
  SubmitNextDialog("新建的分组");
  createGroupItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
  Check(c.Prefs.Groups.Count==oldCount+1 && c.Prefs.Groups.Last()=="新建的分组","new group added");
  Check(c.Board.CurrentGroup=="新建的分组" && heading.Text=="新建的分组","new group selected and heading refreshed");
  Check(c.Prefs.Apps.Count==3 && !c.Prefs.Apps.Any(a=>a.Group=="新建的分组"),"new group leaves existing entries intact");
  persist(c.Prefs);
  using(var f=File.OpenRead(saved)) Check(((Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(f)).Groups.Contains("新建的分组"),"new group saved");
  c.UndoBoard(); Check(!c.Prefs.Groups.Contains("新建的分组"),"new group undo");
  c.Board.SelectRenamedGroup("未分组");
  var defaultTab=bar.Children.OfType<Border>().First(b=>(string)b.Tag=="未分组");
  SubmitNextDialog("收纳箱");
  defaultTab.ContextMenu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
  Check(c.Prefs.GroupLabel("未分组")=="收纳箱" && heading.Text=="收纳箱" && c.Board.CurrentGroup=="未分组","unassigned rename preserves selection and updates heading");
  Check(FeatureRules.InGroup(new Entry { Name="新入口",Path="sample" },"未分组"),"unassigned membership preserved");
  Check(((StackPanel)bar.Children.OfType<Border>().First(b=>(string)b.Tag=="未分组").Child).Children.OfType<TextBlock>().First().Text.StartsWith("收纳箱"),"unassigned tab label refreshed");
  c.ReorderGroup("未分组","AI",false);
  Check(c.Prefs.BoardGroups().IndexOf("未分组")==c.Prefs.BoardGroups().IndexOf("AI")+1,"unassigned draggable after custom group");
  c.UndoBoard(); Check(c.Prefs.UnassignedPosition==0 && c.Prefs.GroupLabel("未分组")=="收纳箱","unassigned reorder undo");
  c.UndoBoard(); Check(c.Prefs.GroupLabel("未分组")=="未分组","unassigned rename undo");
  FeatureRules.RenameGroup(c.Prefs,"未分组","收纳箱");
  c.ReorderGroup("未分组","AI",false);
  persist(c.Prefs);
  using(var f=File.OpenRead(saved)) {
   var loaded=(Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(f); loaded.Normalize();
   Check(loaded.GroupLabel("未分组")=="收纳箱" && loaded.BoardGroups().SequenceEqual(c.Prefs.BoardGroups()),"unassigned label/order survives reload");
  }
  bool duplicate=false;try {FeatureRules.RenameGroup(c.Prefs,"AI","收纳箱");}catch(ArgumentException){duplicate=true;}
  Check(duplicate,"custom group cannot duplicate unassigned label");
  FeatureRules.RenameGroup(c.Prefs,"未分组","未分组"); Check(c.Prefs.GroupLabel("未分组")=="未分组","restore original unassigned name");
  string nativeFile=Path.Combine(root,"系统菜单测试.txt"); File.WriteAllText(nativeFile,"unchanged");
  using(var nativeMenu=new NativeFileMenu(nativeFile)) {
   var commands=nativeMenu.Commands(); Console.WriteLine("Native menu: "+string.Join(" / ",commands));
   Check(commands.Any(x=>x.EndsWith("|copy")) && commands.Any(x=>x.EndsWith("|cut")) && commands.Any(x=>x.EndsWith("|delete")),"system file menu supplies copy/cut/delete");
   Check(commands.Any(x=>x.EndsWith("|rename")) && commands.Any(x=>x.Contains("GlassBoard 管理")),"system rename and GlassBoard management entry");
   IntPtr owner=new System.Windows.Interop.WindowInteropHelper(c.Board).Handle;
   var cancel=Task.Run(async()=>{await Task.Delay(500);PostMessage(owner,0x1F,IntPtr.Zero,IntPtr.Zero);});
   Check(nativeMenu.Show(c.Board)==null,"native popup cancellation");
   await cancel;
  }
  Check(File.ReadAllText(nativeFile)=="unchanged","opening system menu does not modify file");
  using(var nativeMenu=new NativeFileMenu(c.Prefs.Apps[0].Path)) Check(nativeMenu.Commands().Any(x=>x.EndsWith("|rename")),"system folder menu supplies rename");
  var filePrefs=new Preferences(); filePrefs.Apps.Add(new Entry{Name="系统菜单测试",Path=nativeFile}); filePrefs.DockOrder.Add(nativeFile);
  var fileRenamed=await FolderRename.RenameAsync(filePrefs,nativeFile,"系统菜单改名.txt",persist);
  Check(!File.Exists(nativeFile) && File.ReadAllText(fileRenamed.Apps[0].Path)=="unchanged" && fileRenamed.DockOrder[0]==fileRenamed.Apps[0].Path,"file rename syncs disk and dock reference");
  var originalGroups=c.Prefs.BoardGroups().ToList();
  int entriesBeforeDelete=c.Prefs.Apps.Count;
  string folderBeforeDelete=c.Prefs.Apps[0].Path;
  bar.Children.OfType<Border>().First(b=>(string)b.Tag=="项目资料").ContextMenu.Items.OfType<MenuItem>().First(m=>(string)m.Header=="删除分组").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
  Check(!c.Prefs.Groups.Contains("项目资料") && c.Board.CurrentGroup=="全部","delete group menu removes tab and selects all");
  Check(c.Prefs.Apps.Count==entriesBeforeDelete && Directory.Exists(folderBeforeDelete) && !FeatureRules.InGroup(c.Prefs.Apps[0],"项目资料"),"delete group retains files and entries, removes membership");
  c.UndoBoard(); Check(c.Prefs.BoardGroups().SequenceEqual(originalGroups) && FeatureRules.InGroup(c.Prefs.Apps[0],"项目资料"),"delete group undo restores membership and order");
  c.DeleteGroupFromMenu("未分组");
  Check(c.Prefs.UnassignedHidden && !c.Prefs.BoardGroups().Contains("未分组") && c.Prefs.Apps.Count==entriesBeforeDelete,"default group delete hides tab and preserves entries");
  persist(c.Prefs);
  using(var f=File.OpenRead(saved)) {
   var loaded=(Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(f); loaded.Normalize();
   Check(loaded.UnassignedHidden && !loaded.BoardGroups().Contains("未分组"),"default deletion survives reload");
  }
  typeof(BoardWindow).GetMethod("NewFileMenu",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c.Board,null);
  newMenu=(ContextMenu)typeof(BoardWindow).GetField("iconMenu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.Board);
  newMenu.Items.OfType<MenuItem>().First(m=>((string)m.Header).StartsWith("恢复分组")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
  Check(!c.Prefs.UnassignedHidden && c.Board.CurrentGroup=="未分组","restore default group through blank menu");
  c.UndoBoard(); Check(c.Prefs.UnassignedHidden,"restore default undo");
  c.UndoBoard(); Check(!c.Prefs.UnassignedHidden && c.Prefs.BoardGroups().SequenceEqual(originalGroups),"delete default undo restores order");
  int positionMessages=0;
  var boardSource=System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(c.Board).Handle);
  System.Windows.Interop.HwndSourceHook positionHook=(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled)=>{if(msg==0x46)positionMessages++;return IntPtr.Zero;};
  boardSource.AddHook(positionHook);
  Exception modalError=null;
  var closeBatch=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(300)};
  closeBatch.Tick+=(sender,args)=>{
   var dialog=Application.Current.Windows.OfType<BatchAddWindow>().FirstOrDefault();
   if(dialog==null)return;
   closeBatch.Stop();
   try {
    Check(c.Board.ModalBusy && !IsWindowEnabled(new System.Windows.Interop.WindowInteropHelper(c.Board).Handle),"batch operation holds modal guard");
    positionMessages=0;
    var refreshOrder=typeof(DesktopPersistence).GetMethod("RefreshOrder",BindingFlags.Static|BindingFlags.NonPublic);
    for(int i=0;i<12;i++)refreshOrder.Invoke(null,null);
    Check(positionMessages==0 && dialog.IsVisible,"desktop maintenance does not reposition modal owner");
   }catch(Exception ex){modalError=ex;}
   finally{dialog.Close();}
  };
  closeBatch.Start(); c.Choose();
  boardSource.RemoveHook(positionHook);
  if(modalError!=null)throw modalError;
  Check(!c.Board.ModalBusy && c.Board.IsEnabled,"batch operation restores enabled state");
  c.Board.SelectRenamedGroup("全部");
  var initialEntries=c.Prefs.Apps.ToList();
  c.ReorderEntries(new[]{initialEntries[2].Path},initialEntries[0],true);
  Check(c.Prefs.Apps[0].Path==initialEntries[2].Path,"single icon moves before target");
  c.UndoBoard(); Check(c.Prefs.Apps.Select(a=>a.Path).SequenceEqual(initialEntries.Select(a=>a.Path)),"icon reorder undo");
  c.ReorderEntries(new[]{initialEntries[0].Path,initialEntries[1].Path},initialEntries[2],false);
  Check(c.Prefs.Apps.Select(a=>a.Path).SequenceEqual(new[]{initialEntries[2].Path,initialEntries[0].Path,initialEntries[1].Path}),"multi icon reorder preserves relative order");
  persist(c.Prefs);
  using(var f=File.OpenRead(saved)) Check(((Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(f)).Apps.Select(a=>a.Path).SequenceEqual(c.Prefs.Apps.Select(a=>a.Path)),"manual icon order survives save");
  c.UndoBoard();
  c.Board.UpdateLayout();
  var tiles=(System.Windows.Controls.Primitives.UniformGrid)typeof(BoardWindow).GetField("Tiles",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.Board);
  var iconTiles=tiles.Children.OfType<Border>().ToList();
  var dragIcon=new DataObject(); dragIcon.SetData("GlassBoard.Entries",new[]{initialEntries[2].Path});
  var targetIcon=iconTiles[0];
  var dropIcon=(DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{dragIcon,DragDropKeyStates.LeftMouseButton,DragDropEffects.Move,targetIcon,new Point(1,1)},null);
  dropIcon.RoutedEvent=DragDrop.DragOverEvent; targetIcon.RaiseEvent(dropIcon);
  Check(dropIcon.Handled && dropIcon.Effects==DragDropEffects.Move && targetIcon.BorderThickness.Left==3,"icon drop insertion feedback");
  dropIcon.Handled=false; dropIcon.RoutedEvent=DragDrop.DropEvent;targetIcon.RaiseEvent(dropIcon);
  Check(c.Prefs.Apps[0].Path==initialEntries[2].Path,"icon drop event reorders entries");
  c.UndoBoard();
  typeof(BoardWindow).GetMethod("SetSelectionMode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c.Board,new object[]{true});
  c.Board.Render(); c.Board.UpdateLayout();
  iconTiles=tiles.Children.OfType<Border>().ToList();
  var firstTile=iconTiles[0];
  var rectangle=new Rect(firstTile.TranslatePoint(new Point(),tiles),firstTile.RenderSize); rectangle.Inflate(-2,-2);
  var applyMarquee=typeof(BoardWindow).GetMethod("ApplyMarquee",BindingFlags.Instance|BindingFlags.NonPublic);
  applyMarquee.Invoke(c.Board,new object[]{tiles,rectangle,new HashSet<string>(StringComparer.OrdinalIgnoreCase)});
  var selectedSet=(HashSet<string>)typeof(BoardWindow).GetField("selected",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.Board);
  Check(selectedSet.SetEquals(new[]{initialEntries[0].Path}),"marquee selects intersecting icon only");
  applyMarquee.Invoke(c.Board,new object[]{tiles,rectangle,new HashSet<string>(new[]{initialEntries[2].Path},StringComparer.OrdinalIgnoreCase)});
  Check(selectedSet.SetEquals(new[]{initialEntries[0].Path,initialEntries[2].Path}),"Ctrl marquee preserves baseline selection");
  Check(iconTiles.All(t=>t.AllowDrop),"selected icons remain drop targets");
  var diskContents=File.ReadAllText(Path.Combine(folderBeforeDelete,"子目录","内容.txt"));
  Check(diskContents=="preserve me","reordering and selection preserve disk contents");
  c.Board.FinishSelection();
  string desktopFixture=Path.Combine(root,"desktop");Directory.CreateDirectory(desktopFixture);
  string desktopFolder=Path.Combine(desktopFixture,"目录");Directory.CreateDirectory(desktopFolder);File.WriteAllText(Path.Combine(desktopFolder,"子文件.txt"),"nested");
  string desktopBat=Path.Combine(desktopFixture,"启动.bat");File.WriteAllText(desktopBat,"@echo off");
  File.WriteAllText(Path.Combine(desktopFixture,"文档.txt"),"desktop");
  File.WriteAllText(Path.Combine(desktopFixture,"desktop.ini"),"skip");
  string hiddenFile=Path.Combine(desktopFixture,"隐藏.txt");File.WriteAllText(hiddenFile,"hidden");File.SetAttributes(hiddenFile,FileAttributes.Hidden);
  var scanned=DesktopEntries.Scan(new[]{desktopFixture,desktopFixture});
  Check(scanned.Count==5 && scanned.Any(a=>a.Path==desktopBat) && scanned.Any(a=>a.Path==desktopFolder && a.IsFolder),"desktop scan includes batch files/folders and system entries without duplicate roots");
  Check(!scanned.Any(a=>a.Path.EndsWith("子文件.txt") || a.Path.EndsWith("desktop.ini") || a.Path==hiddenFile),"desktop scan excludes nested and hidden system files");
  Check(Native.Icon(DesktopEntries.Computer)!=null && Native.Icon(DesktopEntries.RecycleBin)!=null,"computer and recycle bin resolve real shell icons");
  var desktopPrefs=new Preferences();var retained=new Entry{Name="自定义名称",Path=desktopBat,Group="工作"};desktopPrefs.Apps.Add(retained);desktopPrefs.Groups.Add("工作");
  Check(DesktopEntries.Merge(desktopPrefs,scanned,true)==4 && desktopPrefs.Apps[0]==retained && retained.Group=="工作" && retained.Name=="自定义名称","desktop merge preserves existing order and metadata");
  Check(DesktopEntries.Merge(desktopPrefs,DesktopEntries.Scan(new[]{desktopFixture}),false)==0,"desktop rescan is idempotent");
  EntryRules.Remove(desktopPrefs,new[]{desktopBat});
  Check(DesktopEntries.Merge(desktopPrefs,DesktopEntries.Scan(new[]{desktopFixture}),false)==0 && !desktopPrefs.Apps.Any(a=>a.Path==desktopBat),"desktop sync respects later manual removal");
  Check(DesktopEntries.Merge(desktopPrefs,DesktopEntries.Scan(new[]{desktopFixture}),true)==1,"explicit full desktop import can restore excluded desktop file");
  File.WriteAllText(Path.Combine(desktopFixture,"新增.txt"),"new");
  Check(DesktopEntries.Merge(desktopPrefs,DesktopEntries.Scan(new[]{desktopFixture}),false)==1 && desktopPrefs.Apps.Last().Name=="新增.txt","new desktop file appended on next scan");
  desktopPrefs.DesktopSyncInitialized=true;persist(desktopPrefs);
  using(var f=File.OpenRead(saved)){var loaded=(Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(f);loaded.Normalize();Check(loaded.DesktopSyncInitialized && loaded.Apps.Count==desktopPrefs.Apps.Count && loaded.Apps.Any(a=>a.Path==DesktopEntries.Computer),"desktop sync settings and shell entries survive reload");}
  var mixed=new List<Entry> {
   new Entry{Name="Z文档",Path="z.pdf",Group="分类"},new Entry{Name="程序",Path="app.EXE"},
   new Entry{Name="目录",Path="folder",IsFolder=true,Group="分类"},new Entry{Name="快捷方式",Path="shortcut.lnk"},
   new Entry{Name="脚本",Path="script.bat"},new Entry{Name="A文档",Path="a.docx",Group="分类"},
   new Entry{Name="图片",Path="image.png"},new Entry{Name="未知",Path="unknown.xyz"},
   new Entry{Name="此电脑",Path=DesktopEntries.Computer,IsFolder=true},new Entry{Name="音频",Path="song.mp3"},
   new Entry{Name="视频",Path="movie.mp4"},new Entry{Name="压缩包",Path="archive.zip"}
  };
  var sorted=EntryTypeOrder.Arrange(mixed);
  Check(sorted.Select(EntryTypeOrder.Category).SequenceEqual(new[]{0,1,2,3,4,5,5,6,7,8,9,10}),"automatic arrange groups all supported file categories");
  Check(sorted.Where(a=>EntryTypeOrder.Category(a)==5).Select(a=>a.Name).SequenceEqual(new[]{"A文档","Z文档"}),"automatic arrange names within each category");
  c.Prefs.Apps=mixed.ToList();c.Prefs.Groups.Add("分类");c.Board.SelectRenamedGroup("全部");
  c.AutoArrangeEntries("全部");
  Check(c.Prefs.Apps.Select(a=>a.Path).SequenceEqual(sorted.Select(a=>a.Path)),"controller sorts all entries by type");
  c.UndoBoard();Check(c.Prefs.Apps.Select(a=>a.Path).SequenceEqual(mixed.Select(a=>a.Path)),"automatic arrange undo restores exact prior order");
  c.AutoArrangeEntries("分类");
  Check(c.Prefs.Apps[0].Path=="folder" && c.Prefs.Apps[2].Path=="a.docx" && c.Prefs.Apps[5].Path=="z.pdf" && c.Prefs.Apps[1].Path=="app.EXE","group arrange leaves other entries at original positions");
  Check(c.Prefs.Apps.Count==mixed.Count && c.Prefs.Apps.Count(a=>a.Group=="分类")==3,"arrange preserves entries and group memberships");
  persist(c.Prefs);
  using(var f=File.OpenRead(saved))Check(((Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(f)).Apps.Select(a=>a.Path).SequenceEqual(c.Prefs.Apps.Select(a=>a.Path)),"automatic arrange order survives save");
  c.Board.SelectRenamedGroup("全部");
  typeof(BoardWindow).GetMethod("NewFileMenu",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c.Board,null);
  newMenu=(ContextMenu)typeof(BoardWindow).GetField("iconMenu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.Board);
  newMenu.Items.OfType<MenuItem>().First(m=>(string)m.Header=="自动整理（按类型）").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
  Check(c.Prefs.Apps.Select(EntryTypeOrder.Category).SequenceEqual(new[]{0,1,2,3,4,5,5,6,7,8,9,10}),"blank menu automatic arrange performs sorting");
  c.UndoBoard();
  c.Board.Render(); c.Board.UpdateLayout();
  var homeSort=Descendants(c.Board).OfType<Button>().FirstOrDefault(b=>b.Content as string=="自动整理");
  Check(homeSort!=null && homeSort.IsVisible,"automatic arrange button is on home toolbar");
  homeSort.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
  Check(c.Prefs.Apps.Select(EntryTypeOrder.Category).SequenceEqual(new[]{0,1,2,3,4,5,5,6,7,8,9,10}),"actual home button click executes sorting");
  c.UndoBoard(); Check(!c.Prefs.Apps.Select(EntryTypeOrder.Category).SequenceEqual(new[]{0,1,2,3,4,5,5,6,7,8,9,10}),"home button sorting can be undone");
  typeof(BoardWindow).GetMethod("MoreMenu",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c.Board,null);
  newMenu=(ContextMenu)typeof(BoardWindow).GetField("iconMenu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.Board);
  Check(!newMenu.Items.OfType<MenuItem>().Any(m=>(string)m.Header=="自动整理（按类型）"),"sort is removed from modal more-menu wrapper");
  newMenu.IsOpen=false;
  typeof(BoardWindow).GetField("capacity",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c.Board,3);
  var pageField=typeof(BoardWindow).GetField("page",BindingFlags.Instance|BindingFlags.NonPublic);
  pageField.SetValue(c.Board,0); c.Board.Render();
  Action<int,object> wheel=(delta,source)=>{var evt=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta){RoutedEvent=UIElement.PreviewMouseWheelEvent,Source=source};c.Board.RaiseEvent(evt);};
  wheel(-120,tiles);Check((int)pageField.GetValue(c.Board)==1,"wheel down advances page");
  wheel(120,tiles);Check((int)pageField.GetValue(c.Board)==0,"wheel up returns page");
  wheel(120,tiles);Check((int)pageField.GetValue(c.Board)==0,"wheel clamps at first page");
  wheel(-60,tiles);Check((int)pageField.GetValue(c.Board)==0,"high resolution wheel accumulates partial step");
  wheel(-60,tiles);Check((int)pageField.GetValue(c.Board)==1,"two partial wheel steps advance one page");
  var iconSlider=(Slider)typeof(BoardWindow).GetField("boardIconSlider",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.Board);
  wheel(-120,iconSlider);Check((int)pageField.GetValue(c.Board)==1,"wheel over slider does not turn page");
  c.Board.ModalBusy=true;wheel(-120,tiles);c.Board.ModalBusy=false;Check((int)pageField.GetValue(c.Board)==1,"modal operation blocks wheel paging");
  wheel(-12000,tiles);Check((int)pageField.GetValue(c.Board)==3,"wheel clamps at last page");
  var taskFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),@"Microsoft\Windows\Recent\CustomDestinations\44c129d33e00762c.customDestinations-ms");
  if(File.Exists(taskFile)) {
   var appTasks=ApplicationTasks.ReadFile(taskFile);
   Check(new[]{"应用更新","应用卸载","更换壁纸","玩小游戏"}.All(title=>appTasks.Any(t=>t.Title==title)),"live Lenovo tasks decoded with original labels");
   Check(appTasks.All(t=>!string.IsNullOrWhiteSpace(t.Path) && !string.IsNullOrWhiteSpace(t.Arguments)),"live tasks preserve executable and arguments");
   var readTasks=await ApplicationTasks.ReadAsync(appTasks[0].Path);
   Check(readTasks.Any(t=>t.Title=="应用卸载"),"background application-matched task scan");
   Check(!(await ApplicationTasks.ReadAsync(Path.Combine(root,"unrelated.exe"))).Any(),"unrelated application tasks excluded");
  }
  string invalidTask=Path.Combine(root,"invalid.customDestinations-ms");File.WriteAllText(invalidTask,"invalid");
  Check(ApplicationTasks.ReadFile(invalidTask).Count==0,"malformed task cache ignored");
  var closingWindow=new Window {Title="GlassBoard close test",Width=100,Height=100,ShowInTaskbar=false};
  bool requested=false;closingWindow.Closing+=(s,e)=>{requested=true;e.Cancel=true;};closingWindow.Show();
  var testHandle=new System.Windows.Interop.WindowInteropHelper(closingWindow).Handle;
  var closeTarget=new RunningWindow {Handle=testHandle,ProcessId=(uint)System.Diagnostics.Process.GetCurrentProcess().Id,Title="test"};
  Check(!ApplicationTasks.CloseWindow(new RunningWindow {Handle=testHandle,ProcessId=0}),"stale window process rejected");
  Check(ApplicationTasks.CloseWindow(closeTarget),"normal window close request sent");
  await Task.Delay(150);
  Check(requested && closingWindow.IsVisible,"normal close allows unsaved-content cancellation");
  closingWindow.Hide();
  if(File.Exists(taskFile)) closeTarget.Path=ApplicationTasks.ReadFile(taskFile)[0].Path;
  var groupType=typeof(DockWindow).GetNestedType("TaskGroup",BindingFlags.NonPublic);
  var testGroup=Activator.CreateInstance(groupType,true);
  groupType.GetField("Windows").SetValue(testGroup,new List<RunningWindow>{closeTarget});
  var taskMenu=(ContextMenu)typeof(DockWindow).GetMethod("TaskWindowMenu",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c.Dock,new[]{testGroup});
  Check(taskMenu.Items.OfType<MenuItem>().Any(i=>(string)i.Header=="关闭窗口"),"running icon menu offers close");
  Check(taskMenu.Items.OfType<MenuItem>().Any(i=>(string)i.Header=="任务管理器"),"running icon menu offers task manager");
  Check(c.Dock.ContextMenu.Items.OfType<MenuItem>().Any(i=>(string)i.Header=="任务管理器"),"Dock background menu offers task manager");
  c.Dock.Show(); taskMenu.PlacementTarget=c.Dock;taskMenu.Opened+=(s,e)=>Console.WriteLine("Task menu opened"); taskMenu.IsOpen=true;
  for(int i=0;i<60 && taskMenu.Items.OfType<MenuItem>().Any(t=>(string)t.Header=="正在读取应用任务…");i++) await Task.Delay(100);
  Check(!taskMenu.Items.OfType<MenuItem>().Any(t=>(string)t.Header=="正在读取应用任务…"),"real menu asynchronously loads without blocking UI");
  if(File.Exists(taskFile)) Check(taskMenu.Items.OfType<MenuItem>().Any(t=>(string)t.Header=="更换壁纸"),"actual menu contains original Lenovo task");
  taskMenu.IsOpen=false;
  Check(File.Exists(Path.Combine(Environment.SystemDirectory,"Taskmgr.exe")),"task manager shortcut resolves Windows executable");
  c.Dock.Refresh(false);
  var dockItems=(StackPanel)typeof(DockWindow).GetField("Items",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c.Dock);
  var keyProperty=(DependencyProperty)typeof(DockWindow).GetField("LayoutKey",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
  Check((string)dockItems.Children.OfType<Button>().Last().GetValue(keyProperty)==DockLayoutRules.TaskManager,"task manager icon at far right of common Dock");
  typeof(DockWindow).GetMethod("SetTaskMode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c.Dock,new object[]{true});
  Check((string)dockItems.Children.OfType<Button>().Last().GetValue(keyProperty)==DockLayoutRules.TaskManager,"task manager icon at far right of running Dock");
  var icon=TaskManagerIcon.Create();Check(icon.IsFrozen,"custom vector icon is frozen");
  var visual=new System.Windows.Media.DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(icon,new Rect(0,0,192,192));
  var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(192,192,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(visual);
  var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
  using(var output=File.Create(Path.Combine(root,"task-manager-icon.png")))encoder.Save(output);
  c.ShuttingDown=true; c.Board.Close(); c.Dock.Close();
 }
 static void SubmitNextDialog(string value)
 {
  var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
  timer.Tick+=(s,e)=>{
   var dialog=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.Title.Contains("重命名") || w.Title.Contains("新建分组"));
   if(dialog==null)return;
   timer.Stop();
   var panel=(StackPanel)dialog.Content;
   panel.Children.OfType<TextBox>().First().Text=value;
   panel.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
  };
  timer.Start();
 }
 static IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
  for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++){
   var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i); yield return child;
   foreach(var node in Descendants(child))yield return node;
  }
 }
}
