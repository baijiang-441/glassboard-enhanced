using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GlassBoard;

public sealed class ControlOrbWindow : GlassWindow
{
	private readonly Controller C;

	private ControlCenterWindow panel;

	private bool moved;

	private bool panelWasOpen;

	private Point down;

	public ControlOrbWindow(Controller c)
		: base(999.0)
	{
		C = c;
		base.Title = "GlassBoard 控制中心";
		base.ShowActivated = false;
		Root.Margin = new Thickness(14.0);
		ContentGrid.Cursor = Cursors.Hand;
		ContentGrid.Background = Brushes.Transparent;
		ContentGrid.ToolTip = "控制中心 · 单击展开，拖动移动，右键调节图标与大小";
		ContentGrid.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
		{
			down = e.GetPosition(this);
			moved = false;
			panelWasOpen = panel != null && panel.IsVisible;
		};
		ContentGrid.MouseMove += delegate(object s, MouseEventArgs e)
		{
			if (e.LeftButton == MouseButtonState.Pressed && !C.Prefs.ControlCenter.Locked && !moved)
			{
				Point position = e.GetPosition(this);
				if (!(Math.Abs(position.X - down.X) + Math.Abs(position.Y - down.Y) < 8.0))
				{
					moved = true;
					HidePanel();
					try
					{
						DragMove();
						Rect rect = DesktopWindowPolicy.WorkArea(this, pointer: true);
						base.Left = FeatureRules.Position(base.Left, rect.Left, rect.Width, base.Width);
						base.Top = FeatureRules.Position(base.Top, rect.Top, rect.Height, base.Height);
						C.Prefs.ControlCenter.Left = base.Left;
						C.Prefs.ControlCenter.Top = base.Top;
						C.Save();
					}
					catch (InvalidOperationException)
					{
					}
				}
			}
		};
		ContentGrid.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
		{
			if (!moved)
			{
				if (panelWasOpen)
				{
					HidePanel();
				}
				else
				{
					OpenPanel();
				}
			}
			e.Handled = true;
		};
		base.ContextMenuOpening += delegate
		{
			ContextMenu contextMenu = MenuTheme.Create(C.Prefs.Light);
			MenuItem menuItem = new MenuItem
			{
				Header = "控制中心设置"
			};
			menuItem.Click += delegate
			{
				HidePanel();
				C.ControlCenterSettings(this);
			};
			contextMenu.Items.Add(menuItem);
			MenuItem menuItem2 = new MenuItem
			{
				Header = "关闭小球（可从托盘恢复）"
			};
			menuItem2.Click += delegate
			{
				C.Prefs.ControlCenter.Enabled = false;
				C.RefreshControlCenter();
				C.Save();
			};
			contextMenu.Items.Add(menuItem2);
			base.ContextMenu = contextMenu;
		};
		base.ContextMenu = MenuTheme.Create(light: false);
		base.Closed += delegate
		{
			if (panel != null)
			{
				panel.Close();
				panel = null;
			}
		};
		Sync();
	}

	public void Sync()
	{
		ControlCenterOptions controlCenter = C.Prefs.ControlCenter;
		LightTheme = C.Prefs.Light;
		MotionEnabled = controlCenter.Motion && C.Prefs.Dock.MotionEnabled;
		base.Topmost = controlCenter.Topmost;
		double width = (base.Height = controlCenter.Size + 28.0);
		base.Width = width;
		GlassPercent = controlCenter.Opacity;
		ConfigureGlass(C.Prefs.Dock);
		Rect rect = DesktopWindowPolicy.WorkArea(this);
		base.Left = FeatureRules.Position(controlCenter.Left ?? (rect.Right - base.Width - 24.0), rect.Left, rect.Width, base.Width);
		base.Top = FeatureRules.Position(controlCenter.Top ?? (rect.Bottom - base.Height - 104.0), rect.Top, rect.Height, base.Height);
		ContentGrid.Children.Clear();
		ContentGrid.Children.Add(ControlIcons.Create(controlCenter.IconStyle, (Brush)new BrushConverter().ConvertFromString(controlCenter.Color), ControlIconRules.OrbGlyphSize(controlCenter)));
		Backdrop(LightTheme);
		if (panel != null)
		{
			panel.RefreshIconSizes();
		}
	}

	public void OpenPanel()
	{
		if (panel == null)
		{
			panel = new ControlCenterWindow(C, this);
		}
		panel.OpenNear();
	}

	public void HidePanel()
	{
		if (panel != null)
		{
			panel.Collapse();
		}
	}

	public void HideAll()
	{
		if (panel != null)
		{
			panel.Collapse(immediate: true);
		}
		Hide();
	}
}
