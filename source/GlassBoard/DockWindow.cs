using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GlassBoard;

public class DockWindow : GlassWindow
{
	private sealed class TaskGroup
	{
		public string Key;

		public string Name;

		public List<RunningWindow> Windows;
	}

	private static readonly DependencyProperty DragSuppressed = DependencyProperty.RegisterAttached("DockDragSuppressed", typeof(bool), typeof(DockWindow), new PropertyMetadata(false));

	private Action dockPointerClick;

	private bool dockIconDragging;

	private bool dockPointerPreviousBusy;

	private DockPointerGesture dockPointer;

	private FrameworkElement dockPointerOwner;

	private bool dockPointerFromMenu;

	private readonly DispatcherTimer dockPointerWatch = new DispatcherTimer(DispatcherPriority.Input)
	{
		Interval = TimeSpan.FromMilliseconds(33.0)
	};

	private Border dockPointerPreview;

	private TextBlock dockPointerLabel;

	private static readonly DependencyProperty LayoutKey = DependencyProperty.RegisterAttached("DockLayoutKey", typeof(string), typeof(DockWindow), new PropertyMetadata(null));

	private int revealSerial;

	private int layoutSerial;

	private readonly DockWheelState taskWheel = new DockWheelState();

	private readonly Stopwatch taskClock = Stopwatch.StartNew();

