using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GlassBoard;

public class BoardWindow : GlassWindow
{
	private TextBlock groupHeading;
	private Border dragPreview;

	private Image dragPreviewIcon;

	private TextBlock dragPreviewText;

	private bool activeDrag;

	private string hoverGroup;

	private string folderSignature = "";

	private readonly DispatcherTimer hoverTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(700.0)
	};

	private readonly DispatcherTimer dragWatch = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(120.0)
	};

	private bool entryDragRunning;

	private readonly DispatcherTimer sizeRefresh = new DispatcherTimer(DispatcherPriority.Background)
	{
		Interval = TimeSpan.FromMilliseconds(100.0)
	};

	private bool resizingPanel;

	private bool refreshingSize;

	private bool iconsDirty = true;

	public bool ModalBusy;

	private Controller C;

	private TextBox Search;

	private UniformGrid Tiles;

	private TextBlock Pager;

	private TextBlock Status;

	private TextBlock Empty;

	private Grid layout;

	private int page;
	private int wheelRemainder;

	private int capacity = 35;

	private int columns = 7;

	private Point dragStart;

	private Entry dragEntry;

	private bool editing;

	private readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private WrapPanel bulkBar;

	private RowDefinition bulkRow;

	private TextBlock selectionCount;

	private Button selectAll;

	private Button clearSelection;

	private Button deleteSelected;

	private Button undoRemoval;

	private Button editSelection;

	private Button moveSelected;

	private ContextMenu iconMenu;

	private ComboBox groups;

	private ComboBox moveGroup;

	private string currentGroup = "全部";

	private bool syncingGroups;

	private bool iconSizeSync;

	private Slider boardIconSlider;

	private TextBlock iconSizeText;

	private Button addExisting;

	private Thumb moveGrip;

	private Thumb resizeGrip;

	private int visibilitySerial;

	private WrapPanel folderBar;

	private readonly DispatcherTimer idleRelease = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(2.0)
	};

	private bool preparingView;

	private string pendingStatus;

	private bool foldersVisible
	{
		get
		{
			if (!C.Prefs.BoardHideGroups)
			{
				return !C.Prefs.BoardHideFolders;
			}
			return false;
		}
	}

	public string CurrentGroup => currentGroup;

	internal bool HasView => layout != null;

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int key);

	internal void ClearDragState()
	{
		EndDragPreview();
	}

	private void InitDragExperience()
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		dragPreviewIcon = new Image
		{
			Width = 28.0,
			Height = 28.0,
			Stretch = Stretch.Uniform,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
		};
		dragPreviewText = new TextBlock
		{
			Foreground = Brushes.White,
			VerticalAlignment = VerticalAlignment.Center,
			MaxWidth = 230.0,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		stackPanel.Children.Add(dragPreviewIcon);
		stackPanel.Children.Add(dragPreviewText);
		dragPreview = new Border
		{
			Child = stackPanel,
			Padding = new Thickness(12.0, 9.0, 12.0, 9.0),
			CornerRadius = new CornerRadius(14.0),
			Background = Polish.Color("#E52A3447"),
			BorderBrush = Polish.Color("#80B8CEEF"),
			BorderThickness = new Thickness(1.0),
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			IsHitTestVisible = false,
			Visibility = Visibility.Collapsed
		};
		Root.Children.Add(dragPreview);
		dragWatch.Tick += delegate
		{
			RecoverExternalDrag(((uint)GetAsyncKeyState(1) & 0x8000u) != 0 || ((uint)GetAsyncKeyState(2) & 0x8000u) != 0 || (GetAsyncKeyState(4) & 0x8000) != 0);
		};
		base.IsVisibleChanged += delegate
		{
			if (!base.IsVisible)
			{
				EndDragPreview();
			}
		};
		base.PreviewMouseDown += delegate
		{
			if (!entryDragRunning && activeDrag)
			{
				EndDragPreview();
				Render();
			}
		};
		hoverTimer.Tick += delegate
		{
			hoverTimer.Stop();
			if (activeDrag && hoverGroup != null && hoverGroup != currentGroup)
			{
				OpenGroup(hoverGroup);
			}
		};
		base.PreviewDragOver += delegate(object s, DragEventArgs e)
		{
			string[] array = DraggedEntries(e.Data);
			bool dataPresent = e.Data.GetDataPresent(DataFormats.FileDrop);
			if (array.Length != 0 || dataPresent)
			{
				activeDrag = true;
				dragWatch.Start();
				Point position2 = e.GetPosition(Root);
				if (dragPreviewText.Text.Length == 0)
				{
					if (dataPresent)
					{
						string[] array2 = (string[])e.Data.GetData(DataFormats.FileDrop);
						dragPreviewText.Text = ((array2.Length == 1) ? Path.GetFileName(array2[0]) : ("添加 " + array2.Length + " 个文件"));
					}
					else
					{
						dragPreviewText.Text = "正在移动图标";
					}
				}
				dragPreview.RenderTransform = new TranslateTransform(position2.X + 16.0, position2.Y + 16.0);
				dragPreview.Visibility = Visibility.Visible;
			}
		};
		base.PreviewDragLeave += delegate(object s, DragEventArgs e)
		{
			Point position = e.GetPosition(Root);
			if (position.X < 0.0 || position.Y < 0.0 || position.X > base.ActualWidth || position.Y > base.ActualHeight)
			{
				EndDragPreview();
				Render();
			}
		};
		base.PreviewDrop += delegate
		{
			EndDragPreview();
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				if (!activeDrag && base.IsVisible)
				{
					Render();
				}
			});
		};
		base.Closed += delegate
		{
			hoverTimer.Stop();
			dragWatch.Stop();
		};
	}

	private void RecoverExternalDrag(bool buttonsDown)
	{
		if (!(entryDragRunning || !activeDrag || buttonsDown))
		{
			EndDragPreview();
			Render();
		}
	}

	internal void CheckBoardFeedback16()
	{
		Open();
		C.Prefs.Groups.Add("待删除测试组");
		C.Prefs.Apps.Add(new Entry
		{
			Name = "保留入口",
			Path = "C:\\keep-feedback16.lnk",
			Group = "待删除测试组"
		});
		SyncGroups();
		Render();
		activeDrag = true;
		dragPreview.Visibility = Visibility.Visible;
		FeatureRules.DeleteGroup(C.Prefs, "待删除测试组");
		SyncGroups();
		Render();
		if (activeDrag || dragPreview.Visibility != Visibility.Collapsed || folderBar.Children.OfType<Border>().Any((Border b) => (string)b.Tag == "待删除测试组") || !C.Prefs.Apps.Any((Entry a) => a.Name == "保留入口" && FeatureRules.InGroup(a, "未分组")))
		{
			throw new Exception("删除分组后界面或入口未同步");
		}
		activeDrag = true;
		dragPreview.Visibility = Visibility.Visible;
		RecoverExternalDrag(buttonsDown: false);
		if (activeDrag || dragPreview.Visibility != Visibility.Collapsed)
		{
			throw new Exception("外部拖拽取消未清理");
		}
		activeDrag = true;
		Hide();
		if (activeDrag || dragWatch.IsEnabled)
		{
			throw new Exception("隐藏面板后拖拽状态残留");
		}
	}

	private void BeginGroupHover(string name)
	{
		if (!(hoverGroup == name))
		{
			hoverGroup = name;
			hoverTimer.Stop();
			hoverTimer.Start();
		}
	}

	private void StopGroupHover(string name)
	{
		if (name == null || hoverGroup == name)
		{
			hoverGroup = null;
			hoverTimer.Stop();
		}
	}

	private void EndDragPreview()
	{
		activeDrag = false;
		dragWatch.Stop();
		hoverTimer.Stop();
		hoverGroup = null;
		dragPreview.Visibility = Visibility.Collapsed;
		dragPreviewText.Text = "";
		dragPreviewIcon.Source = null;
		folderSignature = "";
	}

	private void StartEntryDrag(FrameworkElement tile, Entry entry)
	{
		string[] array = ((editing && selected.Contains(entry.Path)) ? selected.ToArray() : new string[1] { entry.Path });
		DataObject dataObject = new DataObject();
		dataObject.SetData("GlassBoard.Entry", entry.Path);
		dataObject.SetData("GlassBoard.Entries", array);
		dataObject.SetData("GlassBoard.SourceGroup", currentGroup);
		dragPreviewText.Text = ((array.Length == 1) ? entry.Name : (entry.Name + " 等 " + array.Length + " 个"));
		try
		{
			dragPreviewIcon.Source = FeatureUi.Icon(entry, C.Prefs.Dock.ShowShortcutOverlays, C.Prefs.Dock.SharperIcons);
		}
		catch
		{
			dragPreviewIcon.Source = null;
		}
		bool modalBusy = ModalBusy;
		ModalBusy = true;
		activeDrag = true;
		C.Dock.BeginBoardPinDrag();
		entryDragRunning = true;
		try
		{
			DragDrop.DoDragDrop(tile, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
		}
		finally
		{
			entryDragRunning = false;
			C.Dock.EndBoardPinDrag();
			EndDragPreview();
			ModalBusy = modalBusy;
			Render();
		}
	}

	private DragDropEffects GroupDragEffect(IDataObject data, string name)
	{
		if (name == "全部")
		{
			return DragDropEffects.None;
		}
		if (DraggedEntries(data).Length != 0)
		{
			if (!data.GetDataPresent("GlassBoard.SourceGroup") || !((string)data.GetData("GlassBoard.SourceGroup") == "全部"))
			{
				return DragDropEffects.Move;
			}
			return DragDropEffects.Copy;
		}
		if (!data.GetDataPresent(DataFormats.FileDrop))
		{
			return DragDropEffects.None;
		}
		return DragDropEffects.Copy;
	}

	public void ApplyDisplayOptions()
	{
		if (C.Prefs.BoardHideGroups)
		{
			currentGroup = "全部";
		}
		if (C.Prefs.BoardHideSearch && Search != null)
		{
			Search.Clear();
		}
		if (base.IsVisible)
		{
			BuildForSize();
			QueueSizeRefresh();
		}
	}

	private void MoreMenu()
	{
		EndDragPreview();
		ContextMenu menu = MenuTheme.Create(C.Prefs.Light);
		iconMenu = menu;
		menu.Closed += delegate
		{
			if (iconMenu == menu)
			{
				iconMenu = null;
			}
		};
		Action<string, Action, bool> obj = delegate(string label, Action action, bool enabled)
		{
			MenuItem menuItem = new MenuItem
			{
				Header = label,
				IsEnabled = enabled
			};
			menuItem.Click += delegate
			{
				bool modalBusy = ModalBusy;
				ModalBusy = true;
				try
				{
					menu.IsOpen = false;
					action();
				}
				finally
				{
					ModalBusy = modalBusy;
				}
			};
			menu.Items.Add(menuItem);
		};
		obj("撤销 · Ctrl+Z", delegate
		{
			C.UndoBoard();
		}, C.CanUndoRemoval);
		obj("重做 · Ctrl+Y", delegate
		{
			C.RedoBoard();
		}, C.CanRedoBoard);
		menu.Items.Add(new Separator());
		obj("新建文件或文件夹", delegate
		{
			NewFileMenu();
		}, arg3: true);
		obj("图标间距与默认大小", delegate
		{
			IconSettings();
		}, arg3: true);
		obj(C.Prefs.BoardHideSearch ? "显示搜索栏" : "隐藏搜索栏", delegate
		{
			C.Prefs.BoardHideSearch = !C.Prefs.BoardHideSearch;
			C.Save();
			ApplyDisplayOptions();
		}, arg3: true);
		obj(C.Prefs.BoardHideGroups ? "显示分组栏" : "隐藏分组栏 · 显示全部图标", delegate
		{
			C.Prefs.BoardHideGroups = !C.Prefs.BoardHideGroups;
			C.Save();
			ApplyDisplayOptions();
		}, arg3: true);
		obj(foldersVisible ? "收起分组文件夹" : "展开分组文件夹", delegate
		{
			C.Prefs.BoardHideFolders = !C.Prefs.BoardHideFolders;
			C.Save();
			ApplyDisplayOptions();
		}, !C.Prefs.BoardHideGroups);
		obj("自动分组", delegate
		{
			C.AutoGroupApps();
		}, arg3: true);
		obj("管理分组", delegate
		{
			C.ManageGroups();
		}, arg3: true);
		obj("工作区", delegate
		{
			C.ManageWorkspaces();
		}, arg3: true);
		obj("外观方案", delegate
		{
			C.ManageSchemes();
		}, arg3: true);
		menu.Items.Add(new Separator());
		obj("导出完整备份", delegate
		{
			C.ExportFullBackup();
		}, arg3: true);
		obj("恢复完整备份", delegate
		{
			C.RestoreFullBackup();
		}, arg3: true);
		menu.PlacementTarget = this;
		menu.IsOpen = true;
	}

	private void BoardBlankMenu(object sender, MouseButtonEventArgs e)
	{
		DependencyObject dependencyObject = e.OriginalSource as DependencyObject;
		while (dependencyObject != null && dependencyObject != this)
		{
			if (dependencyObject is Border category && folderBar != null && folderBar.Children.Contains(category) && category.ContextMenu == null)
			{
				e.Handled = true;
				return;
			}
			if (dependencyObject is FrameworkElement frameworkElement && (frameworkElement.ContextMenu != null || frameworkElement.Tag is Entry || frameworkElement is ButtonBase || frameworkElement is TextBox || frameworkElement is ComboBox || frameworkElement is Thumb || frameworkElement is Slider))
			{
				return;
			}
			dependencyObject = ((dependencyObject is Visual) ? VisualTreeHelper.GetParent(dependencyObject) : LogicalTreeHelper.GetParent(dependencyObject));
		}
		NewFileMenu();
		e.Handled = true;
	}

	private void NewFileMenu()
	{
		EndDragPreview();
		ContextMenu menu = MenuTheme.Create(C.Prefs.Light);
		iconMenu = menu;
		menu.Closed += delegate
		{
			if (iconMenu == menu)
			{
				iconMenu = null;
			}
		};
		string[] array = new string[5] { "文本文档 (.txt)", "Markdown 文档 (.md)", "Word 文档 (.docx)", "Excel 工作簿 (.xlsx)", "文件夹" };
		for (int i = 0; i < array.Length; i++)
		{
			int index = i;
			MenuItem menuItem = new MenuItem
			{
				Header = "新建" + array[i]
			};
			menuItem.Click += delegate
			{
				menu.IsOpen = false;
				C.CreateBoardFile((index == 4) ? null : BoardFiles.Extensions[index], currentGroup);
			};
			menu.Items.Add(menuItem);
		}
		menu.Items.Add(new Separator());
		MenuItem createGroup = new MenuItem { Header = "新建分组" };
		createGroup.Click += delegate
		{
			menu.IsOpen = false;
			C.CreateGroupFromMenu();
		};
		menu.Items.Add(createGroup);
		var arrange = new MenuItem { Header = "自动整理（按类型）", ToolTip = "整理当前分组：系统入口、文件夹、程序、快捷方式、脚本、文档、图片、音视频、压缩包及其他；同类按名称排列" };
		arrange.Click += delegate { menu.IsOpen = false; C.AutoArrangeEntries(currentGroup); };
		menu.Items.Add(arrange);
		if (C.Prefs.UnassignedHidden)
		{
			var restore = new MenuItem { Header = "恢复分组「" + C.Prefs.GroupLabel("未分组") + "」" };
			restore.Click += delegate { menu.IsOpen = false; C.RestoreUnassignedGroup(); };
			menu.Items.Add(restore);
		}
		menu.PlacementTarget = this;
		menu.IsOpen = true;
	}

	private void InitResizeRefresh()
	{
		sizeRefresh.Tick += delegate
		{
			sizeRefresh.Stop();
			ApplySizeRefresh();
		};
	}

	private void QueueSizeRefresh()
	{
		if (Tiles != null && !resizingPanel && !refreshingSize && (base.IsVisible || preparingView) && !sizeRefresh.IsEnabled)
		{
			sizeRefresh.Start();
		}
	}

	private void ApplySizeRefresh()
	{
		if (Tiles == null || resizingPanel || refreshingSize || (!base.IsVisible && !preparingView))
		{
			return;
		}
		refreshingSize = true;
		try
		{
			int num = BoardIconRules.Columns((Tiles.ActualWidth > 0.0) ? Tiles.ActualWidth : (base.Width - 104.0), C.Prefs.BoardIconSize, C.Prefs.BoardIconGap);
			int num2 = BoardIconRules.Rows((Tiles.ActualHeight > 0.0) ? Tiles.ActualHeight : (base.Height - (double)(C.Prefs.BoardHideGroups ? 180 : 258)), C.Prefs.BoardIconSize, C.Prefs.BoardIconGap);
			if (iconsDirty || columns != num || Tiles.Rows != num2)
			{
				iconsDirty = false;
				columns = num;
				capacity = num * num2;
				Tiles.Columns = num;
				Tiles.Rows = num2;
				Render();
			}
		}
		catch (Exception ex)
		{
			Store.Log(ex);
			SetStatus("面板刷新未完成，请重新打开面板。");
		}
		finally
		{
			refreshingSize = false;
		}
	}

	internal void CheckResizeRefresh()
	{
		Open();
		string text = Search.Text;
		TextBox search = Search;
		UniformGrid tiles = Tiles;
		double[] array = new double[4] { 900.0, 1200.0, 920.0, 1260.0 };
		foreach (double width in array)
		{
			base.Width = width;
			base.Height = 620.0;
			UpdateLayout();
			QueueSizeRefresh();
			ApplySizeRefresh();
			UpdateLayout();
		}
		sizeRefresh.Stop();
		if (search != Search || tiles != Tiles || Search.Text != text)
		{
			throw new Exception("缩放不应重建搜索框或图标宿主");
		}
		Hide();
	}

	public void ClearSearch()
	{
		if (Search != null)
		{
			Search.Clear();
		}
	}

	public BoardWindow(Controller c)
		: base(42.0)
	{
		C = c;
		base.Title = "GlassBoard 应用启动台";
		base.AllowDrop = true;
		InitDragExperience();
		base.IsVisibleChanged += delegate
		{
			if (base.IsVisible)
			{
				idleRelease.Stop();
			}
			else if (layout != null)
			{
				idleRelease.Start();
			}
		};
		idleRelease.Tick += delegate
		{
			if (base.IsVisible)
			{
				idleRelease.Stop();
			}
			else if (ReleaseHiddenView())
			{
				idleRelease.Stop();
			}
		};
		base.Closed += delegate
		{
			idleRelease.Stop();
			sizeRefresh.Stop();
		};
		base.PreviewMouseWheel += BoardMouseWheel;
		base.PreviewKeyDown += delegate(object s, KeyEventArgs e)
		{
			if (Search != null)
			{
				if (e.Key == Key.Escape)
				{
					EndDragPreview();
					CollapseSmooth();
					e.Handled = true;
				}
				else if (!Search.IsKeyboardFocusWithin && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && (e.Key == Key.Z || e.Key == Key.Y))
				{
					if (e.Key == Key.Y || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
					{
						C.RedoBoard();
					}
					else
					{
						C.UndoBoard();
					}
					e.Handled = true;
				}
				else if (editing && !Search.IsKeyboardFocusWithin && e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
				{
					SelectFiltered();
					e.Handled = true;
				}
				else if (editing && !Search.IsKeyboardFocusWithin && e.Key == Key.Delete)
				{
					C.RemoveMany(selected.ToArray());
					e.Handled = true;
				}
				else if (e.Key == Key.Next)
				{
					ChangePage(1);
					e.Handled = true;
				}
				else if (e.Key == Key.Prior)
				{
					ChangePage(-1);
					e.Handled = true;
				}
				else if (e.Key == Key.Return && Search.IsKeyboardFocusWithin && !editing)
				{
					Entry entry = Filtered().FirstOrDefault();
					if (entry != null)
					{
						C.Launch(entry);
					}
					e.Handled = true;
				}
			}
		};
		base.Drop += ExternalDrop;
		base.PreviewMouseRightButtonUp += BoardBlankMenu;
		base.Deactivated += delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				if (C.Prefs.BoardDismissOnDeactivate && base.IsVisible && !base.IsActive && !ModalBusy && !activeDrag && !IsContextMenuOpen() && Mouse.LeftButton != MouseButtonState.Pressed)
				{
					CollapseSmooth();
				}
			});
		};
		moveGrip = new Thumb
		{
			Width = 60.0,
			Height = 8.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
			Cursor = Cursors.SizeAll,
			ToolTip = "拖动面板位置"
		};
		Root.Children.Add(moveGrip);
		resizeGrip = new Thumb
		{
			Width = 20.0,
			Height = 20.0,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			Margin = new Thickness(0.0, 0.0, 5.0, 5.0),
			Cursor = Cursors.SizeNWSE,
			ToolTip = "拖动调整面板大小"
		};
		Root.Children.Add(resizeGrip);
		moveGrip.Template = GlassWindow.GripTemplate();
		resizeGrip.Template = GlassWindow.GripTemplate(corner: true);
		base.Closing += delegate(object s, CancelEventArgs e)
		{
			if (!C.ShuttingDown)
			{
				e.Cancel = true;
				CollapseSmooth();
			}
		};
		moveGrip.DragStarted += delegate
		{
			ModalBusy = true;
		};
		moveGrip.DragDelta += delegate(object s, DragDeltaEventArgs e)
		{
			if (!C.Prefs.Placement.PanelLocked)
			{
				Rect workArea2 = SystemParameters.WorkArea;
				base.Left = FeatureRules.Position(base.Left + e.HorizontalChange, workArea2.Left, workArea2.Width, base.Width);
				base.Top = FeatureRules.Position(base.Top + e.VerticalChange, workArea2.Top, workArea2.Height, base.Height);
			}
		};
		InitResizeRefresh();
		resizeGrip.DragStarted += delegate
		{
			ModalBusy = true;
			resizingPanel = true;
			sizeRefresh.Stop();
		};
		resizeGrip.DragDelta += delegate(object s, DragDeltaEventArgs e)
		{
			if (!C.Prefs.Placement.PanelLocked)
			{
				Rect workArea = SystemParameters.WorkArea;
				base.Width = Math.Max(Math.Min(860.0, workArea.Width - 32.0), Math.Min(workArea.Width - 32.0, base.Width + e.HorizontalChange));
				base.Height = Math.Max(Math.Min(560.0, workArea.Height - 32.0), Math.Min(workArea.Height - 32.0, base.Height + e.VerticalChange));
				base.Left = FeatureRules.Position(base.Left, workArea.Left, workArea.Width, base.Width);
				base.Top = FeatureRules.Position(base.Top, workArea.Top, workArea.Height, base.Height);
			}
		};
		moveGrip.DragCompleted += delegate
		{
			try
			{
				SavePlacement();
				Backdrop(C.Prefs.Light);
			}
			finally
			{
				ModalBusy = false;
			}
		};
		resizeGrip.DragCompleted += delegate
		{
			try
			{
				resizingPanel = false;
				SavePlacement();
				QueueSizeRefresh();
				Backdrop(C.Prefs.Light);
			}
			finally
			{
				ModalBusy = false;
			}
		};
	}

	private void SavePlacement()
	{
		PlacementOptions placement = C.Prefs.Placement;
		placement.PanelLeft = base.Left;
		placement.PanelTop = base.Top;
		placement.PanelWidth = base.Width;
		placement.PanelHeight = base.Height;
		C.Save();
	}

	private void BuildForSize()
	{
		string text = ((C.Prefs.BoardHideSearch || Search == null) ? "" : Search.Text);
		if (C.Prefs.BoardHideGroups)
		{
			currentGroup = "全部";
		}
		columns = BoardIconRules.Columns(base.Width - 104.0, C.Prefs.BoardIconSize, C.Prefs.BoardIconGap);
		int num = BoardIconRules.Rows(base.Height - (double)(C.Prefs.BoardHideGroups ? 180 : 258), C.Prefs.BoardIconSize, C.Prefs.BoardIconGap);
		capacity = columns * num;
		Build(num);
		Search.Text = text;
	}

	public void RefreshIcons()
	{
		if (boardIconSlider != null)
		{
			iconSizeSync = true;
			try
			{
				boardIconSlider.Value = C.Prefs.BoardIconSize;
				iconSizeText.Text = "图标 " + Math.Round(C.Prefs.BoardIconSize) + " px";
			}
			finally
			{
				iconSizeSync = false;
			}
		}
		iconsDirty = true;
		QueueSizeRefresh();
	}

	private void IconSettings()
	{
		ContextMenu menu = MenuTheme.Create(C.Prefs.Light);
		iconMenu = menu;
		menu.Closed += delegate
		{
			if (iconMenu == menu)
			{
				iconMenu = null;
			}
		};
		Action<string, double, double, double, Action<double>> obj = delegate(string name, double min, double max, double value, Action<double> change)
		{
			StackPanel stackPanel = new StackPanel
			{
				Width = 230.0
			};
			TextBlock label = new TextBlock
			{
				Text = name + "：" + Math.Round(value) + " px",
				Foreground = base.TextBrush,
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			};
			stackPanel.Children.Add(label);
			Slider slider = new Slider
			{
				Minimum = min,
				Maximum = max,
				Value = value,
				TickFrequency = 1.0,
				IsSnapToTickEnabled = true,
				Style = (base.Resources[typeof(Slider)] as Style)
			};
			stackPanel.Children.Add(slider);
			slider.ValueChanged += delegate
			{
				label.Text = name + "：" + Math.Round(slider.Value) + " px";
				change(slider.Value);
				RefreshIcons();
				C.Save();
			};
			menu.Items.Add(new MenuItem
			{
				Header = stackPanel,
				StaysOpenOnClick = true
			});
		};
		obj("图标大小", 28.0, 80.0, C.Prefs.BoardIconSize, delegate(double v)
		{
			C.Prefs.BoardIconSize = v;
		});
		obj("图标间距", 0.0, 20.0, C.Prefs.BoardIconGap, delegate(double v)
		{
			C.Prefs.BoardIconGap = v;
		});
		MenuItem menuItem = new MenuItem
		{
			Header = "恢复默认"
		};
		menuItem.Click += delegate
		{
			C.Prefs.BoardIconSize = 48.0;
			C.Prefs.BoardIconGap = 5.0;
			RefreshIcons();
			C.Save();
		};
		menu.Items.Add(menuItem);
		menu.PlacementTarget = this;
		menu.IsOpen = true;
	}

	public void CollapseSmooth()
	{
		EndDragPreview();
		if (!base.IsVisible)
		{
			return;
		}
		int serial = ++visibilitySerial;
		if (!MotionEnabled)
		{
			Hide();
			return;
		}
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(110.0));
		doubleAnimation.Completed += delegate
		{
			if (serial == visibilitySerial)
			{
				Hide();
			}
		};
		BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
	}

	internal bool ReleaseHiddenView()
	{
		if (base.IsVisible || preparingView || ModalBusy || activeDrag || base.IsMouseCaptureWithin)
		{
			return false;
		}
		sizeRefresh.Stop();
		if (iconMenu != null)
		{
			iconMenu.IsOpen = false;
		}
		iconMenu = null;
		EndDragPreview();
		ContentGrid.Children.Clear();
		ReleaseBackdrop();
		Search = null;
		Tiles = null;
		Pager = (Status = (Empty = null));
		layout = null;
		bulkBar = (folderBar = null);
		bulkRow = null;
		selectionCount = (iconSizeText = null);
		selectAll = (clearSelection = (deleteSelected = (undoRemoval = (editSelection = (moveSelected = (addExisting = null))))));
		groups = (moveGroup = null);
		boardIconSlider = null;
		dragEntry = null;
		return true;
	}

	private bool IsContextMenuOpen()
	{
		if (iconMenu == null || !iconMenu.IsOpen)
		{
			if (Tiles != null)
			{
				return Tiles.Children.OfType<FrameworkElement>().Any((FrameworkElement x) => x.ContextMenu != null && x.ContextMenu.IsOpen);
			}
			return false;
		}
		return true;
	}

	public void Open()
	{
		idleRelease.Stop();
		EndDragPreview();
		preparingView = true;
		try
		{
			visibilitySerial++;
			Rect workArea = SystemParameters.WorkArea;
			double num = ((C.Prefs.PanelSize == 0) ? 980 : 1280);
			double num2 = ((C.Prefs.PanelSize == 0) ? 620 : 730);
			if (C.Prefs.PanelSize == 2)
			{
				num = workArea.Width - 32.0;
				num2 = workArea.Height - C.Dock.Height - 40.0;
			}
			PlacementOptions placement = C.Prefs.Placement;
			base.Width = Math.Min(placement.PanelWidth ?? num, workArea.Width - 32.0);
			base.Height = Math.Min(placement.PanelHeight ?? num2, workArea.Height - 32.0);
			base.Height = Math.Max(Math.Min(400.0, workArea.Height - 130.0), base.Height);
			base.Width = Math.Max(Math.Min(860.0, workArea.Width - 32.0), base.Width);
			base.Height = Math.Max(Math.Min(560.0, workArea.Height - 32.0), base.Height);
			base.Left = FeatureRules.Position(placement.PanelLeft ?? (workArea.Left + (workArea.Width - base.Width) / 2.0), workArea.Left, workArea.Width, base.Width);
			base.Top = FeatureRules.Position(placement.PanelTop ?? (workArea.Top + (workArea.Height - C.Dock.Height - base.Height) / 2.0), workArea.Top, workArea.Height, base.Height);
			Thumb thumb = moveGrip;
			Visibility visibility2 = (resizeGrip.Visibility = (placement.PanelLocked ? Visibility.Collapsed : Visibility.Visible));
			thumb.Visibility = visibility2;
			editing = false;
			selected.Clear();
			LightTheme = C.Prefs.Light;
			MotionEnabled = C.Prefs.Dock.MotionEnabled;
			MotionLevel = C.Prefs.Dock.MotionLevel;
			ConfigureGlass(C.Prefs.Dock, C.Prefs.BoardMaterial);
			GlassPercent = C.Prefs.OpacityPercent ?? 72.0;
			Polish.Apply(this, LightTheme);
			BuildForSize();
			Backdrop(C.Prefs.Light);
			Show();
			Activate();
			Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
			AnimateOpen();
			if (!C.Prefs.BoardHideSearch)
			{
				Search.Focus();
			}
			else
			{
				Focus();
			}
		}
		finally
		{
			preparingView = false;
		}
	}

	private void Build(int rows)
	{
		ContentGrid.Children.Clear();
		layout = new Grid
		{
			Margin = new Thickness(38.0, 22.0, 38.0, 20.0)
		};
		layout.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(55.0)
		});
		layout.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		bulkRow = new RowDefinition
		{
			Height = new GridLength(0.0)
		};
		layout.RowDefinitions.Add(bulkRow);
		layout.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(1.0, GridUnitType.Star)
		});
		layout.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(45.0)
		});
		layout.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(32.0)
		});
		ContentGrid.Children.Add(layout);
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(116.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(80.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(40.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(40.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(44.0)
		});
		layout.Children.Add(grid);
		Border border = new Border
		{
			CornerRadius = new CornerRadius(18.0),
			BorderThickness = new Thickness(1.0),
			BorderBrush = new SolidColorBrush(Color.FromArgb(90, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			Background = new SolidColorBrush(Color.FromArgb(25, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			Margin = new Thickness(10.0, 0.0, 18.0, 9.0)
		};
		Grid grid2 = (Grid)(border.Child = new Grid());
		TextBlock hint = new TextBlock
		{
			Text = "搜索应用、文件夹…",
			Foreground = base.MutedBrush,
			Margin = new Thickness(18.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			IsHitTestVisible = false
		};
		grid2.Children.Add(hint);
		Search = new TextBox
		{
			FontSize = 15.0,
			Foreground = base.TextBrush,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			CaretBrush = base.TextBrush,
			VerticalContentAlignment = VerticalAlignment.Center,
			Padding = new Thickness(18.0, 0.0, 18.0, 0.0)
		};
		Search.TextChanged += delegate
		{
			hint.Visibility = ((Search.Text.Length != 0) ? Visibility.Hidden : Visibility.Visible);
			page = 0;
			Render(animate: true);
		};
		grid2.Children.Add(Search);
		grid.Children.Add(border);
		border.Visibility = (C.Prefs.BoardHideSearch ? Visibility.Collapsed : Visibility.Visible);
		groupHeading = null;
		if (C.Prefs.BoardHideSearch)
		{
			groupHeading = new TextBlock
			{
				Text = (C.Prefs.BoardHideGroups ? "全部应用" : C.Prefs.GroupLabel(currentGroup)),
				Foreground = base.TextBrush,
				FontSize = 18.0,
				Margin = new Thickness(12.0, 0.0, 0.0, 8.0),
				VerticalAlignment = VerticalAlignment.Center,
				IsHitTestVisible = false
			};
			grid.Children.Add(groupHeading);
		}
		Button element = Button("+ 批量添加", delegate
		{
			C.Choose();
		}, 106.0);
		Grid.SetColumn(element, 1);
		grid.Children.Add(element);
		editSelection = Button(editing ? "完成" : "多选", null, 68.0);
		editSelection.ToolTip = "空白处按住左键框选；长按图标进入多选；可批量移动或移除入口";
		editSelection.Click += delegate
		{
			SetSelectionMode(!editing);
			Search.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
			Render();
		};
		Grid.SetColumn(editSelection, 2);
		grid.Children.Add(editSelection);
		Button button = Button("⚙", delegate
		{
			C.Settings();
		}, 30.0);
		button.Content = Polish.Icon("settings", base.TextBrush, 18.0);
		button.ToolTip = "外观、组件与快捷键";
		Grid.SetColumn(button, 3);
		grid.Children.Add(button);
		Button button2 = Button("···", delegate
		{
			MoreMenu();
		}, 32.0);
		button2.ToolTip = "更多与显示选项";
		Grid.SetColumn(button2, 4);
		grid.Children.Add(button2);
		Button element2 = Button("×", delegate
		{
			CollapseSmooth();
		}, 32.0);
		Grid.SetColumn(element2, 5);
		grid.Children.Add(element2);
		WrapPanel wrapPanel = new WrapPanel
		{
			VerticalAlignment = VerticalAlignment.Center,
			Visibility = (C.Prefs.BoardHideGroups ? Visibility.Collapsed : Visibility.Visible)
		};
		Grid.SetRow(wrapPanel, 1);
		layout.Children.Add(wrapPanel);
		groups = new ComboBox
		{
			Width = 145.0,
			Height = 30.0,
			Margin = new Thickness(8.0, 0.0, 10.0, 0.0)
		};
		wrapPanel.Children.Add(groups);
		groups.SelectionChanged += delegate
		{
			if (!syncingGroups)
			{
				string text = (groups.SelectedItem as string) ?? "全部";
				if (text != currentGroup)
				{
					selected.Clear();
				}
				currentGroup = text;
				if (addExisting != null)
				{
					addExisting.IsEnabled = currentGroup != "全部";
				}
				page = 0;
				Render(animate: true);
			}
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(6.0, 0.0, 8.0, 0.0)
		};
		iconSizeText = new TextBlock
		{
			Text = "图标 " + Math.Round(C.Prefs.BoardIconSize) + " px",
			Foreground = base.TextBrush,
			VerticalAlignment = VerticalAlignment.Center,
			Width = 75.0
		};
		stackPanel.Children.Add(iconSizeText);
		boardIconSlider = new Slider
		{
			Minimum = 28.0,
			Maximum = 80.0,
			Value = C.Prefs.BoardIconSize,
			Width = 110.0,
			TickFrequency = 2.0,
			IsSnapToTickEnabled = true,
			ToolTip = "缩小图标，让每页容纳更多入口"
		};
		boardIconSlider.ValueChanged += delegate
		{
			if (!iconSizeSync)
			{
				C.Prefs.BoardIconSize = boardIconSlider.Value;
				iconSizeText.Text = "图标 " + Math.Round(boardIconSlider.Value) + " px";
				RefreshIcons();
				C.Save();
			}
		};
		stackPanel.Children.Add(boardIconSlider);
		wrapPanel.Children.Add(stackPanel);
		addExisting = Button("添加已有图标", delegate
		{
			C.AddExistingToGroup(currentGroup);
		}, 112.0);
		wrapPanel.Children.Add(addExisting);
		wrapPanel.Children.Add(Button("添加文件夹", delegate
		{
			C.AddFolder();
		}, 100.0));
		wrapPanel.Children.Add(Button("新建", delegate
		{
			NewFileMenu();
		}, 60.0));
		var autoArrange = Button("自动整理", delegate { C.AutoArrangeEntries(currentGroup); }, 96.0);
		autoArrange.ToolTip = "按类型和名称整理当前分组；Ctrl+Z 撤销";
		wrapPanel.Children.Add(autoArrange);
		wrapPanel.Children.Add(Button("更多", delegate
		{
			MoreMenu();
		}, 68.0));
		bulkBar = new WrapPanel
		{
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		Grid.SetRow(bulkBar, 2);
		layout.Children.Add(bulkBar);
		selectAll = Button("全选结果", delegate
		{
			SelectFiltered();
		}, 90.0);
		selectAll.ToolTip = "选中当前搜索结果的所有页；已有勾选会保留";
		bulkBar.Children.Add(selectAll);
		clearSelection = Button("取消全选", delegate
		{
			selected.Clear();
			Render();
		}, 90.0);
		bulkBar.Children.Add(clearSelection);
		deleteSelected = Button("移除已选", delegate
		{
			C.RemoveMany(selected.ToArray());
		}, 112.0);
		deleteSelected.Foreground = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 155, 155));
		bulkBar.Children.Add(deleteSelected);
		undoRemoval = Button("撤销", delegate
		{
			C.UndoBoard();
		}, 72.0);
		wrapPanel.Children.Add(undoRemoval);
		selectionCount = new TextBlock
		{
			Foreground = base.MutedBrush,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0)
		};
		bulkBar.Children.Add(selectionCount);
		moveGroup = new ComboBox
		{
			Width = 112.0,
			Height = 30.0,
			Margin = new Thickness(12.0, 0.0, 4.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		bulkBar.Children.Add(moveGroup);
		moveSelected = Button("移动", delegate
		{
			if (moveGroup.SelectedItem is string to && selected.Count != 0)
			{
				C.TransferEntries(selected.ToArray(), currentGroup, to);
			}
		}, 100.0);
		bulkBar.Children.Add(moveSelected);
		SyncGroups();
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(0.0, 10.0, 0.0, 4.0)
		};
		Grid.SetRow(dockPanel, 3);
		layout.Children.Add(dockPanel);
		folderSignature = "";
		folderBar = new WrapPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
			Visibility = ((!foldersVisible) ? Visibility.Collapsed : Visibility.Visible)
		};
		DockPanel.SetDock(folderBar, Dock.Top);
		dockPanel.Children.Add(folderBar);
		Grid grid3 = new Grid { Background = Brushes.Transparent };
		dockPanel.Children.Add(grid3);
		Tiles = new UniformGrid
		{
			Columns = columns,
			Rows = rows,
			ClipToBounds = true
		};
		grid3.Children.Add(Tiles);
		Tiles.SizeChanged += delegate
		{
			QueueSizeRefresh();
		};
		Empty = new TextBlock
		{
			Text = "将软件、文档或文件夹拖到这里\n面板默认保持打开；按 Esc 收起，也可点击「批量添加」",
			TextAlignment = TextAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Center,
			Foreground = base.MutedBrush,
			FontSize = 19.0,
			LineHeight = 32.0
		};
		Empty.IsHitTestVisible = false;
		grid3.Children.Add(Empty);
		InitMarquee(grid3);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		Grid.SetRow(stackPanel2, 4);
		layout.Children.Add(stackPanel2);
		stackPanel2.Children.Add(Button("‹", delegate
		{
			ChangePage(-1);
		}, 40.0));
		Pager = new TextBlock
		{
			Width = 100.0,
			TextAlignment = TextAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = base.TextBrush,
			FontSize = 14.0
		};
		stackPanel2.Children.Add(Pager);
		stackPanel2.Children.Add(Button("›", delegate
		{
			ChangePage(1);
		}, 40.0));
		Grid grid4 = new Grid();
		grid4.ColumnDefinitions.Add(new ColumnDefinition());
		grid4.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(230.0)
		});
		Grid.SetRow(grid4, 5);
		layout.Children.Add(grid4);
		Status = new TextBlock
		{
			Text = "图标可拖到 Dock 固定 · Esc 收起 · ··· 显示选项",
			Foreground = base.MutedBrush,
			FontSize = 11.0,
			TextTrimming = TextTrimming.CharacterEllipsis,
			VerticalAlignment = VerticalAlignment.Center
		};
		StackPanel stackPanel3 = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel3.Children.Add(Status);
		grid4.Children.Add(stackPanel3);
		StackPanel stackPanel4 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		Grid.SetColumn(stackPanel4, 1);
		grid4.Children.Add(stackPanel4);
		TextBlock amount = new TextBlock
		{
			Text = "透明度 " + Math.Round(100.0 - GlassPercent) + "%",
			Foreground = base.MutedBrush,
			Width = 83.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel4.Children.Add(amount);
		Slider transparency = new Slider
		{
			Minimum = 0.0,
			Maximum = 100.0,
			Value = 100.0 - GlassPercent,
			Width = 140.0,
			TickFrequency = 5.0,
			IsSnapToTickEnabled = true,
			ToolTip = "只改变玻璃背景，图标和文字保持清晰"
		};
		stackPanel4.Children.Add(transparency);
		transparency.ValueChanged += delegate
		{
			C.Opacity(100.0 - transparency.Value);
			amount.Text = "透明度 " + Math.Round(transparency.Value) + "%";
			C.Save();
		};
		Render(animate: true);
	}

	public void SyncGroups()
	{
		if (addExisting != null)
		{
			addExisting.IsEnabled = currentGroup != "全部";
		}
		if (groups != null)
		{
			syncingGroups = true;
			List<string> list = C.Prefs.BoardGroups().ToList();
			groups.ItemTemplate = GroupDisplay.Template(C.Prefs);
			groups.ItemsSource = list;
			if (!list.Contains(currentGroup))
			{
				currentGroup = "全部";
				selected.Clear();
				page = 0;
			}
			groups.SelectedItem = currentGroup;
			if (moveGroup != null)
			{
				string text = moveGroup.SelectedItem as string;
				moveGroup.ItemTemplate = GroupDisplay.Template(C.Prefs);
				moveGroup.ItemsSource = list.Skip(1).ToList();
				moveGroup.SelectedItem = (list.Contains(text) ? text : "未分组");
			}
			syncingGroups = false;
			if (addExisting != null)
			{
				addExisting.IsEnabled = currentGroup != "全部";
			}
		}
	}

	private List<Entry> Filtered()
	{
		string q = ((C.Prefs.BoardHideSearch || Search == null) ? "" : Search.Text.Trim());
		return C.Prefs.Apps.Where((Entry a) => (C.Prefs.BoardHideGroups || currentGroup == "全部" || FeatureRules.InGroup(a, currentGroup)) && LayoutRules.Matches(a.Name, q)).ToList();
	}

	public void SetStatus(string text)
	{
		pendingStatus = text;
		if (Status != null)
		{
			Status.Text = text;
		}
	}

	private void SetSelectionMode(bool enabled)
	{
		editing = enabled;
		selected.Clear();
		dragEntry = null;
		if (editSelection != null)
		{
			editSelection.Content = (enabled ? "完成" : "多选");
		}
	}

	public void FinishSelection()
	{
		SetSelectionMode(enabled: false);
	}

	private void SelectFiltered()
	{
		foreach (Entry item in Filtered())
		{
			selected.Add(item.Path);
		}
		Render();
	}

	private void UpdateSelectionBar()
	{
		selected.RemoveWhere((string p) => !C.Prefs.Apps.Any((Entry a) => string.Equals(a.Path, p, StringComparison.OrdinalIgnoreCase)));
		bool flag = editing;
		bulkBar.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		bulkRow.Height = (flag ? GridLength.Auto : new GridLength(0.0));
		moveGroup.IsEnabled = editing;
		moveSelected.Content = ((currentGroup == "全部") ? "添加到分组" : "移动");
		moveSelected.IsEnabled = editing && selected.Count > 0;
		selectAll.IsEnabled = editing;
		clearSelection.IsEnabled = editing && selected.Count > 0;
		deleteSelected.IsEnabled = editing && selected.Count > 0;
		deleteSelected.Content = "移除已选（" + selected.Count + "）";
		undoRemoval.IsEnabled = C.CanUndoRemoval;
		undoRemoval.ToolTip = "撤销：" + C.UndoLabel + " · Ctrl+Z";
		undoRemoval.Visibility = ((!C.CanUndoRemoval) ? Visibility.Collapsed : Visibility.Visible);
		selectionCount.Text = (editing ? ("已选 " + selected.Count + " · 跨页保留") : "可撤销最近操作");
	}

	public void Render(bool animate = false)
	{
		if (groupHeading != null) groupHeading.Text = C.Prefs.BoardHideGroups ? "全部应用" : C.Prefs.GroupLabel(currentGroup);
		if (Tiles == null || (!base.IsVisible && !preparingView))
		{
			return;
		}
		UpdateSelectionBar();
		RefreshFolders();
		Tiles.Children.Clear();
		List<Entry> list = Filtered();
		int num = LayoutRules.Pages(list.Count, capacity);
		page = Math.Min(page, num - 1);
		Pager.Text = page + 1 + " / " + num;
		Empty.Visibility = ((list.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		if (pendingStatus != null)
		{
			Status.Text = pendingStatus;
		}
		int num2 = 0;
		foreach (Entry item in list.Skip(page * capacity).Take(capacity))
		{
			Tiles.Children.Add(Tile(item, num2++, animate));
		}
	}

	private void BoardMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (Tiles == null || C.UiBusy || activeDrag || IsContextMenuOpen() || Mouse.LeftButton == MouseButtonState.Pressed) return;
		DependencyObject node = e.OriginalSource as DependencyObject;
		while (node != null && node != this) {
			if (node is TextBoxBase || node is ComboBox || node is Slider || node is ScrollViewer || node is ContextMenu || node is MenuItem) { wheelRemainder = 0; return; }
			node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
		}
		if ((wheelRemainder > 0 && e.Delta < 0) || (wheelRemainder < 0 && e.Delta > 0)) wheelRemainder = 0;
		wheelRemainder += e.Delta;
		int steps = wheelRemainder / 120;
		wheelRemainder %= 120;
		if (steps != 0) ChangePage(-steps);
		e.Handled = true;
	}

	private void ChangePage(int delta)
	{
		int num = LayoutRules.Pages(Filtered().Count, capacity);
		int next = Math.Max(0, Math.Min(num - 1, page + delta));
		if (next == page) return;
		page = next;
		Render(animate: true);
	}

	private FrameworkElement Tile(Entry a, int index, bool animate)
	{
		bool isSelected = editing && selected.Contains(a.Path);
		SolidColorBrush selectedBrush = new SolidColorBrush(Color.FromArgb(48, 111, 167, byte.MaxValue));
		Border tile = new Border
		{
			Tag = a,
			Background = (isSelected ? selectedBrush : Brushes.Transparent),
			BorderThickness = new Thickness(1.0),
			BorderBrush = (isSelected ? new SolidColorBrush(Color.FromArgb(150, 146, 187, byte.MaxValue)) : Brushes.Transparent),
			CornerRadius = new CornerRadius(18.0),
			Margin = new Thickness(C.Prefs.BoardIconGap),
			Padding = new Thickness(4.0),
			Cursor = Cursors.Hand,
			ToolTip = a.Name,
			ContextMenu = (editing ? null : C.Menu(a, currentGroup)),
			AllowDrop = true
		};
		if (!editing) C.AttachSystemFileMenu(tile, a, currentGroup);
		Grid grid = new Grid();
		tile.Child = grid;
		StackPanel stackPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.Children.Add(stackPanel);
		ImageSource imageSource = FeatureUi.Icon(a, C.Prefs.Dock.ShowShortcutOverlays, C.Prefs.Dock.SharperIcons);
		if (imageSource != null)
		{
			stackPanel.Children.Add(new Image
			{
				Source = imageSource,
				Width = C.Prefs.BoardIconSize,
				Height = C.Prefs.BoardIconSize,
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			});
		}
		else
		{
			stackPanel.Children.Add(new TextBlock
			{
				Text = "▣",
				Foreground = base.TextBrush,
				FontSize = C.Prefs.BoardIconSize * 0.84,
				Height = C.Prefs.BoardIconSize,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			});
		}
		stackPanel.Children.Add(new TextBlock
		{
			Text = a.Name,
			Foreground = base.TextBrush,
			FontSize = 12.0,
			TextAlignment = TextAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis,
			MaxWidth = Math.Max(20.0, ((Tiles.ActualWidth > 0.0) ? Tiles.ActualWidth : (base.Width - 104.0)) / (double)columns - 2.0 * C.Prefs.BoardIconGap - 10.0)
		});
		grid.Children.Add(new Border
		{
			Tag = "running",
			Width = 5.0,
			Height = 5.0,
			CornerRadius = new CornerRadius(3.0),
			Background = new SolidColorBrush(Color.FromRgb(135, 205, byte.MaxValue)),
			VerticalAlignment = VerticalAlignment.Bottom,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 0.0, 1.0),
			ToolTip = "正在运行，点击切回窗口",
			Visibility = ((!C.Prefs.Dock.ShowRunning || !RunningWindows.IsRunning(a)) ? Visibility.Collapsed : Visibility.Visible)
		});
		if (editing)
		{
			CheckBox element = new CheckBox
			{
				IsChecked = isSelected,
				IsHitTestVisible = false,
				Focusable = false,
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(2.0)
			};
			grid.Children.Add(element);
		}
		ScaleTransform scale = new ScaleTransform(1.0, 1.0);
		TranslateTransform lift = new TranslateTransform();
		TransformGroup transformGroup = new TransformGroup();
		transformGroup.Children.Add(scale);
		transformGroup.Children.Add(lift);
		tile.RenderTransform = transformGroup;
		tile.RenderTransformOrigin = new Point(0.5, 0.5);
		Action<bool> hover = delegate(bool over)
		{
			tile.Background = (isSelected ? selectedBrush : (over ? new SolidColorBrush(Color.FromArgb(24, byte.MaxValue, byte.MaxValue, byte.MaxValue)) : Brushes.Transparent));
			if (MotionEnabled)
			{
				CubicEase easingFunction2 = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				};
				scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(over ? (1.0 + 0.025 * MotionLevel) : 1.0, TimeSpan.FromMilliseconds(170.0))
				{
					EasingFunction = easingFunction2
				});
				scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(over ? (1.0 + 0.025 * MotionLevel) : 1.0, TimeSpan.FromMilliseconds(170.0))
				{
					EasingFunction = easingFunction2
				});
				lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(over ? (-3.0 * MotionLevel) : 0.0, TimeSpan.FromMilliseconds(170.0))
				{
					EasingFunction = easingFunction2
				});
			}
		};
		tile.MouseEnter += delegate
		{
			hover(obj: true);
		};
		tile.MouseLeave += delegate
		{
			hover(obj: false);
		};
		if (animate && MotionEnabled)
		{
			CubicEase easingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			};
			TimeSpan value = TimeSpan.FromMilliseconds(Math.Min(index, 14) * 12);
			tile.Opacity = 0.0;
			tile.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(190.0))
			{
				BeginTime = value,
				EasingFunction = easingFunction
			});
			lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(9.0 * MotionLevel, 0.0, TimeSpan.FromMilliseconds(230.0))
			{
				BeginTime = value,
				EasingFunction = easingFunction
			});
		}
		var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
		hold.Tick += delegate {
			hold.Stop();
			if (Mouse.LeftButton != MouseButtonState.Pressed || dragEntry != a || entryDragRunning) return;
			if (!editing) SetSelectionMode(true);
			selected.Add(a.Path);
			dragEntry = null;
			Render();
		};
		tile.Unloaded += delegate { hold.Stop(); };
		tile.MouseLeave += delegate { hold.Stop(); };
		tile.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
		{
			dragStart = e.GetPosition(this);
			dragEntry = a;
			hold.Start();
		};
		tile.MouseMove += delegate(object s, MouseEventArgs e)
		{
			if (e.LeftButton == MouseButtonState.Pressed && dragEntry == a)
			{
				Point position = e.GetPosition(this);
				if (Math.Abs(position.X - dragStart.X) + Math.Abs(position.Y - dragStart.Y) > 10.0)
				{
					hold.Stop();
					dragEntry = null;
					StartEntryDrag(tile, a);
				}
			}
		};
		tile.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
		{
			hold.Stop();
			if (dragEntry == a)
			{
				dragEntry = null;
				if (editing)
				{
					if (!selected.Remove(a.Path))
					{
						selected.Add(a.Path);
					}
					Render();
					e.Handled = true;
				}
				else
				{
					C.Launch(a);
				}
			}
		};
		tile.DragOver += delegate(object sender, DragEventArgs e) {
			var paths = DraggedEntries(e.Data);
			if (paths.Length == 0) return;
			bool allowed = !paths.Contains(a.Path, StringComparer.OrdinalIgnoreCase);
			e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
			tile.BorderBrush = new SolidColorBrush(Color.FromRgb(125, 175, 255));
			tile.BorderThickness = allowed ? (e.GetPosition(tile).X < tile.ActualWidth / 2 ? new Thickness(3,0,0,0) : new Thickness(0,0,3,0)) : new Thickness(1);
			e.Handled = true;
		};
		tile.DragLeave += delegate { tile.BorderThickness = new Thickness(1); tile.BorderBrush = Brushes.Transparent; };
		tile.Drop += delegate(object sender, DragEventArgs e) {
			var paths = DraggedEntries(e.Data);
			if (paths.Length == 0) return;
			C.ReorderEntries(paths, a, e.GetPosition(tile).X < tile.ActualWidth / 2);
			e.Effects = DragDropEffects.Move;
			e.Handled = true;
		};
		return tile;
	}

	private void InitMarquee(Grid surface)
	{
		var canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
		var box = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(125,175,255)), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromArgb(40,125,175,255)), Visibility = Visibility.Collapsed };
		canvas.Children.Add(box); surface.Children.Add(canvas);
		bool pending = false, selecting = false;
		Point origin = new Point();
		var baseline = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		surface.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
			DependencyObject node = e.OriginalSource as DependencyObject;
			while (node != null && node != surface) {
				if (node is FrameworkElement element && element.Tag is Entry) return;
				node = VisualTreeHelper.GetParent(node);
			}
			if (C.UiBusy) return;
			origin = e.GetPosition(this); pending = true; selecting = false;
			baseline.Clear();
			if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) baseline.UnionWith(selected);
			surface.CaptureMouse(); e.Handled = true;
		};
		surface.MouseMove += delegate(object sender, MouseEventArgs e) {
			if (!pending || e.LeftButton != MouseButtonState.Pressed) return;
			Point now = e.GetPosition(this);
			if (!selecting && Math.Abs(now.X-origin.X)+Math.Abs(now.Y-origin.Y) < 8) return;
			if (!selecting) {
				selecting = true;
				if (!editing) SetSelectionMode(true);
				editSelection.Content = "完成";
				UpdateSelectionBar(); surface.UpdateLayout();
			}
			Point start = TranslatePoint(origin, surface), end = e.GetPosition(surface);
			var rect = new Rect(start, end);
			Canvas.SetLeft(box, rect.Left); Canvas.SetTop(box, rect.Top);
			box.Width = rect.Width; box.Height = rect.Height; box.Visibility = Visibility.Visible;
			ApplyMarquee(surface, rect, baseline);
			e.Handled = true;
		};
		surface.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) {
			if (!pending) return;
			if (!selecting) selected.Clear();
			bool render = selecting || editing; pending = selecting = false; box.Visibility = Visibility.Collapsed;
			surface.ReleaseMouseCapture();
			if (render) Render();
			e.Handled = true;
		};
		surface.LostMouseCapture += delegate {
			bool render = selecting; pending = selecting = false; box.Visibility = Visibility.Collapsed;
			if (render) Render();
		};
	}

	private void ApplyMarquee(FrameworkElement surface, Rect rect, HashSet<string> baseline)
	{
		selected.Clear(); selected.UnionWith(baseline);
		foreach (Border tile in Tiles.Children.OfType<Border>()) {
			if (!(tile.Tag is Entry entry)) continue;
			var bounds = new Rect(tile.TranslatePoint(new Point(), surface), tile.RenderSize);
			if (rect.IntersectsWith(bounds)) selected.Add(entry.Path);
			bool chosen = selected.Contains(entry.Path);
			tile.Background = chosen ? new SolidColorBrush(Color.FromArgb(48,111,167,255)) : Brushes.Transparent;
			tile.BorderBrush = chosen ? new SolidColorBrush(Color.FromArgb(150,146,187,255)) : Brushes.Transparent;
		}
		UpdateSelectionBar();
	}

	private string[] DraggedEntries(IDataObject data)
	{
		if (data.GetDataPresent("GlassBoard.Entries"))
		{
			return (string[])data.GetData("GlassBoard.Entries");
		}
		if (data.GetDataPresent("GlassBoard.Entry"))
		{
			return new string[1] { (string)data.GetData("GlassBoard.Entry") };
		}
		return new string[0];
	}

	public void SelectRenamedGroup(string name) { OpenGroup(name); }

	private void OpenGroup(string name)
	{
		FinishSelection();
		currentGroup = name;
		page = 0;
		ClearSearch();
		SyncGroups();
		Render(animate: true);
	}

	private void RefreshFolders()
	{
		if (folderBar == null)
		{
			return;
		}
		if (activeDrag && folderBar.Children.Count > 0)
		{
			if ((from b in folderBar.Children.OfType<Border>()
				select b.Tag as string).SequenceEqual(C.Prefs.BoardGroups()))
			{
				return;
			}
			EndDragPreview();
		}
		string text = C.Prefs.GroupLabel("未分组") + "/" + currentGroup + "/" + LightTheme + "/" + string.Join("|", from g in C.Prefs.BoardGroups()
			select g + ":" + C.Prefs.Apps.Count((Entry a) => FeatureRules.InGroup(a, g)));
		if (folderSignature == text && folderBar.Children.Count > 0)
		{
			return;
		}
		folderSignature = text;
		folderBar.Children.Clear();
		foreach (string item in C.Prefs.BoardGroups())
		{
			string name = item;
			int num = ((name == "全部") ? C.Prefs.Apps.Count : C.Prefs.Apps.Count((Entry a) => FeatureRules.InGroup(a, name)));
			SolidColorBrush normal = ((name == currentGroup) ? new SolidColorBrush(Color.FromArgb(55, 130, 180, byte.MaxValue)) : new SolidColorBrush(Color.FromArgb(22, byte.MaxValue, byte.MaxValue, byte.MaxValue)));
			Border box = new Border
			{
				Tag = name,
				CornerRadius = new CornerRadius(12.0),
				Background = normal,
				Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
				Margin = new Thickness(0.0, 0.0, 6.0, 6.0),
				Cursor = Cursors.Hand,
				AllowDrop = true,
				ToolTip = ((name == "全部") ? "查看全部入口" : "点击打开；将一个或多个入口拖到这里分组")
			};
			StackPanel stackPanel = new StackPanel
			{
				Orientation = Orientation.Horizontal
			};
			FrameworkElement frameworkElement = Polish.Icon((name == "全部") ? "grid" : "folder", base.TextBrush, 18.0);
			frameworkElement.Margin = new Thickness(0.0, 0.0, 6.0, 0.0);
			stackPanel.Children.Add(frameworkElement);
			stackPanel.Children.Add(new TextBlock
			{
				Text = C.Prefs.GroupLabel(name) + "  " + num,
				Foreground = base.TextBrush,
				FontSize = 12.0,
				VerticalAlignment = VerticalAlignment.Center
			});
			box.Child = stackPanel;
			if (C.Prefs.EditableGroup(name))
			{
				ContextMenu groupMenu = MenuTheme.Create(C.Prefs.Light);
				MenuItem rename = new MenuItem { Header = "重命名分组" };
				rename.Click += delegate { C.RenameGroupFromMenu(name); };
				groupMenu.Items.Add(rename);
				MenuItem delete = new MenuItem { Header = "删除分组", ToolTip = "仅移除分组，保留原文件；Ctrl+Z 撤销" };
				delete.Click += delegate { C.DeleteGroupFromMenu(name); };
				groupMenu.Items.Add(delete);
				box.ContextMenu = groupMenu;
				box.ToolTip = "右键重命名；拖动标签排序；拖入图标分组";
			}
			Point groupPress = new Point();
			bool groupWasDragged = false;
			box.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
			{
				groupPress = e.GetPosition(box);
				groupWasDragged = false;
			};
			box.MouseMove += delegate(object sender, MouseEventArgs e)
			{
				if (e.LeftButton != MouseButtonState.Pressed || groupWasDragged || !C.Prefs.EditableGroup(name) || C.UiBusy) return;
				Point position = e.GetPosition(box);
				if (Math.Abs(position.X-groupPress.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y-groupPress.Y) < SystemParameters.MinimumVerticalDragDistance) return;
				groupWasDragged = true;
				try { DragDrop.DoDragDrop(box, new DataObject("GlassBoard.GroupReorder", name), DragDropEffects.Move); }
				finally { box.Background = normal; }
				e.Handled = true;
			};
			box.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
			{
				if (!groupWasDragged) OpenGroup(name);
				e.Handled = true;
			};
			box.DragEnter += delegate(object sender, DragEventArgs e)
			{
				if (GroupReorderFeedback(box, name, e)) return;
				if (DraggedEntries(e.Data).Length != 0 || e.Data.GetDataPresent(DataFormats.FileDrop))
				{
					BeginGroupHover(name);
					box.Background = new SolidColorBrush(Color.FromArgb(90, 100, 160, 240));
					e.Effects = GroupDragEffect(e.Data, name);
					e.Handled = true;
				}
			};
			box.DragOver += delegate(object sender, DragEventArgs e)
			{
				if (GroupReorderFeedback(box, name, e)) return;
				if (DraggedEntries(e.Data).Length != 0 || e.Data.GetDataPresent(DataFormats.FileDrop))
				{
					BeginGroupHover(name);
				}
				e.Effects = GroupDragEffect(e.Data, name);
				e.Handled = true;
			};
			box.DragLeave += delegate
			{
				StopGroupHover(name);
				box.Background = normal;
				box.BorderThickness = new Thickness(0);
			};
			box.Drop += delegate(object sender, DragEventArgs e)
			{
				e.Handled = true;
				if (e.Data.GetDataPresent("GlassBoard.GroupReorder"))
				{
					box.BorderThickness = new Thickness(0);
					box.Background = normal;
					C.ReorderGroup(e.Data.GetData("GlassBoard.GroupReorder") as string, name, e.GetPosition(box).X < box.ActualWidth / 2);
					return;
				}
				if (!(name == "全部"))
				{
					string[] array = DraggedEntries(e.Data);
					if (array.Length != 0)
					{
						string from = (e.Data.GetDataPresent("GlassBoard.SourceGroup") ? ((string)e.Data.GetData("GlassBoard.SourceGroup")) : currentGroup);
						C.TransferEntries(array, from, name);
					}
					else if (e.Data.GetDataPresent(DataFormats.FileDrop))
					{
						C.Add((string[])e.Data.GetData(DataFormats.FileDrop), manual: true, name);
						OpenGroup(name);
						SetStatus("已添加到「" + name + "」");
					}
				}
			};
			folderBar.Children.Add(box);
		}
	}

	private bool GroupReorderFeedback(Border box, string name, DragEventArgs e)
	{
		if (!e.Data.GetDataPresent("GlassBoard.GroupReorder")) return false;
		e.Handled = true;
		string source = e.Data.GetData("GlassBoard.GroupReorder") as string;
		bool allowed = source != name && C.Prefs.EditableGroup(source) && C.Prefs.EditableGroup(name);
		e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
		if (allowed)
		{
			box.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 160, 240));
			box.BorderThickness = e.GetPosition(box).X < box.ActualWidth / 2 ? new Thickness(3, 0, 0, 0) : new Thickness(0, 0, 3, 0);
		}
		return true;
	}

	private void ExternalDrop(object s, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			C.Add((string[])e.Data.GetData(DataFormats.FileDrop), manual: true, currentGroup);
			ClearSearch();
			e.Handled = true;
		}
	}

	public void UpdateRunning()
	{
		if (Tiles == null || !base.IsVisible)
		{
			return;
		}
		foreach (Border item in Tiles.Children.OfType<Border>())
		{
			Entry entry = item.Tag as Entry;
			Grid grid = item.Child as Grid;
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
}