	private readonly DispatcherTimer taskHintTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(1800.0)
	};

	private List<TaskGroup> taskGroups = new List<TaskGroup>();

	private string taskSignature = "";

	private bool externalDockDrag;

	private bool boardPinDragging;

	private TextBlock taskHint;

	private System.Windows.Controls.ContextMenu taskMenu;

	private Controller C;

	private StackPanel Items = new StackPanel
	{
		Orientation = System.Windows.Controls.Orientation.Horizontal,
		VerticalAlignment = VerticalAlignment.Center,
		HorizontalAlignment = System.Windows.HorizontalAlignment.Center
	};

	private HwndSource source;

	private HotkeyManager hotkeys;

	private DispatcherTimer autoTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(50.0)
	};

	private DispatcherTimer runningTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(3.0)
	};

	private DateTime lastRunningScan = DateTime.MinValue;

	private AutoHidePolicy hidePolicy = new AutoHidePolicy();

	private bool renderedHidden;

	private bool pointerAtRevealEdge;

	private double shownTop;

	private Rect dockWorkingArea;

	private Rect dockScreenArea;

	private Thumb moveGrip;

	private bool dragging;

	private bool DockInputBusy
	{
		get
		{
			if (dockPointer == null)
			{
				return dockIconDragging;
			}
			return true;
		}
	}

	private IEnumerable<string> TaskKeys => taskGroups.Select((TaskGroup g) => "running:" + g.Key);

	private List<string> LayoutKeys
	{
		get
		{
			if (!taskWheel.Running)
			{
				return DockLayoutRules.Common(C.Prefs);
			}
			return DockLayoutRules.Running(C.Prefs, TaskKeys);
		}
	}

	public double RestingTop => shownTop;

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int key);

	private bool DockDragSuppresses(FrameworkElement element)
	{
		if (element != null)
		{
			return (bool)element.GetValue(DragSuppressed);
		}
		return false;
	}

	private void BindDockSource(FrameworkElement element, Entry entry, Action beforeDrag = null)
	{
		BindPointerItem(element, entry.Path, entry.Name, delegate
		{
			C.Launch(entry);
		}, beforeDrag);
	}

	private void BindPointerItem(FrameworkElement element, string key, string name, Action click, Action beforeDrag = null)
	{
		element.ToolTip = name + " · 点击打开；按住拖动调整 Dock 位置";
		element.AddHandler(Mouse.PreviewMouseDownEvent, (MouseButtonEventHandler)delegate(object s, MouseButtonEventArgs e)
		{
			if (e.ChangedButton == MouseButton.Left && !DockInputBusy)
			{
				System.Windows.Point position = e.GetPosition(this);
				if (StartPointerItem(element, key, name, click, position, beforeDrag))
				{
					e.Handled = true;
				}
			}
		}, handledEventsToo: true);
	}

	private bool StartDockPointer(FrameworkElement owner, Entry entry, System.Windows.Point point, Action beforeDrag)
	{
		return StartPointerItem(owner, entry.Path, entry.Name, delegate
		{
			C.Launch(entry);
		}, point, beforeDrag);
	}

	private bool StartPointerItem(FrameworkElement owner, string key, string name, Action click, System.Windows.Point point, Action beforeDrag)
	{
		if (!base.IsVisible || !LayoutKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
		{
			return false;
		}
		beforeDrag?.Invoke();
		if (!Mouse.Capture(this, CaptureMode.Element))
		{
			Store.Trace("DOCK pointer capture failed");
			return false;
		}
		StopDockLayoutMotion();
		dockPointer = new DockPointerGesture(key, point.X, point.Y, SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance);
		dockPointerClick = click;
		dockPointerOwner = owner;
		dockPointerFromMenu = owner is System.Windows.Controls.MenuItem;
		owner.SetValue(DragSuppressed, true);
		dockPointerPreviousBusy = C.Board.ModalBusy;
		C.Board.ModalBusy = true;
		dockPointerLabel.Text = name;
		dockPointerWatch.Start();
		Store.Trace("DOCK pointer press");
		return true;
	}

	private void MoveDockPointer(System.Windows.Point point)
	{
		if (dockPointer == null)
		{
			return;
		}
		bool flag = dockPointer.Dragging;
		dockPointer.Move(point.X, point.Y);
		if (!dockPointer.Dragging)
		{
			return;
		}
		if (!flag)
		{
			dockIconDragging = true;
			ResetDockHover();
			if (dockPointerOwner != null)
			{
				dockPointerOwner.BeginAnimation(UIElement.OpacityProperty, null);
				dockPointerOwner.Opacity = 0.45;
			}
			Store.Trace("DOCK pointer dragging");
		}
		System.Windows.Point point2 = TranslatePoint(point, Root);
		dockPointerPreview.RenderTransform = new TranslateTransform(point2.X + 12.0, point2.Y - 36.0);
		dockPointerPreview.Visibility = Visibility.Visible;
		ShowDockDrop(InsideDock(point) ? ResolveDockDrop(dockPointer.Source, TranslatePoint(point, Items).X) : null);
	}

	private bool InsideDock(System.Windows.Point point)
	{
		if (point.X >= 0.0 && point.Y >= 0.0 && point.X <= base.ActualWidth)
		{
			return point.Y <= base.ActualHeight;
		}
		return false;
	}

	private void FinishDockPointer(System.Windows.Point point)
	{
		if (dockPointer != null)
		{
			MoveDockPointer(point);
			DockPointerGesture dockPointerGesture = dockPointer;
			bool flag = InsideDock(point);
			DockPointerResult dockPointerResult = dockPointerGesture.Release(point.X, point.Y, flag, flag ? ResolveDockDrop(dockPointerGesture.Source, TranslatePoint(point, Items).X) : null, dockPointerFromMenu || flag);
			Action action = dockPointerClick;
			ClearDockPointer();
			bool flag2 = false;
			if (dockPointerResult.Target != null)
			{
				flag2 = C.ReorderDock(dockPointerGesture.Source, dockPointerResult.Target.Path, dockPointerResult.Target.Before);
			}
			if (!flag2 && !dockPointerResult.Click)
			{
				Refresh(capture: false);
			}
			Store.Trace("DOCK pointer release dragged=" + dockPointerResult.Dragged + " inside=" + flag + " candidate=" + (dockPointerResult.Target != null) + " changed=" + flag2);
			if (dockPointerResult.Click)
			{
				action?.Invoke();
			}
		}
	}

	private void ClearDockPointer()
	{
		FrameworkElement owner = dockPointerOwner;
		dockPointer = null;
		dockPointerClick = null;
		dockPointerOwner = null;
		dockIconDragging = false;
		dockPointerWatch.Stop();
		C.Board.ModalBusy = dockPointerPreviousBusy;
		try
		{
			ClearDockMarkers();
			dockPointerPreview.Visibility = Visibility.Collapsed;
			dockPointerLabel.Text = "";
			if (owner == null)
			{
				return;
			}
			owner.BeginAnimation(UIElement.OpacityProperty, null);
			owner.Opacity = 1.0;
			if (!base.Dispatcher.HasShutdownStarted)
			{
				base.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, (Action)delegate
				{
					owner.SetValue(DragSuppressed, false);
				});
			}
		}
		finally
		{
			if (Mouse.Captured == this)
			{
				Mouse.Capture(null);
			}
		}
	}

	private void RouteDockPointer(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			Store.Log(ex);
			CancelDockPointer(refresh: false);
			if (base.IsVisible && !C.ShuttingDown)
			{
				Refresh(capture: false);
			}
		}
	}

	private void CancelDockPointer(bool refresh = true)
	{
		if (dockPointer != null)
		{
			dockPointer.Cancel();
			ClearDockPointer();
			Store.Trace("DOCK pointer cancelled");
			if (refresh && base.IsVisible && !C.ShuttingDown)
			{
				Refresh(capture: false);
			}
		}
	}

	private void ClearDockMarkers()
	{
		foreach (System.Windows.Controls.Button item in Items.Children.OfType<System.Windows.Controls.Button>())
		{
			if (!(item.Content is Grid grid))
			{
				continue;
			}
			foreach (Border item2 in from b in grid.Children.OfType<Border>()
				where b.Tag as string == "dockInsert"
				select b)
			{
				item2.Visibility = Visibility.Collapsed;
			}
		}
	}

	private void ResetDockHover()
	{
		foreach (System.Windows.Controls.Button item in Items.Children.OfType<System.Windows.Controls.Button>())
		{
			if (!(item.RenderTransform is TransformGroup transformGroup))
			{
				continue;
			}
			foreach (ScaleTransform item2 in transformGroup.Children.OfType<ScaleTransform>())
			{
				item2.BeginAnimation(ScaleTransform.ScaleXProperty, null);
				item2.BeginAnimation(ScaleTransform.ScaleYProperty, null);
				double scaleX = (item2.ScaleY = 1.0);
				item2.ScaleX = scaleX;
			}
			foreach (TranslateTransform item3 in transformGroup.Children.OfType<TranslateTransform>())
			{
				item3.BeginAnimation(TranslateTransform.YProperty, null);
				item3.Y = 0.0;
			}
		}
	}

	private void BindDockIcon(System.Windows.Controls.Button button, Entry entry)
	{
		BindDockSource(button, entry);
		AddInsertMarker(button);
	}

	private DockDropTarget ResolveDockDrop(string source, double x)
	{
		List<System.Windows.Controls.Button> list = (from b in Items.Children.OfType<System.Windows.Controls.Button>()
			where ItemKey(b) != null
			select b).ToList();
		foreach (System.Windows.Controls.Button item in list)
		{
			double x2 = item.TranslatePoint(new System.Windows.Point(0.0, 0.0), Items).X;
			if (x >= x2 && x <= x2 + item.ActualWidth)
			{
				return DockLayoutRules.OnIcon(C.Prefs, source, ItemKey(item), taskWheel.Running ? TaskKeys : null);
			}
		}
		return DockDropRules.OnGap(source, list.Select(ItemKey).ToArray(), list.Select((System.Windows.Controls.Button b) => b.TranslatePoint(new System.Windows.Point(b.ActualWidth / 2.0, 0.0), Items).X).ToArray(), x);
	}

	private void ShowDockDrop(DockDropTarget drop)
	{
		ClearDockMarkers();
		if (drop == null)
		{
			return;
		}
		List<System.Windows.Controls.Button> list = (from b in Items.Children.OfType<System.Windows.Controls.Button>()
			where ItemKey(b) != null
			select b).ToList();
		System.Windows.Controls.Button button = ((drop.Path == null) ? list.LastOrDefault() : list.FirstOrDefault((System.Windows.Controls.Button b) => string.Equals(ItemKey(b), drop.Path, StringComparison.OrdinalIgnoreCase)));
		Grid grid = ((button == null) ? null : (button.Content as Grid));
		if (grid == null)
		{
			return;
		}
		foreach (Border item in from b in grid.Children.OfType<Border>()
			where b.Tag as string == "dockInsert"
			select b)
		{
			item.HorizontalAlignment = ((drop.Path == null || !drop.Before) ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left);
			item.Visibility = Visibility.Visible;
		}
	}

	private void InitDockPointer()
	{
		Items.Background = System.Windows.Media.Brushes.Transparent;
		dockPointerLabel = new TextBlock
		{
			Foreground = System.Windows.Media.Brushes.White,
			MaxWidth = 180.0,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		dockPointerPreview = new Border
		{
			Child = dockPointerLabel,
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			CornerRadius = new CornerRadius(12.0),
			Background = Polish.Color("#E52A3447"),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			IsHitTestVisible = false,
			Visibility = Visibility.Collapsed
		};
		Root.Children.Add(dockPointerPreview);
		AddHandler(Mouse.PreviewMouseMoveEvent, (System.Windows.Input.MouseEventHandler)delegate(object s, System.Windows.Input.MouseEventArgs e)
		{
			if (dockPointer != null)
			{
				e.Handled = true;
				RouteDockPointer(delegate
				{
					MoveDockPointer(e.GetPosition(this));
				});
			}
		}, handledEventsToo: true);
		AddHandler(Mouse.PreviewMouseUpEvent, (MouseButtonEventHandler)delegate(object s, MouseButtonEventArgs e)
		{
			if (dockPointer != null && e.ChangedButton == MouseButton.Left)
			{
				e.Handled = true;
				RouteDockPointer(delegate
				{
					FinishDockPointer(e.GetPosition(this));
				});
			}
		}, handledEventsToo: true);
		AddHandler(Mouse.LostMouseCaptureEvent, (System.Windows.Input.MouseEventHandler)delegate
		{
			if (dockPointer != null && Mouse.Captured != this)
			{
				CancelDockPointer();
			}
		}, handledEventsToo: true);
		base.PreviewKeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
		{
			if (dockPointer != null && e.Key == Key.Escape)
			{
				e.Handled = true;
				CancelDockPointer();
			}
		};
		dockPointerWatch.Tick += delegate
		{
			if (dockPointer == null)
			{
				dockPointerWatch.Stop();
			}
			else if (((uint)GetAsyncKeyState(27) & 0x8000u) != 0)
			{
				CancelDockPointer();
			}
			else if (Mouse.Captured != this)
			{
				CancelDockPointer();
			}
			else if ((GetAsyncKeyState((!SystemInformation.MouseButtonsSwapped) ? 1 : 2) & 0x8000) == 0)
			{
				RouteDockPointer(delegate
				{
					FinishDockPointer(Mouse.GetPosition(this));
				});
			}
		};
		base.IsVisibleChanged += delegate
		{
			if (!base.IsVisible)
			{
				CancelDockPointer(refresh: false);
			}
		};
		base.Closed += delegate
		{
			CancelDockPointer(refresh: false);
			dockPointerWatch.Stop();
		};
	}

	internal void CheckPointerReorder()
	{
		List<System.Windows.Controls.Button> list = (from b in Items.Children.OfType<System.Windows.Controls.Button>()
			where b.Tag is Entry
			select b).ToList();
		if (list.Count < 3)
		{
			throw new Exception("指针排序测试缺少可见图标");
		}
		Entry entry = (Entry)list.First().Tag;
		Entry entry2 = (Entry)list.Last().Tag;
		System.Windows.Point point = list.Last().TranslatePoint(new System.Windows.Point(list.Last().ActualWidth / 2.0, list.Last().ActualHeight / 2.0), this);
		System.Windows.Point point2 = list.First().TranslatePoint(new System.Windows.Point(list.First().ActualWidth / 2.0, list.First().ActualHeight / 2.0), this);
		if (!StartDockPointer(list.Last(), entry2, point, null))
		{
			throw new Exception("Dock 未捕获排序鼠标");
		}
		MoveDockPointer(point2);
		FinishDockPointer(point2);
		UpdateLayout();
		if (DockOrderRules.Ordered(C.Prefs)[0].Path != entry2.Path)
		{
			throw new Exception("后向前松手未提交");
		}
		list = (from b in Items.Children.OfType<System.Windows.Controls.Button>()
			where b.Tag is Entry
			select b).ToList();
		point = list.First().TranslatePoint(new System.Windows.Point(list.First().ActualWidth / 2.0, list.First().ActualHeight / 2.0), this);
		point2 = list.Last().TranslatePoint(new System.Windows.Point(list.Last().ActualWidth / 2.0, list.Last().ActualHeight / 2.0), this);
		if (!StartDockPointer(list.First(), entry2, point, null))
		{
			throw new Exception("第二次排序未捕获鼠标");
		}
		MoveDockPointer(point2);
		FinishDockPointer(point2);
		UpdateLayout();
		if (DockOrderRules.Ordered(C.Prefs).Last().Path != entry2.Path)
		{
			throw new Exception("前向后松手未提交");
		}
		list = (from b in Items.Children.OfType<System.Windows.Controls.Button>()
			where b.Tag is Entry
			select b).ToList();
		string text = string.Join("|", C.Prefs.DockOrder);
		point = list.First().TranslatePoint(new System.Windows.Point(20.0, 20.0), this);
		if (!StartDockPointer(list.First(), entry, point, null))
		{
			throw new Exception("取消测试未捕获鼠标");
		}
		MoveDockPointer(new System.Windows.Point(point.X + 30.0, point.Y));
		CancelDockPointer();
		if (string.Join("|", C.Prefs.DockOrder) != text || Mouse.Captured == this || dockPointer != null)
		{
			throw new Exception("取消后排序或捕获未恢复");
		}
	}

	private static string ItemKey(System.Windows.Controls.Button b)
	{
		object obj = b.GetValue(LayoutKey) as string;
		if (obj == null)
		{
			if (!(b.Tag is Entry))
			{
				return null;
			}
			obj = ((Entry)b.Tag).Path;
		}
		return (string)obj;
	}

	internal bool MoveLayout(string source, string target, bool before)
	{
		return DockLayoutRules.Move(C.Prefs, source, target, before, taskWheel.Running ? TaskKeys : null);
	}

	internal bool StepLayout(string source, int step)
	{
		return DockLayoutRules.Step(C.Prefs, source, step, taskWheel.Running ? TaskKeys : null);
	}

	private void AddSystemButton(string key, System.Windows.Controls.Button button, Action action)
	{
		button.SetValue(LayoutKey, key);
		BindLayoutButton(button, key, (button.ToolTip as string) ?? "系统入口", action);
		System.Windows.Controls.ContextMenu contextMenu = button.ContextMenu ?? MenuTheme.Create(C.Prefs.Light);
		System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
		{
			Header = "向前移动"
		};
		menuItem.Click += delegate
		{
			C.StepDock(key, -1);
		};
		System.Windows.Controls.MenuItem menuItem2 = new System.Windows.Controls.MenuItem
		{
			Header = "向后移动"
		};
		menuItem2.Click += delegate
		{
			C.StepDock(key, 1);
		};
		if (contextMenu.Items.Count > 0)
		{
			contextMenu.Items.Add(new Separator());
		}
		contextMenu.Items.Add(menuItem);
		contextMenu.Items.Add(menuItem2);
		if (button.ContextMenu == null)
		{
			AddDockControls(contextMenu);
		}
		button.ContextMenu = contextMenu;
		Items.Children.Add(button);
	}

	private void BindLayoutButton(System.Windows.Controls.Button button, string key, string name, Action action)
	{
		button.SetValue(LayoutKey, key);
		BindPointerItem(button, key, name, action);
		AddInsertMarker(button);
	}

	private void AddInsertMarker(System.Windows.Controls.Button button)
	{
		if (button.Content is Grid grid && !grid.Children.OfType<Border>().Any((Border b) => b.Tag as string == "dockInsert"))
		{
			grid.Children.Add(new Border
			{
				Tag = "dockInsert",
				Width = 3.0,
				Height = C.Prefs.Dock.IconSize + 8.0,
				CornerRadius = new CornerRadius(2.0),
				Background = Polish.Accent,
				VerticalAlignment = VerticalAlignment.Center,
				Visibility = Visibility.Collapsed,
				IsHitTestVisible = false
			});
		}
	}

	private void ArrangeDockItems()
	{
		List<System.Windows.Controls.Button> list = Items.Children.OfType<System.Windows.Controls.Button>().ToList();
		List<string> layoutKeys = LayoutKeys;
		List<System.Windows.Controls.Button> list2 = new List<System.Windows.Controls.Button>();
		foreach (string key in layoutKeys)
		{
			System.Windows.Controls.Button button = list.FirstOrDefault((System.Windows.Controls.Button x) => string.Equals(ItemKey(x), key, StringComparison.OrdinalIgnoreCase));
			if (button != null)
			{
				list2.Add(button);
			}
		}
		List<System.Windows.Controls.Button> collection = list.Where((System.Windows.Controls.Button b) => ItemKey(b) == null).ToList();
		int num = list2.FindLastIndex((System.Windows.Controls.Button b) => !DockLayoutRules.SystemItem(ItemKey(b)));
		list2.InsertRange((num < 0) ? Math.Min(2, list2.Count) : (num + 1), collection);
		Items.Children.Clear();
		foreach (System.Windows.Controls.Button item in list2)
		{
			Items.Children.Add(item);
			if (C.Prefs.Dock.ShowSeparator && ItemKey(item) == "glassboard://dock/apps")
			{
				Items.Children.Add(new Border
				{
					Width = 1.0,
					Height = C.Prefs.Dock.IconSize * 0.7,
					Margin = new Thickness(8.0, 0.0, 8.0, 0.0),
					Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(65, 220, 220, 230))
				});
			}
		}
	}

	internal void CheckMixedPointerReorder()
	{
		C.Prefs.Dock.MotionEnabled = false;
		C.Prefs.Dock.ShowPower = (C.Prefs.Dock.ShowNetwork = (C.Prefs.Dock.ShowComputer = (C.Prefs.Dock.ShowSettings = true)));
		taskWheel.Set(running: false);
		Refresh(capture: false);
		UpdateLayout();
		string[] systemKeys = DockLayoutRules.SystemKeys;
		foreach (string key in systemKeys)
		{
			for (int j = 0; j < 2; j++)
			{
				System.Windows.Controls.Button button = Items.Children.OfType<System.Windows.Controls.Button>().First((System.Windows.Controls.Button b) => ItemKey(b) == key);
				List<System.Windows.Controls.Button> list = (from b in Items.Children.OfType<System.Windows.Controls.Button>()
					where b.Tag is Entry
					select b).ToList();
				System.Windows.Controls.Button button2 = ((j == 0) ? list.First() : list.Last());
				string text = ItemKey(button2);
				DockDropTarget dockDropTarget = DockLayoutRules.OnIcon(C.Prefs, key, text);
				if (dockDropTarget == null)
				{
					throw new Exception("混排目标无效");
				}
				List<string> list2 = LayoutKeys.ToList();
				list2.Remove(key);
				list2.Insert(list2.IndexOf(text) + ((!dockDropTarget.Before) ? 1 : 0), key);
				bool clicked = false;
				System.Windows.Point point = button.TranslatePoint(new System.Windows.Point(button.ActualWidth / 2.0, button.ActualHeight / 2.0), this);
				System.Windows.Point point2 = button2.TranslatePoint(new System.Windows.Point(button2.ActualWidth / 2.0, button2.ActualHeight / 2.0), this);
				if (!StartPointerItem(button, key, key, delegate
				{
					clicked = true;
				}, point, null))
				{
					throw new Exception("系统入口无法捕获拖动");
				}
				MoveDockPointer(point2);
				FinishDockPointer(point2);
				UpdateLayout();
				if (clicked || !LayoutKeys.SequenceEqual(list2))
				{
					throw new Exception("系统入口拖放未保存，或拖动触发了点击");
				}
			}
		}
		string text2 = string.Join("|", LayoutKeys);
		C.Prefs.Dock.ShowSettings = false;
		Refresh(capture: false);
		if (Items.Children.OfType<System.Windows.Controls.Button>().Any((System.Windows.Controls.Button b) => ItemKey(b) == "glassboard://dock/settings") || base.ContextMenu == null)
		{
			throw new Exception("隐藏设置图标后仍显示，或缺少恢复菜单");
		}
		C.Prefs.Dock.ShowSettings = true;
		Refresh(capture: false);
		if (string.Join("|", LayoutKeys) != text2)
		{
			throw new Exception("重新显示设置图标后丢失位置");
		}
	}

	private void StopDockLayoutMotion()
	{
		double width = base.Width;
		double left = base.Left;
		layoutSerial++;
		BeginAnimation(FrameworkElement.WidthProperty, null);
		BeginAnimation(Window.LeftProperty, null);
		if (!double.IsNaN(width))
		{
			base.Width = width;
		}
		if (!double.IsNaN(left))
		{
			base.Left = left;
		}
	}

	private void AnimateDockLayout(double oldLeft, double oldWidth)
	{
		if (!MotionEnabled || !base.IsVisible || double.IsNaN(oldWidth) || double.IsNaN(oldLeft) || oldWidth <= 0.0 || Math.Abs(oldWidth - base.Width) < 0.5)
		{
			return;
		}
		int serial = ++layoutSerial;
		TimeSpan timeSpan = TimeSpan.FromMilliseconds(180.0);
		CubicEase easingFunction = new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		};
		DoubleAnimation doubleAnimation = new DoubleAnimation(oldWidth, base.Width, timeSpan)
		{
			EasingFunction = easingFunction,
			FillBehavior = FillBehavior.Stop
		};
		DoubleAnimation animation = new DoubleAnimation(oldLeft, base.Left, timeSpan)
		{
			EasingFunction = easingFunction,
			FillBehavior = FillBehavior.Stop
		};
		doubleAnimation.Completed += delegate
		{
			if (serial == layoutSerial)
			{
				BeginAnimation(FrameworkElement.WidthProperty, null);
				BeginAnimation(Window.LeftProperty, null);
			}
		};
		BeginAnimation(FrameworkElement.WidthProperty, doubleAnimation, HandoffBehavior.SnapshotAndReplace);
		BeginAnimation(Window.LeftProperty, animation, HandoffBehavior.SnapshotAndReplace);
	}

	public void AddDockControls(System.Windows.Controls.ContextMenu menu)
	{
		AddTaskDockMenu(menu);
		menu.Items.Add(new Separator());
		System.Windows.Controls.MenuItem movable = new System.Windows.Controls.MenuItem
		{
			Header = "允许自由拖动 Dock",
			IsCheckable = true
		};
		System.Windows.Controls.MenuItem spring = new System.Windows.Controls.MenuItem
		{
			Header = "弹簧弹出效果",
			IsCheckable = true,
			ToolTip = "自动隐藏后弹出时生效，遵循界面动效总开关"
		};
		System.Windows.Controls.MenuItem automatic = new System.Windows.Controls.MenuItem
		{
			Header = "Dock 自动隐藏",
			IsCheckable = true
		};
		menu.Opened += delegate
		{
			movable.IsChecked = !C.Prefs.Placement.DockLocked;
			spring.IsChecked = C.Prefs.Dock.SpringReveal;
			automatic.IsChecked = C.Prefs.Dock.AutoHide;
		};
		movable.Click += delegate
		{
			C.Prefs.Placement.DockLocked = !movable.IsChecked;
			C.Save();
			Refresh(capture: false);
		};
		spring.Click += delegate
		{
			C.Prefs.Dock.SpringReveal = spring.IsChecked;
			C.Save();
		};
		automatic.Click += delegate
		{
			C.Prefs.Dock.AutoHide = automatic.IsChecked;
			C.Save();
			Refresh(capture: false);
		};
		menu.Items.Add(movable);
		menu.Items.Add(spring);
		menu.Items.Add(automatic);
		System.Windows.Controls.MenuItem settingsIcon = new System.Windows.Controls.MenuItem
		{
			Header = "显示 Dock 设置图标",
			IsCheckable = true
		};
		menu.Opened += delegate
		{
			settingsIcon.IsChecked = C.Prefs.Dock.ShowSettings;
		};
		settingsIcon.Click += delegate
		{
			C.Prefs.Dock.ShowSettings = settingsIcon.IsChecked;
			C.Save();
			Refresh(capture: false);
		};
		menu.Items.Add(settingsIcon);
	}

	private void InitializeBackgroundMove()
	{
		Root.Background = System.Windows.Media.Brushes.Transparent;
		Root.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
		{
			if (!e.Handled && e.ButtonState == MouseButtonState.Pressed && !C.Prefs.Placement.DockLocked && !DockInputBusy)
			{
				DependencyObject dependencyObject = e.OriginalSource as DependencyObject;
				while (dependencyObject != null && dependencyObject != Root)
				{
					if (dependencyObject is System.Windows.Controls.Primitives.ButtonBase || dependencyObject is Thumb || dependencyObject is System.Windows.Controls.Primitives.TextBoxBase)
					{
						return;
					}
					dependencyObject = ((dependencyObject is Visual) ? VisualTreeHelper.GetParent(dependencyObject) : LogicalTreeHelper.GetParent(dependencyObject));
				}
				if (dependencyObject == Root)
				{
					e.Handled = true;
					BeginDockMove();
					try
					{
						DragMove();
					}
					catch (InvalidOperationException)
					{
					}
					finally
					{
						FinishDockMove();
					}
				}
			}
		};
	}

	private void BeginDockMove()
	{
		if (!C.Prefs.Placement.DockLocked)
		{
			StopDockLayoutMotion();
			dragging = true;
			double top = base.Top;
			revealSerial++;
			BeginAnimation(Window.TopProperty, null);
			base.Top = top;
			renderedHidden = false;
			hidePolicy = new AutoHidePolicy();
			ResetDockHover();
		}
	}

	private void FinishDockMove()
	{
		if (!dragging)
		{
			return;
		}
		try
		{
			Rect rect = (dockWorkingArea = DesktopWindowPolicy.WorkArea(this));
			dockScreenArea = DesktopWindowPolicy.WorkArea(this, pointer: false, fullScreen: true);
			base.Left = FeatureRules.Position(base.Left, rect.Left, rect.Width, base.Width);
			base.Top = FeatureRules.Position(base.Top, rect.Top, rect.Height, base.Height);
			shownTop = base.Top;
			C.Prefs.Placement.DockLeft = base.Left;
			C.Prefs.Placement.DockCenter = base.Left + base.Width / 2.0;
			C.Prefs.Placement.DockTop = base.Top;
			hidePolicy = new AutoHidePolicy();
			C.Save();
			Backdrop(C.Prefs.Light);
		}
		finally
		{
			dragging = false;
		}
	}

	private void AnimateDockVisibility(bool hide, double target)
	{
		double top = base.Top;
		int serial = ++revealSerial;
		BeginAnimation(Window.TopProperty, null);
		base.Top = target;
		if (!MotionEnabled)
		{
			return;
		}
		if (!hide && C.Prefs.Dock.SpringReveal)
		{
			double[] array = DockRevealRules.Stops(top, target, MotionLevel, dockWorkingArea.Top);
			DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
			{
				Duration = TimeSpan.FromMilliseconds(420.0),
				FillBehavior = FillBehavior.Stop
			};
			doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(array[0], KeyTime.FromTimeSpan(TimeSpan.Zero)));
			doubleAnimationUsingKeyFrames.KeyFrames.Add(new SplineDoubleKeyFrame(array[1], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(230.0)), new KeySpline(0.18, 0.72, 0.28, 1.0)));
			doubleAnimationUsingKeyFrames.KeyFrames.Add(new SplineDoubleKeyFrame(array[2], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330.0)), new KeySpline(0.35, 0.0, 0.65, 1.0)));
			doubleAnimationUsingKeyFrames.KeyFrames.Add(new SplineDoubleKeyFrame(array[3], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420.0)), new KeySpline(0.35, 0.0, 0.5, 1.0)));
			doubleAnimationUsingKeyFrames.Completed += delegate
			{
				if (serial == revealSerial)
				{
					BeginAnimation(Window.TopProperty, null);
				}
			};
			BeginAnimation(Window.TopProperty, doubleAnimationUsingKeyFrames, HandoffBehavior.SnapshotAndReplace);
			return;
		}
		DoubleAnimation doubleAnimation = new DoubleAnimation(top, target, TimeSpan.FromMilliseconds(hide ? 160 : 190))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			},
			FillBehavior = FillBehavior.Stop
		};
		doubleAnimation.Completed += delegate
		{
			if (serial == revealSerial)
			{
				BeginAnimation(Window.TopProperty, null);
			}
		};
		BeginAnimation(Window.TopProperty, doubleAnimation, HandoffBehavior.SnapshotAndReplace);
	}

	private void InitTaskDock()
	{
		base.AllowDrop = true;
		taskHint = new TextBlock
		{
			FontSize = 10.0,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(10.0, 2.0, 0.0, 0.0),
			IsHitTestVisible = false,
			Visibility = Visibility.Collapsed
		};
		Root.Children.Add(taskHint);
		taskHintTimer.Tick += delegate
		{
			taskHintTimer.Stop();
			taskHint.Visibility = Visibility.Collapsed;
		};
		AddHandler(Mouse.PreviewMouseWheelEvent, (MouseWheelEventHandler)delegate(object s, MouseWheelEventArgs e)
		{
			if (!DockInputBusy && !dragging && !externalDockDrag)
			{
				e.Handled = true;
				if (taskWheel.Scroll(e.Delta, taskClock.ElapsedMilliseconds))
				{
					ApplyTaskMode();
				}
			}
		}, handledEventsToo: true);
		base.PreviewDragEnter += DockExternalOver;
		base.PreviewDragOver += DockExternalOver;
		base.PreviewDragLeave += delegate(object s, System.Windows.DragEventArgs e)
		{
			System.Windows.Point position = e.GetPosition(this);
			if (!InsideDock(position))
			{
				ClearExternalDockDrag();
			}
		};
		base.PreviewDrop += delegate(object s, System.Windows.DragEventArgs e)
		{
			if (!DockInputBusy)
			{
				string[] array = DockPayload(e.Data);
				if (array.Length != 0)
				{
					e.Handled = true;
					e.Effects = System.Windows.DragDropEffects.None;
					try
					{
						DockDropTarget target = ExternalDockTarget(e.GetPosition(Items).X);
						ClearExternalDockDrag();
						DockPinResult dockPinResult = C.PinDockPaths(array, target);
						e.Effects = ((dockPinResult.Paths.Count > 0) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None);
						ShowTaskHint((dockPinResult.Paths.Count > 0) ? ("已放入 Dock" + ((dockPinResult.Skipped > 0) ? " · 已达固定上限" : "")) : "无法固定此入口");
						return;
					}
					catch (Exception ex)
					{
						ClearExternalDockDrag();
						Store.Log(ex);
						ShowTaskHint("添加失败，请重试");
						return;
					}
				}
				ClearExternalDockDrag();
			}
		};
		base.IsVisibleChanged += delegate
		{
			if (!base.IsVisible)
			{
				ClearExternalDockDrag();
			}
		};
		base.Closed += delegate
		{
			taskHintTimer.Stop();
			if (taskMenu != null)
			{
				taskMenu.IsOpen = false;
			}
		};
	}

	private void ShowTaskHint(string text)
	{
		taskHint.Text = text;
		taskHint.Foreground = base.MutedBrush;
		taskHint.Visibility = Visibility.Visible;
		taskHintTimer.Stop();
		taskHintTimer.Start();
	}

	private void SetTaskMode(bool running)
	{
		if (!DockInputBusy && !externalDockDrag)
		{
			taskWheel.Set(running);
			ApplyTaskMode();
		}
	}

	private void ApplyTaskMode()
	{
		if (taskMenu != null)
		{
			taskMenu.IsOpen = false;
		}
		if (taskWheel.Running)
		{
			RunningWindows.Scan();
			ReadTaskGroups();
		}
		ResetDockHover();
		Refresh(capture: false);
		ShowTaskHint(taskWheel.Running ? "正在运行 · 上滚回常用" : "常用软件 · 下滚看运行");
		if (MotionEnabled)
		{
			Items.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.35, 1.0, TimeSpan.FromMilliseconds(160.0))
			{
				FillBehavior = FillBehavior.Stop
			});
		}
	}

	private void AddTaskDockMenu(System.Windows.Controls.ContextMenu menu)
	{
		System.Windows.Controls.MenuItem common = new System.Windows.Controls.MenuItem
		{
			Header = "常用软件 · 滚轮向上",
			IsCheckable = true
		};
		System.Windows.Controls.MenuItem running = new System.Windows.Controls.MenuItem
		{
			Header = "正在运行 · 滚轮向下",
			IsCheckable = true
		};
		common.Click += delegate
		{
			SetTaskMode(running: false);
		};
		running.Click += delegate
		{
			SetTaskMode(running: true);
		};
		menu.Opened += delegate
		{
			common.IsChecked = !taskWheel.Running;
			running.IsChecked = taskWheel.Running;
		};
		menu.Items.Add(common);
		menu.Items.Add(running);
	}

	private static string TaskKey(RunningWindow window)
	{
		string fileName = Path.GetFileName(window.Path ?? "");
		if (!string.IsNullOrEmpty(window.Path) && !fileName.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
		{
			return window.Path;
		}
		return "window:" + window.Handle.ToInt64();
	}

	private void ReadTaskGroups()
	{
		taskGroups = RunningWindows.Snapshot().GroupBy(TaskKey, StringComparer.OrdinalIgnoreCase).OrderBy((IGrouping<string, RunningWindow> g) => g.Key, StringComparer.OrdinalIgnoreCase)
			.Select(delegate(IGrouping<string, RunningWindow> g)
			{
				List<RunningWindow> windows = g.OrderBy((RunningWindow w) => w.Handle.ToInt64()).ToList();
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(windows[0].Path ?? "");
				Entry entry = C.Prefs.Apps.FirstOrDefault((Entry a) => !a.IsFolder && WindowActivationRules.Executables(a).Contains(windows[0].Path, StringComparer.OrdinalIgnoreCase));
				return new TaskGroup
				{
					Key = g.Key,
					Name = ((entry != null) ? entry.Name : ((string.IsNullOrWhiteSpace(fileNameWithoutExtension) || g.Key.StartsWith("window:")) ? windows[0].Title : fileNameWithoutExtension)),
					Windows = windows
				};
			})
			.ToList();
		taskSignature = TaskSignature();
	}

	private string TaskSignature()
	{
		return string.Join("|", from w in RunningWindows.Snapshot()
			orderby w.Handle.ToInt64()
			select w.Handle.ToInt64() + ":" + w.Path + ":" + w.Title);
	}

	private void RefreshTaskSnapshot()
	{
		if (taskWheel.Running && !DockInputBusy && !externalDockDrag && !dragging && !C.Board.ModalBusy && (taskMenu == null || !taskMenu.IsOpen) && (base.ContextMenu == null || !base.ContextMenu.IsOpen) && !Items.Children.OfType<System.Windows.Controls.Button>().Any((System.Windows.Controls.Button b) => b.IsMouseOver || (b.ContextMenu != null && b.ContextMenu.IsOpen)) && !(taskSignature == TaskSignature()))
		{
			ReadTaskGroups();
			Refresh(capture: false);
		}
	}

	private void BuildTaskButtons(int shown, int slots)
	{
		List<string> taskOrder = DockLayoutRules.Running(C.Prefs, TaskKeys);
		List<TaskGroup> list = taskGroups.OrderBy((TaskGroup g) => taskOrder.IndexOf("running:" + g.Key)).ToList();
		foreach (TaskGroup item in list.Take(shown))
		{
			TaskGroup selected = item;
			System.Windows.Controls.Button button = DockButton(null, RunningWindows.Icon(item.Windows[0]), delegate
			{
				OpenTask(selected, null);
			}, item.Name + ((item.Windows.Count > 1) ? (" · " + item.Windows.Count + " 个窗口") : ""));
			button.Tag = item;
			BindLayoutButton(button, "running:" + item.Key, item.Name, delegate
			{
				OpenTask(selected, button);
			});
			if (button.Content is Grid grid)
			{
				grid.Children.Add(new Border
				{
					Width = ((item.Windows.Count > 1) ? 12 : 5),
					Height = 4.0,
					CornerRadius = new CornerRadius(2.0),
					Background = Polish.Accent,
					HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Bottom,
					IsHitTestVisible = false
				});
			}
			button.ContextMenu = TaskWindowMenu(item);
			Items.Children.Add(button);
		}
		if (taskGroups.Count > shown && slots > 0)
		{
			System.Windows.Controls.Button more = DockButton("···", null, delegate
			{
			}, "更多正在运行的软件");
			System.Windows.Controls.ContextMenu menu = MenuTheme.Create(C.Prefs.Light);
			foreach (TaskGroup item2 in list.Skip(shown))
			{
				System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
				{
					Header = item2.Name + ((item2.Windows.Count > 1) ? ("（" + item2.Windows.Count + "）") : "")
				};
				foreach (RunningWindow window in item2.Windows)
				{
					menuItem.Items.Add(TaskWindowItem(window));
				}
				menu.Items.Add(menuItem);
			}
			more.Click += delegate
			{
				OpenTaskMenu(menu, more);
			};
			more.ContextMenu = menu;
			Items.Children.Add(more);
		}
		if (taskGroups.Count == 0)
		{
			Items.Children.Add(DockButton("—", null, delegate
			{
				SetTaskMode(running: false);
			}, "暂无正在运行的软件 · 点击返回常用"));
		}
	}

	private System.Windows.Controls.MenuItem TaskWindowItem(RunningWindow window)
	{
		RunningWindow selected = window;
		System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem();
		menuItem.Header = (string.IsNullOrWhiteSpace(window.Title) ? "未命名窗口" : window.Title);
		menuItem.ToolTip = window.Title;
		menuItem.Click += delegate
		{
			ActivateTask(selected.Handle);
		};
		return menuItem;
	}

	private System.Windows.Controls.MenuItem TaskManagerItem()
    {
        var item = new System.Windows.Controls.MenuItem { Header = "任务管理器" };
        item.Click += delegate { try { ApplicationTasks.OpenTaskManager(); } catch (Exception ex) { ShowTaskHint(ex.Message); } };
        return item;
    }

    private System.Windows.Controls.ContextMenu TaskWindowMenu(TaskGroup group)
    {
        var menu = MenuTheme.Create(C.Prefs.Light);
        var tasks = new System.Windows.Controls.MenuItem { Header = "正在读取应用任务…", IsEnabled = false };
        menu.Items.Add(tasks);
        menu.Items.Add(new Separator());
        foreach (RunningWindow window in group.Windows) menu.Items.Add(TaskWindowItem(window));
        var close = new System.Windows.Controls.MenuItem { Header = group.Windows.Count > 1 ? "关闭所有窗口" : "关闭窗口" };
        close.Click += delegate {
            int sent = group.Windows.Count(ApplicationTasks.CloseWindow);
            ShowTaskHint(sent > 0 ? "已请求关闭，未保存内容由应用提示" : "窗口已关闭或无权关闭");
        };
        menu.Items.Add(close);
        menu.Items.Add(new Separator());
        menu.Items.Add(TaskManagerItem());
        AddDockControls(menu);
        bool loading = false;
        menu.Opened += async delegate {
            if (loading) return;
            loading = true;
            var entries = await ApplicationTasks.ReadAsync(group.Windows.FirstOrDefault()?.Path);
            menu.Items.Remove(tasks);
            int index = 0;
            foreach (var entry in entries) {
                var selected = entry;
                var item = new System.Windows.Controls.MenuItem { Header = entry.Title };
                if (!string.IsNullOrEmpty(entry.IconPath) && File.Exists(entry.IconPath)) {
                    var image = Native.Icon(entry.IconPath);
                    if (image != null) item.Icon = new System.Windows.Controls.Image { Source=image, Width=16, Height=16 };
                }
                item.Click += delegate { try { selected.Launch(); } catch (Exception ex) { ShowTaskHint(ex.Message); } };
                menu.Items.Insert(index++, item);
            }
            if (entries.Count == 0) menu.Items.Insert(0, new System.Windows.Controls.MenuItem { Header="应用未提供可读取的快捷任务", IsEnabled=false });
        };
        return menu;
    }

	private void OpenTask(TaskGroup group, FrameworkElement owner)
	{
		if (group.Windows.Count == 1)
		{
			ActivateTask(group.Windows[0].Handle);
			return;
		}
		if (owner == null)
		{
			owner = Items.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault((System.Windows.Controls.Button b) => b.Tag == group);
		}
		OpenTaskMenu(TaskWindowMenu(group), owner ?? this);
	}

	private void OpenTaskMenu(System.Windows.Controls.ContextMenu menu, UIElement owner)
	{
		taskMenu = menu;
		menu.Closed += delegate
		{
			if (taskMenu == menu)
			{
				taskMenu = null;
			}
		};
		menu.PlacementTarget = owner;
		menu.Placement = PlacementMode.Top;
		menu.IsOpen = true;
	}

	private void ActivateTask(IntPtr handle)
	{
		if (RunningWindows.ActivateHandle(handle))
		{
			C.Board.CollapseSmooth();
			return;
		}
		RunningWindows.Scan();
		ReadTaskGroups();
		Refresh(capture: false);
		ShowTaskHint("此窗口已关闭");
	}

	private static string[] DockPayload(System.Windows.IDataObject data)
	{
		if (data.GetDataPresent("GlassBoard.Entries"))
		{
			return (data.GetData("GlassBoard.Entries") as string[]) ?? new string[0];
		}
		if (data.GetDataPresent("GlassBoard.Entry"))
		{
			return new string[1] { data.GetData("GlassBoard.Entry") as string };
		}
		if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop))
		{
			return new string[0];
		}
		return (data.GetData(System.Windows.DataFormats.FileDrop) as string[]) ?? new string[0];
	}

	private DockDropTarget ExternalDockTarget(double x)
	{
		List<System.Windows.Controls.Button> list = (from b in Items.Children.OfType<System.Windows.Controls.Button>()
			where ItemKey(b) != null
			select b).ToList();
		return DockDropRules.OnGap(null, list.Select(ItemKey).ToArray(), list.Select((System.Windows.Controls.Button b) => b.TranslatePoint(new System.Windows.Point(b.ActualWidth / 2.0, 0.0), Items).X).ToArray(), x);
	}

	private void DockExternalOver(object sender, System.Windows.DragEventArgs e)
	{
		if (DockInputBusy || DockPayload(e.Data).Length == 0)
		{
			e.Effects = System.Windows.DragDropEffects.None;
			e.Handled = true;
			return;
		}
		e.Handled = true;
		e.Effects = System.Windows.DragDropEffects.Copy;
		if (!externalDockDrag)
		{
			externalDockDrag = true;
			if (taskWheel.Running)
			{
				taskWheel.Set(running: false);
				Refresh(capture: false);
				UpdateLayout();
			}
			ShowTaskHint("松手固定到 Dock");
		}
		ShowDockDrop(ExternalDockTarget(e.GetPosition(Items).X));
	}

	public void BeginBoardPinDrag()
	{
		boardPinDragging = true;
		if (base.IsVisible)
		{
			base.Topmost = true;
		}
	}

	public void EndBoardPinDrag()
	{
		boardPinDragging = false;
		ClearExternalDockDrag();
		if (base.IsVisible)
		{
			base.Topmost = C.Prefs.Dock.AlwaysOnTop || C.Prefs.Dock.AutoHide;
		}
	}

	internal void CheckTaskDock()
	{
		SetTaskMode(running: false);
		UpdateLayout();
		foreach (System.Windows.Controls.Button item in Items.Children.OfType<System.Windows.Controls.Button>())
		{
			item.ApplyTemplate();
			SolidColorBrush solidColorBrush = ((!(VisualTreeHelper.GetChild(item, 0) is Border border)) ? null : (border.Background as SolidColorBrush));
			if (solidColorBrush == null || solidColorBrush.Color.A == 0)
			{
				throw new Exception("Dock 整块点击区域缺少有效像素");
			}
			System.Windows.Point[] array = new System.Windows.Point[3]
			{
				new System.Windows.Point(item.ActualWidth / 2.0, item.ActualHeight / 2.0),
				new System.Windows.Point(4.0, 4.0),
				new System.Windows.Point(item.ActualWidth - 4.0, item.ActualHeight - 4.0)
			};
			foreach (System.Windows.Point point in array)
			{
				DependencyObject dependencyObject = item.InputHitTest(point) as DependencyObject;
				while (dependencyObject != null && dependencyObject != item)
				{
					dependencyObject = ((dependencyObject is Visual) ? VisualTreeHelper.GetParent(dependencyObject) : LogicalTreeHelper.GetParent(dependencyObject));
				}
				if (dependencyObject != item)
				{
					throw new Exception("Dock 图标空白区域无法命中按钮");
				}
			}
		}
		MouseWheelEventArgs mouseWheelEventArgs = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120)
		{
			RoutedEvent = Mouse.PreviewMouseWheelEvent
		};
		RaiseEvent(mouseWheelEventArgs);
		if (!mouseWheelEventArgs.Handled || !taskWheel.Running)
		{
			throw new Exception("Dock 滚轮未切换运行软件");
		}
		SetTaskMode(running: false);
	}

	private void ReleaseCancelledExternalDrag()
	{
		if (externalDockDrag && (GetAsyncKeyState((!SystemInformation.MouseButtonsSwapped) ? 1 : 2) & 0x8000) == 0)
		{
			ClearExternalDockDrag();
		}
	}

	private void ClearExternalDockDrag()
	{
		externalDockDrag = false;
		ClearDockMarkers();
	}

	public void GamePolling(bool enabled)
	{
		TimeSpan timeSpan = TimeSpan.FromMilliseconds(enabled ? 350 : 3000);
		if (runningTimer.Interval != timeSpan)
		{
			runningTimer.Interval = timeSpan;
		}
	}

	public DockWindow(Controller c)
		: base(29.0)
	{
		C = c;
		base.Title = "GlassBoard Dock";
		base.Height = 112.0;
		base.ShowActivated = false;
		base.Topmost = false;
		ContentGrid.Children.Add(Items);
		InitDockPointer();
		InitializeBackgroundMove();
		InitTaskDock();
		moveGrip = new Thumb
		{
			Width = 42.0,
			Height = 6.0,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 7.0, 0.0, 0.0),
			Cursor = System.Windows.Input.Cursors.SizeAll,
			ToolTip = "拖动 Dock；设置中可锁定位置"
		};
		Root.Children.Add(moveGrip);
		moveGrip.Template = GlassWindow.GripTemplate();
		base.Closing += delegate(object s, CancelEventArgs e)
		{
			if (!C.ShuttingDown)
			{
				e.Cancel = true;
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					C.Quit();
				});
			}
		};
		moveGrip.DragStarted += delegate
		{
			BeginDockMove();
		};
		moveGrip.DragDelta += delegate(object s, DragDeltaEventArgs e)
		{
			if (dragging)
			{
				Rect rect = DesktopWindowPolicy.WorkArea(this, pointer: true);
				base.Left = FeatureRules.Position(base.Left + e.HorizontalChange, rect.Left, rect.Width, base.Width);
				base.Top = FeatureRules.Position(base.Top + e.VerticalChange, rect.Top, rect.Height, base.Height);
			}
		};
		moveGrip.DragCompleted += delegate
		{
			FinishDockMove();
		};
		autoTimer.Tick += delegate
		{
			UpdateAutoHide();
		};
		runningTimer.Tick += delegate
		{
			ReleaseCancelledExternalDrag();
			C.CheckGameMode();
			if (!C.GlassSuppressed && (base.IsVisible || C.Board.IsVisible) && (C.Prefs.Dock.ShowRunning || taskWheel.Running) && !((DateTime.UtcNow - lastRunningScan).TotalSeconds < (double)(renderedHidden ? 10 : 3)))
			{
				lastRunningScan = DateTime.UtcNow;
				RunningWindows.Scan();
				RefreshTaskSnapshot();
				UpdateRunning();
				C.Board.UpdateRunning();
			}
		};
		runningTimer.Start();
		base.SourceInitialized += delegate
		{
			source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
			source.AddHook(Hook);
			hotkeys = new HotkeyManager(RegisterKey, delegate(int id)
			{
				Native.UnregisterHotKey(source.Handle, id);
			});
			List<string> list = hotkeys.Start(C.Prefs.Hotkeys);
			if (list.Count > 0)
			{
				TrayWarning(string.Join("、", list));
			}
		};
		System.Windows.Controls.ContextMenu contextMenu = MenuTheme.Create(C.Prefs.Light);
		System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
		{
			Header = "添加软件或文件到 Dock"
		};
		menuItem.Click += delegate
		{
			C.ChooseDockFiles();
		};
		contextMenu.Items.Add(menuItem);
		System.Windows.Controls.MenuItem menuItem2 = new System.Windows.Controls.MenuItem
		{
			Header = "外观设置"
		};
		menuItem2.Click += delegate
		{
			C.Settings();
		};
		contextMenu.Items.Add(menuItem2);
		AddDockControls(contextMenu);
		System.Windows.Controls.MenuItem menuItem3 = new System.Windows.Controls.MenuItem
		{
			Header = "退出"
		};
		menuItem3.Click += delegate
		{
			C.Quit();
		};
		contextMenu.Items.Add(menuItem3);
		contextMenu.Items.Insert(0, TaskManagerItem());
		base.ContextMenu = contextMenu;
		SystemEvents.DisplaySettingsChanged += DisplayChanged;
	}

	private void TrayWarning(string shortcuts)
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			if (C.Tray != null)
			{
				C.Tray.ShowBalloonTip(3000, "GlassBoard", shortcuts + " 已被占用，对应功能仍可从托盘操作。", ToolTipIcon.Info);
			}
		});
	}

	private bool RegisterKey(HotkeySetting key)
	{
		bool num = Native.RegisterHotKey(source.Handle, key.ActionId, key.Modifiers | 0x4000u, key.Key);
		if (!num)
		{
			Store.Log(new Win32Exception(Marshal.GetLastWin32Error(), "RegisterHotKey: " + HotkeyRules.Actions[key.ActionId - 1] + " " + HotkeyRules.Label(key)));
		}
		return num;
	}

	private void DisplayChanged(object s, EventArgs e)
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			C.Board.Hide();
			Refresh();
		});
	}

	public bool ApplyHotkeys(List<HotkeySetting> candidate, out string error)
	{
		if (hotkeys == null)
		{
			error = "快捷键窗口尚未就绪。";
			return false;
		}
		return hotkeys.TryApply(candidate, out error);
	}

	public bool HotkeyActive(int id)
	{
		if (hotkeys != null)
		{
			return hotkeys.Active.Any((HotkeySetting k) => k.ActionId == id);
		}
		return false;
	}

	public string RetryHotkeys()
	{
		if (hotkeys == null)
		{
			return "快捷键窗口尚未就绪。";
		}
		hotkeys.Release();
		List<string> list = hotkeys.Start(C.Prefs.Hotkeys);
		if (list.Count != 0)
		{
			return "以下组合被占用，请改键或使用开关：\n" + string.Join("\n", list);
		}
		return "已重新注册启用的快捷键。";
	}

	public List<HotkeySetting> SuspendHotkeys()
	{
		List<HotkeySetting> result = ((hotkeys == null) ? new List<HotkeySetting>() : hotkeys.Active.Select((HotkeySetting k) => k.Copy()).ToList());
		if (hotkeys != null)
		{
			hotkeys.Release();
		}
		return result;
	}

	public void ResumeHotkeys(List<HotkeySetting> keys)
	{
		if (hotkeys != null)
		{
			hotkeys.Release();
			List<string> list = hotkeys.Start(keys);
			if (list.Count > 0)
			{
				TrayWarning(string.Join("、", list));
			}
		}
	}

	public void ReleaseHotkey()
	{
		CancelDockPointer(refresh: false);
		autoTimer.Stop();
		runningTimer.Stop();
		taskHintTimer.Stop();
		if (hotkeys != null)
		{
			hotkeys.Release();
		}
		SystemEvents.DisplaySettingsChanged -= DisplayChanged;
	}

	private IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
	{
		switch (msg)
		{
		case 32815:
			handled = true;
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				C.ShellAction(3);
			});
			return IntPtr.Zero;
		case 786:
		{
			int id = w.ToInt32();
			handled = true;
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				if (id == 1)
				{
					C.Toggle();
				}
				else if (id == 2)
				{
					C.ShellAction(1);
				}
				else if (id == 3)
				{
					C.ShellAction(2);
				}
				else if (id == 4)
				{
					C.AddFromAnywhere();
				}
				else if (id == 5)
				{
					C.Settings();
				}
				else if (id == 6)
				{
					C.ShellAction(3);
				}
				else if (id == 7)
				{
					C.ToggleTaskbarAutoHide();
				}
			});
			break;
		}
		}
		return IntPtr.Zero;
	}

	public void Refresh(bool capture = true)
	{
		if (DockInputBusy)
		{
			return;
		}
		double width = base.Width;
		double left = base.Left;
		StopDockLayoutMotion();
		DockOptions dock = C.Prefs.Dock;
		dock.Normalize();
		LightTheme = C.Prefs.Light;
		MotionEnabled = dock.MotionEnabled;
		MotionLevel = dock.MotionLevel;
		ConfigureGlass(dock);
		base.Topmost = dock.AlwaysOnTop || dock.AutoHide || boardPinDragging;
		revealSerial++;
		BeginAnimation(Window.TopProperty, null);
		renderedHidden = false;
		hidePolicy = new AutoHidePolicy();
		moveGrip.Visibility = Visibility.Visible;
		moveGrip.Cursor = (C.Prefs.Placement.DockLocked ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.SizeAll);
		moveGrip.ToolTip = (C.Prefs.Placement.DockLocked ? "右键 Dock → 允许自由拖动 Dock" : "拖动把手或空白区域移动 Dock，松手保存");
		Items.Children.Clear();
		Items.Margin = new Thickness(dock.HoverPadding(), 0.0, dock.HoverPadding(), 0.0);
		Surface.CornerRadius = new CornerRadius(dock.Radius);
		AddSystemButton("glassboard://dock/apps", DockButton("▦", null, delegate
		{
			C.Toggle();
		}, "展开大号应用面板"), delegate
		{
			C.Toggle();
		});
		if (dock.ShowComputer)
		{
			AddSystemButton("glassboard://dock/computer", DockButton("computer", null, delegate
			{
				C.Board.Hide();
				Native.Computer();
			}, "此电脑"), delegate
			{
				C.Board.Hide();
				Native.Computer();
			});
		}
		Rect rect = (dockWorkingArea = DesktopWindowPolicy.WorkArea(this));
		dockScreenArea = DesktopWindowPolicy.WorkArea(this, pointer: false, fullScreen: true);
		List<Entry> list = DockOrderRules.Ordered(C.Prefs);
		int num = (taskWheel.Running ? taskGroups.Count : list.Count);
		int num2 = dock.VisiblePinned(rect.Width - 20.0, num);
		int num3 = ((num > num2) ? Math.Max(0, num2 - 1) : num2);
		if (taskWheel.Running)
		{
			BuildTaskButtons(num3, num2);
		}
		else
		{
			foreach (Entry a in list.Take(num3))
			{
				System.Windows.Controls.Button button = DockButton(null, FeatureUi.Icon(a, C.Prefs.Dock.ShowShortcutOverlays, C.Prefs.Dock.SharperIcons), delegate
				{
					C.Launch(a);
				}, a.Name, a);
				button.ContextMenu = C.Menu(a, "Dock");
				C.AttachSystemFileMenu(button, a, "Dock");
				AddDockControls(button.ContextMenu);
				Items.Children.Add(button);
			}
			if (list.Count > num3 && num2 > 0)
			{
				System.Windows.Controls.Button more = DockButton("···", null, delegate
				{
				}, "更多固定应用");
				System.Windows.Controls.ContextMenu menu = MenuTheme.Create(C.Prefs.Light);
				foreach (Entry item2 in list.Skip(num3))
				{
					System.Windows.Controls.MenuItem item = new System.Windows.Controls.MenuItem
					{
						Header = item2.Name
					};
					Entry entry = item2;
					item.Click += delegate
					{
						if (!DockDragSuppresses(item))
						{
							C.Launch(entry);
						}
					};
					BindDockSource(item, entry, delegate
					{
						menu.IsOpen = false;
					});
					menu.Items.Add(item);
				}
				AddDockControls(menu);
				more.ContextMenu = menu;
				more.Click += delegate
				{
					menu.PlacementTarget = more;
					menu.Placement = PlacementMode.Top;
					menu.IsOpen = true;
				};
				Items.Children.Add(more);
			}
			if (list.Count == 0)
			{
				Items.Children.Add(DockButton("+", null, delegate
				{
					C.Toggle(force: true);
				}, "打开面板，可拖入软件固定"));
			}
		}
		AddQuickButtons();
		if (dock.ShowSettings)
		{
			AddSystemButton("glassboard://dock/settings", DockButton("⚙", null, delegate
			{
				C.Settings();
			}, "DIY 设置"), delegate
			{
				C.Settings();
			});
		}
        Action openTaskManager = delegate { try { ApplicationTasks.OpenTaskManager(); } catch (Exception ex) { ShowTaskHint(ex.Message); } };
        AddSystemButton(DockLayoutRules.TaskManager, DockButton(null, TaskManagerIcon.Create(), openTaskManager, "任务管理器"), openTaskManager);
		ArrangeDockItems();
		base.Width = Math.Min(rect.Width - 20.0, dock.WidthFor((num == 0) ? 1 : num2));
		double num4 = dock.IconSize + 16.0 + (double)(dock.ShowLabels ? 18 : 0);
		double num5 = Math.Min(1.6, 1.0 + (dock.Zoom - 1.0) * MotionLevel);
		base.Height = 28.0 + num4 + dock.Padding * 2.0 + Math.Max(0.0, num4 * (num5 - 1.0) * 0.65 + 5.0 * MotionLevel - dock.Padding) * 2.0;
		double num6 = ((dock.Alignment == 0) ? (rect.Left + 10.0) : ((dock.Alignment == 2) ? (rect.Right - base.Width - 10.0) : (rect.Left + (rect.Width - base.Width) / 2.0)));
		PlacementOptions placement = C.Prefs.Placement;
		if (placement.DockLeft.HasValue && !placement.DockCenter.HasValue)
		{
			placement.DockCenter = placement.DockLeft.Value + base.Width / 2.0;
		}
		base.Left = DockAnchorRules.Left(placement.DockLeft.HasValue ? placement.DockCenter : null, base.Width, num6 + dock.Offset, rect.Left, rect.Width);
		base.Top = FeatureRules.Position(C.Prefs.Placement.DockTop ?? (rect.Bottom - base.Height - C.Prefs.DockGap), rect.Top, rect.Height, base.Height);
		shownTop = base.Top;
		if (dock.AutoHide)
		{
			autoTimer.Start();
		}
		else
		{
			autoTimer.Stop();
		}
		GlassPercent = dock.OpacityPercent;
		if (capture)
		{
			Backdrop(C.Prefs.Light);
		}
		else
		{
			SetGlassOpacity(GlassPercent);
			foreach (Border item3 in from b in Root.Children.OfType<Border>()
				where b.Tag as string == "backdrop"
				select b)
			{
				RectangleGeometry clip = (RectangleGeometry)(item3.Clip = new RectangleGeometry(new Rect(0.0, 0.0, Math.Max(1.0, base.Width - 28.0), Math.Max(1.0, base.Height - 28.0)), dock.Radius, dock.Radius));
				if (item3.Child is Border border)
				{
					border.Clip = clip;
					border.CornerRadius = Surface.CornerRadius;
					border.Effect = new BlurEffect
					{
						Radius = base.MaterialBlur,
						RenderingBias = RenderingBias.Performance
					};
				}
			}
		}
		System.Windows.Media.Color color = Accent(dock.Accent);
		Surface.BorderBrush = ((GlassPercent == 0.0) ? ((System.Windows.Media.Brush)System.Windows.Media.Brushes.Transparent) : ((System.Windows.Media.Brush)new LinearGradientBrush(System.Windows.Media.Color.FromArgb((byte)Math.Min(255.0, 160.0 * EdgeStrength), color.R, color.G, color.B), System.Windows.Media.Color.FromArgb((byte)Math.Min(255.0, 30.0 * EdgeStrength), byte.MaxValue, byte.MaxValue, byte.MaxValue), 90.0)));
		UpdateRunning();
		if (!capture)
		{
			AnimateDockLayout(left, width);
		}
	}

	private void UpdateAutoHide()
	{
		ReleaseCancelledExternalDrag();
		if (!C.GlassSuppressed && source != null && base.IsVisible)
		{
			System.Drawing.Point position = System.Windows.Forms.Cursor.Position;
			System.Windows.Point point = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(position.X, position.Y));
			double bottom = dockScreenArea.Bottom;
			bool over = !renderedHidden && point.X >= base.Left && point.X <= base.Left + base.Width && point.Y >= shownTop && point.Y <= shownTop + base.Height;
			bool edge = point.X >= dockScreenArea.Left && point.X < dockScreenArea.Right
				&& point.Y >= bottom - C.Prefs.Dock.EdgeRange && point.Y < bottom;
			if (edge && !pointerAtRevealEdge && C.Prefs.Dock.AutoHide && !C.UiBusy && !C.Board.IsVisible)
			{
				DesktopWindowPolicy.RaiseWithoutActivation(this);
			}
			pointerAtRevealEdge = edge;
			bool flag = (base.ContextMenu != null && base.ContextMenu.IsOpen) || Items.Children.OfType<System.Windows.Controls.Button>().Any((System.Windows.Controls.Button b) => b.ContextMenu != null && b.ContextMenu.IsOpen);
			bool flag2 = hidePolicy.Update(DateTime.UtcNow, C.Prefs.Dock.AutoHide, over, edge, dragging || DockInputBusy || externalDockDrag || (taskMenu != null && taskMenu.IsOpen) || flag || C.UiBusy || C.Board.IsVisible, C.Prefs.Dock.HideDelay);
			if (flag2 != renderedHidden)
			{
				renderedHidden = flag2;
				double target = (flag2 ? (bottom - 20.0) : shownTop);
				AnimateDockVisibility(flag2, target);
			}
		}
	}

	private void UpdateRunning()
	{
		foreach (System.Windows.Controls.Button item in Items.Children.OfType<System.Windows.Controls.Button>())
		{
			Entry entry = item.Tag as Entry;
			Grid grid = item.Content as Grid;
			if (entry == null || grid == null)
			{
				continue;
			}
			foreach (Border item2 in from b in grid.Children.OfType<Border>()
				where b.Tag as string == "running"
				select b)
			{
				item2.Visibility = ((!C.Prefs.Dock.ShowRunning || !RunningWindows.IsRunning(entry)) ? Visibility.Collapsed : Visibility.Visible);
			}
		}
	}

	private static System.Windows.Media.Color Accent(int index)
	{
		return (new System.Windows.Media.Color[6]
		{
			System.Windows.Media.Color.FromRgb(121, 185, 250),
			System.Windows.Media.Color.FromRgb(183, 149, 245),
			System.Windows.Media.Color.FromRgb(105, 218, 183),
			System.Windows.Media.Color.FromRgb(242, 193, 104),
			System.Windows.Media.Color.FromRgb(244, 150, 178),
			System.Windows.Media.Color.FromRgb(194, 203, 217)
		})[index];
	}

	private System.Windows.Controls.Button DockButton(string text, ImageSource icon, Action action, string tip, Entry entry = null)
	{
		DockOptions o = C.Prefs.Dock;
		double iconSize = o.IconSize;
		System.Windows.Controls.Button button = Button("", delegate(object s, RoutedEventArgs e)
		{
			if (!dockIconDragging && !DockDragSuppresses(s as FrameworkElement))
			{
				action();
			}
		}, iconSize + 16.0);
		button.Height = iconSize + 16.0 + (double)(o.ShowLabels ? 18 : 0);
		button.Margin = new Thickness(o.Spacing / 2.0, 0.0, o.Spacing / 2.0, 0.0);
		button.ToolTip = tip;
		ToolTipService.SetInitialShowDelay(button, 200);
		ToolTipService.SetShowDuration(button, 3500);
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(Border));
		frameworkElementFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, byte.MaxValue, byte.MaxValue, byte.MaxValue)));
		FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(ContentPresenter));
		frameworkElementFactory2.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
		frameworkElementFactory2.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		frameworkElementFactory.AppendChild(frameworkElementFactory2);
		button.Template = new ControlTemplate(typeof(System.Windows.Controls.Button))
		{
			VisualTree = frameworkElementFactory
		};
		FrameworkElement element;
		if (!(text == "computer"))
		{
			element = ((icon == null) ? ((FrameworkElement)new TextBlock
			{
				Text = (text ?? "□"),
				FontSize = iconSize * 0.72,
				Width = iconSize,
				Height = iconSize,
				TextAlignment = TextAlignment.Center,
				Foreground = base.TextBrush,
				VerticalAlignment = VerticalAlignment.Center
			}) : ((FrameworkElement)new System.Windows.Controls.Image
			{
				Source = icon,
				Width = iconSize,
				Height = iconSize,
				Stretch = Stretch.Uniform
			}));
		}
		else
		{
			System.Windows.Media.Color endColor = Accent(o.Accent);
			Grid grid = new Grid
			{
				Width = 44.0,
				Height = 44.0
			};
			bool flag = o.ComputerStyle == 0;
			if (flag)
			{
				grid.Children.Add(new Border
				{
					CornerRadius = new CornerRadius(12.0),
					Background = new LinearGradientBrush(System.Windows.Media.Color.FromRgb((byte)((double)(int)endColor.R * 0.65), (byte)((double)(int)endColor.G * 0.65), (byte)((double)(int)endColor.B * 0.65)), endColor, 45.0),
					BorderThickness = new Thickness(1.0),
					BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, byte.MaxValue, byte.MaxValue, byte.MaxValue))
				});
			}
			System.Windows.Media.Brush brush = (flag ? System.Windows.Media.Brushes.White : base.TextBrush);
			grid.Children.Add(new Border
			{
				Height = (flag ? 19 : 25),
				Width = (flag ? 29 : 38),
				CornerRadius = new CornerRadius(3.0),
				BorderThickness = new Thickness(1.6),
				BorderBrush = brush,
				Background = (flag ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(36, byte.MaxValue, byte.MaxValue, byte.MaxValue)) : System.Windows.Media.Brushes.Transparent),
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(0.0, flag ? 9 : 5, 0.0, 0.0)
			});
			grid.Children.Add(new Border
			{
				Width = 2.0,
				Height = 5.0,
				Background = brush,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(0.0, flag ? 28 : 30, 0.0, 0.0)
			});
			grid.Children.Add(new Border
			{
				Width = 15.0,
				Height = 1.6,
				CornerRadius = new CornerRadius(1.0),
				Background = brush,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(0.0, flag ? 33 : 35, 0.0, 0.0)
			});
			if (flag)
			{
				grid.Children.Add(new Border
				{
					Width = 19.0,
					Height = 1.0,
					Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
					VerticalAlignment = VerticalAlignment.Top,
					Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
				});
			}
			element = new Viewbox
			{
				Width = iconSize,
				Height = iconSize,
				Child = grid,
				Stretch = Stretch.Uniform
			};
		}
		StackPanel stackPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel.Children.Add(element);
		if (o.ShowLabels)
		{
			stackPanel.Children.Add(new TextBlock
			{
				Text = text switch
				{
					"···" => "更多", 
					"⚙" => "设置", 
					"▦" => "应用", 
					"computer" => "此电脑", 
					_ => tip, 
				},
				FontSize = 10.0,
				Foreground = base.MutedBrush,
				TextAlignment = TextAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis,
				MaxWidth = iconSize + 12.0,
				Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
			});
		}
		Grid grid2 = new Grid
		{
			IsHitTestVisible = false
		};
		grid2.Children.Add(stackPanel);
		if (entry != null)
		{
			button.Tag = entry;
			grid2.Children.Add(new Border
			{
				Tag = "running",
				Width = 5.0,
				Height = 4.0,
				CornerRadius = new CornerRadius(2.0),
				Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(135, 205, byte.MaxValue)),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Bottom,
				Visibility = Visibility.Collapsed
			});
		}
		button.Content = grid2;
		ScaleTransform scale = new ScaleTransform(1.0, 1.0);
		TranslateTransform lift = new TranslateTransform();
		TransformGroup transformGroup = new TransformGroup();
		transformGroup.Children.Add(scale);
		transformGroup.Children.Add(lift);
		button.RenderTransform = transformGroup;
		button.RenderTransformOrigin = new System.Windows.Point(0.5, 0.65);
		Action<bool> animate = delegate(bool hover)
		{
			if (MotionEnabled)
			{
				CubicEase easingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				};
				double toValue = (hover ? Math.Min(1.6, 1.0 + (o.Zoom - 1.0) * MotionLevel) : 1.0);
				scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(toValue, TimeSpan.FromMilliseconds(180.0))
				{
					EasingFunction = easingFunction
				});
				scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(toValue, TimeSpan.FromMilliseconds(180.0))
				{
					EasingFunction = easingFunction
				});
				lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(hover ? (-5.0 * MotionLevel) : 0.0, TimeSpan.FromMilliseconds(180.0))
				{
					EasingFunction = easingFunction
				});
			}
		};
		button.MouseEnter += delegate
		{
			if (!DockInputBusy)
			{
				animate(obj: true);
			}
		};
		button.MouseLeave += delegate
		{
			animate(obj: false);
		};
		if (entry != null)
		{
			BindDockIcon(button, entry);
		}
		return button;
	}

	[DllImport("PowrProf.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.U1)]
	private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool disableWake);

	private void AddQuickButtons()
	{
		if (C.Prefs.Dock.ShowPower)
		{
			AddQuickButton("glassboard://dock/power", "⏻", "电源", new string[3] { "睡眠", "重启", "关机" }, delegate(int i)
			{
				Power(i);
			});
		}
		if (!C.Prefs.Dock.ShowNetwork)
		{
			return;
		}
		AddQuickButton("glassboard://dock/network", "⌁", "控制中心", new string[2] { "打开控制中心", "小球设置" }, delegate(int i)
		{
			if (i == 0)
			{
				C.OpenControlCenter();
			}
			else
			{
				C.ControlCenterSettings(this);
			}
		});
	}

	private void AddQuickButton(string key, string glyph, string title, string[] names, Action<int> action)
	{
		System.Windows.Controls.Button b = DockButton(glyph, null, delegate
		{
		}, title);
		System.Windows.Controls.ContextMenu menu = MenuTheme.Create(C.Prefs.Light);
		for (int i = 0; i < names.Length; i++)
		{
			int index = i;
			System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
			{
				Header = names[i]
			};
			menuItem.Click += delegate
			{
				action(index);
			};
			menu.Items.Add(menuItem);
		}
		b.ContextMenu = menu;
		Action open = delegate
		{
			menu.PlacementTarget = b;
			menu.Placement = PlacementMode.Top;
			menu.IsOpen = true;
		};
		b.Click += delegate
		{
			if (!DockDragSuppresses(b))
			{
				open();
			}
		};
		AddSystemButton(key, b, open);
	}

	private void Power(int action)
	{
		string text = (new string[3] { "睡眠", "重启", "关机" })[action];
		if (System.Windows.MessageBox.Show(this, "确定要" + text + "电脑吗？请先保存正在编辑的内容。", "电源 · " + text, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			if (action == 0)
			{
				if (!SetSuspendState(hibernate: false, force: false, disableWake: false))
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}
			}
			else
			{
				Process.Start(new ProcessStartInfo("shutdown.exe", (action == 1) ? "/r /t 0" : "/s /t 0")
				{
					UseShellExecute = false,
					CreateNoWindow = true
				});
			}
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show(this, ex.Message, "电源操作失败");
		}
	}
}
