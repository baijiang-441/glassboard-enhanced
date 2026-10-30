using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GlassBoard;

public sealed class ControlCenterWindow : GlassWindow
{
	private sealed class IconScaleConverter : IValueConverter
	{
		public object Convert(object value, Type target, object parameter, CultureInfo culture)
		{
			return (double)value * (double)parameter;
		}

		public object ConvertBack(object value, Type target, object parameter, CultureInfo culture)
		{
			return System.Windows.Data.Binding.DoNothing;
		}
	}

	private sealed class WirelessCard
	{
		public string Kind;

		public System.Windows.Controls.Button Expand;

		public System.Windows.Controls.Button Toggle;

		public TextBlock State;

		public FrameworkElement Arrow;

		public StackPanel List;

		public Border Details;

		public bool Open;

		public int Generation;
	}

	private readonly Controller C;

	private readonly ControlOrbWindow orb;

	private readonly StackPanel body = new StackPanel();

	private readonly TextBlock status = new TextBlock();

	private readonly DispatcherTimer timer = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(3.0)
	};

	private SystemSnapshot snapshot;

	private bool reading;

	private bool command;

	private bool modal;

	private bool stopped;

	private bool collapsing;

	private string page = "home";

	private Slider volume;

	private Slider brightness;

	private System.Windows.Controls.Button mute;

	private bool updating;

	private DateTime lastVolume = DateTime.MinValue;

	private DateTime lastBrightness = DateTime.MinValue;

	private readonly DispatcherTimer volumeDelay = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(300.0)
	};

	private readonly DispatcherTimer brightnessDelay = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(350.0)
	};

	public static readonly DependencyProperty PanelGlyphSizeProperty = DependencyProperty.Register("PanelGlyphSize", typeof(double), typeof(ControlCenterWindow), new PropertyMetadata(24.0));

	private static readonly IconScaleConverter iconScale = new IconScaleConverter();

	private readonly List<WirelessCard> wirelessCards = new List<WirelessCard>();

	public double PanelGlyphSize
	{
		get
		{
			return (double)GetValue(PanelGlyphSizeProperty);
		}
		set
		{
			SetValue(PanelGlyphSizeProperty, value);
		}
	}

	public ControlCenterWindow(Controller c, ControlOrbWindow anchor)
		: base(26.0)
	{
		C = c;
		orb = anchor;
		base.Title = "GlassBoard 控制面板";
		base.Width = 402.0;
		base.Height = 520.0;
		base.Topmost = true;
		base.ShowActivated = true;
		Root.Margin = new Thickness(14.0);
		ContentGrid.Margin = new Thickness(22.0);
		DockPanel dockPanel = new DockPanel();
		ContentGrid.Children.Add(dockPanel);
		Grid grid = new Grid
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
		};
		grid.Children.Add(new TextBlock
		{
			Text = "控制中心",
			FontSize = 20.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = Brushes.White,
			VerticalAlignment = VerticalAlignment.Center
		});
		System.Windows.Controls.Button button = Button("", delegate
		{
			Collapse();
		}, 36.0);
		button.Content = PanelIcon("close", 0.7);
		button.ToolTip = "收起控制中心";
		button.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
		grid.Children.Add(button);
		DockPanel.SetDock(grid, Dock.Top);
		dockPanel.Children.Add(grid);
		WrapPanel wrapPanel = new WrapPanel
		{
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		System.Windows.Controls.Button button2 = Button("", delegate
		{
			Settings("ms-settings:");
		}, 52.0);
		button2.Content = PanelIcon("settings", 0.85);
		button2.Height = 44.0;
		button2.ToolTip = "Windows 设置";
		wrapPanel.Children.Add(button2);
		System.Windows.Controls.Button button3 = Button("", delegate(object s, RoutedEventArgs e)
		{
			PowerMenu((System.Windows.Controls.Button)s);
		}, 52.0);
		button3.Content = PanelIcon("power", 0.85);
		button3.Height = 44.0;
		button3.ToolTip = "电源";
		wrapPanel.Children.Add(button3);
		System.Windows.Controls.Button button4 = Button("", delegate
		{
			modal = true;
			try
			{
				C.ControlCenterSettings(this);
			}
			finally
			{
				modal = false;
			}
		}, 52.0);
		button4.Content = PanelIcon("switches", 0.85);
		button4.Height = 44.0;
		button4.ToolTip = "小球外观";
		wrapPanel.Children.Add(button4);
		grid.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding("Text")
		{
			Source = status
		});
		DockPanel.SetDock(wrapPanel, Dock.Bottom);
		dockPanel.Children.Add(wrapPanel);
		dockPanel.Children.Add(new ScrollViewer
		{
			Content = body,
			VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		});
		base.PreviewKeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
			{
				Collapse();
				e.Handled = true;
			}
		};
		base.Deactivated += delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				if (!modal && !base.IsActive && base.IsVisible)
				{
					Collapse();
				}
			});
		};
		base.IsVisibleChanged += delegate
		{
			if (base.IsVisible)
			{
				timer.Start();
				RefreshState();
			}
			else
			{
				timer.Stop();
				volumeDelay.Stop();
				brightnessDelay.Stop();
			}
		};
		timer.Tick += delegate
		{
			if (page == "home")
			{
				RefreshState();
			}
		};
		base.Closed += delegate
		{
			stopped = true;
			timer.Stop();
			volumeDelay.Stop();
			brightnessDelay.Stop();
		};
		volumeDelay.Tick += delegate
		{
			if (!command)
			{
				volumeDelay.Stop();
				if (volume != null)
				{
					lastVolume = DateTime.UtcNow;
					Run(new BridgeRequest
					{
						Action = "volume",
						Value = volume.Value / 100.0
					});
				}
			}
		};
		brightnessDelay.Tick += delegate
		{
			if (!command)
			{
				brightnessDelay.Stop();
				if (brightness != null)
				{
					lastBrightness = DateTime.UtcNow;
					Run(new BridgeRequest
					{
						Action = "brightness",
						Value = brightness.Value
					});
				}
			}
		};
	}

	public void OpenNear()
	{
		ControlCenterOptions controlCenter = C.Prefs.ControlCenter;
		RefreshIconSizes();
		MotionEnabled = controlCenter.Motion && C.Prefs.Dock.MotionEnabled;
		LightTheme = false;
		GlassPercent = controlCenter.Opacity;
		ConfigureGlass(C.Prefs.Dock);
		Rect rect = DesktopWindowPolicy.WorkArea(orb);
		base.Width = Math.Min(402.0, rect.Width - 12.0);
		base.Height = Math.Min(550.0, rect.Height - 12.0);
		base.Left = Math.Max(rect.Left, Math.Min(rect.Right - base.Width, orb.Left + orb.Width / 2.0 - base.Width / 2.0));
		double num = orb.Top - base.Height + 8.0;
		base.Top = ((num >= rect.Top) ? num : Math.Min(rect.Bottom - base.Height, orb.Top + orb.Height - 8.0));
		page = "home";
		BuildHome();
		Backdrop(LightTheme);
		Show();
		Activate();
		AnimatePanel();
		RefreshState();
	}

	private void AnimatePanel()
	{
		collapsing = false;
		BeginAnimation(UIElement.OpacityProperty, null);
		base.Opacity = 1.0;
		Root.RenderTransformOrigin = new Point(Math.Max(0.0, Math.Min(1.0, (orb.Left + orb.Width / 2.0 - base.Left) / base.Width)), (orb.Top > base.Top) ? 0.95 : 0.05);
		ScaleTransform scaleTransform = new ScaleTransform(1.0, 1.0);
		Root.RenderTransform = scaleTransform;
		if (MotionEnabled)
		{
			BackEase easingFunction = new BackEase
			{
				Amplitude = 0.25,
				EasingMode = EasingMode.EaseOut
			};
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(280.0);
			scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.25, 1.0, timeSpan)
			{
				EasingFunction = easingFunction
			});
			scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, 1.0, timeSpan)
			{
				EasingFunction = easingFunction
			});
			BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(140.0)));
		}
	}

	public void Collapse(bool immediate = false)
	{
		if (!base.IsVisible || collapsing)
		{
			return;
		}
		if (immediate || !MotionEnabled)
		{
			BeginAnimation(UIElement.OpacityProperty, null);
			Hide();
			return;
		}
		collapsing = true;
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120.0));
		doubleAnimation.Completed += delegate
		{
			if (collapsing)
			{
				Hide();
				collapsing = false;
				BeginAnimation(UIElement.OpacityProperty, null);
				base.Opacity = 1.0;
			}
		};
		BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
	}

	private void Label(string text)
	{
		body.Children.Add(new TextBlock
		{
			Text = text,
			Foreground = Brushes.White,
			FontSize = 13.0,
			Margin = new Thickness(0.0, 12.0, 0.0, 8.0),
			TextWrapping = TextWrapping.Wrap
		});
	}

	private System.Windows.Controls.Button ActionButton(string text, Action action, double width = 145.0)
	{
		return Button(text, delegate
		{
			action();
		}, width);
	}

	private void BuildHome()
	{
		page = "home";
		body.Children.Clear();
		BuildWirelessCards();
		Label("音量");
		DockPanel dockPanel = new DockPanel();
		mute = ActionButton("静音", delegate
		{
			if (snapshot != null && snapshot.Audio != null)
			{
				Run(new BridgeRequest
				{
					Action = "mute",
					Enabled = !snapshot.Audio.Muted
				});
			}
		}, 56.0);
		DockPanel.SetDock(mute, Dock.Right);
		dockPanel.Children.Add(mute);
		volume = new Slider
		{
			Minimum = 0.0,
			Maximum = 100.0,
			IsEnabled = false,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(3.0, 6.0, 5.0, 6.0)
		};
		volume.ValueChanged += delegate
		{
			if (!updating)
			{
				volumeDelay.Stop();
				volumeDelay.Start();
			}
		};
		dockPanel.Children.Add(volume);
		body.Children.Add(dockPanel);
		body.Children.Add(ActionButton("选择音频输出  ›", Outputs, 300.0));
		Label("屏幕亮度");
		brightness = new Slider
		{
			Minimum = 0.0,
			Maximum = 100.0,
			IsEnabled = false,
			Margin = new Thickness(3.0, 8.0, 3.0, 10.0)
		};
		brightness.ValueChanged += delegate
		{
			if (!updating)
			{
				brightnessDelay.Stop();
				brightnessDelay.Start();
			}
		};
		body.Children.Add(brightness);
		brightness.ToolTip = "当前屏幕支持 WMI 亮度接口时可调；外接屏可打开显示设置。";
		body.Children.Add(ActionButton("显示设置  ↗", delegate
		{
			Settings("ms-settings:display");
		}, 300.0));
		PowerStatus powerStatus = SystemInformation.PowerStatus;
		string text = ((powerStatus.BatteryLifePercent >= 0f && powerStatus.BatteryLifePercent <= 1f) ? ("电池 " + (powerStatus.BatteryLifePercent * 100f).ToString("0") + "%") : "使用交流电或无电池");
		Label(text);
		UpdateHome();
	}

	private void UpdateHome()
	{
		if (page != "home" || snapshot == null)
		{
			return;
		}
		updating = true;
		try
		{
			UpdateWirelessCards();
			volume.IsEnabled = snapshot.Audio != null;
			if (snapshot.Audio != null)
			{
				if (!volume.IsMouseCaptureWithin && !volumeDelay.IsEnabled && (DateTime.UtcNow - lastVolume).TotalSeconds > 1.0)
				{
					volume.Value = snapshot.Audio.Volume * 100.0;
				}
				mute.Content = (snapshot.Audio.Muted ? "取消静音" : "静音");
			}
			mute.IsEnabled = snapshot.Audio != null;
			brightness.IsEnabled = snapshot.Brightness.HasValue;
			if (snapshot.Brightness.HasValue && !brightness.IsMouseCaptureWithin && !brightnessDelay.IsEnabled && (DateTime.UtcNow - lastBrightness).TotalSeconds > 1.0)
			{
				brightness.Value = snapshot.Brightness.Value;
			}
		}
		finally
		{
			updating = false;
		}
	}

	private async void RefreshState()
	{
		if (reading || command || stopped || !base.IsVisible)
		{
			return;
		}
		reading = true;
		try
		{
			BridgeResult bridgeResult = await C.Systems.Send(new BridgeRequest
			{
				Action = "state"
			});
			if (stopped)
			{
				return;
			}
			snapshot = bridgeResult.System;
			UpdateHome();
			if (snapshot != null)
			{
				string text = string.Join("\n", snapshot.Errors ?? new List<string>());
				if (text.Length > 0 && status.Text != text)
				{
					Store.Trace("Control center: " + text);
				}
				status.Text = text;
			}
		}
		catch (Exception ex)
		{
			if (!stopped)
			{
				status.Text = ex.Message;
				Store.Trace("Control center: " + ex.Message);
			}
		}
		finally
		{
			reading = false;
		}
	}

	private async void Run(BridgeRequest request, Action<BridgeResult> completed = null)
	{
		if (command || stopped)
		{
			return;
		}
		command = true;
		status.Text = "正在操作…";
		body.IsEnabled = false;
		try
		{
			BridgeResult obj = await C.Systems.Send(request);
			if (!stopped)
			{
				status.Text = "已提交系统，状态将在刷新后更新。";
				completed?.Invoke(obj);
			}
		}
		catch (Exception ex)
		{
			if (!stopped)
			{
				status.Text = ex.Message;
				Store.Trace("Control center: " + ex.Message);
			}
		}
		finally
		{
			command = false;
			if (!stopped)
			{
				body.IsEnabled = true;
				if (page == "home")
				{
					RefreshState();
				}
			}
		}
	}

	private void ToggleRadio(string kind)
	{
		if (snapshot == null)
		{
			return;
		}
		List<RadioSnapshot> list = (snapshot.Radios ?? new List<RadioSnapshot>()).Where((RadioSnapshot r) => r.Kind == kind).ToList();
		if (list.Count != 0)
		{
			Run(new BridgeRequest
			{
				Action = "radio",
				Text = kind,
				Enabled = !list.Any((RadioSnapshot r) => r.Enabled)
			}, delegate
			{
				QueueWirelessRefresh(kind);
			});
		}
	}

	private void Back(string title)
	{
		body.Children.Clear();
		body.Children.Add(ActionButton("‹ 返回控制中心", delegate
		{
			BuildHome();
			RefreshState();
		}, 300.0));
		Label(title);
	}

	private void Connect(WifiNetwork n)
	{
		if (n.Connected)
		{
			Run(new BridgeRequest
			{
				Action = "disconnect",
				Adapter = n.Adapter
			}, delegate
			{
				QueueWirelessRefresh("WiFi");
			});
			return;
		}
		string password = "";
		if (string.IsNullOrEmpty(n.Profile) && n.Secure)
		{
			modal = true;
			try
			{
				Window w = FeatureUi.Dialog("连接 " + n.Name, this, 360.0, 215.0);
				StackPanel stackPanel = new StackPanel
				{
					Margin = new Thickness(20.0)
				};
				w.Content = stackPanel;
				stackPanel.Children.Add(new TextBlock
				{
					Text = "Wi-Fi 密码",
					Foreground = Brushes.White,
					Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
				});
				PasswordBox box = new PasswordBox
				{
					MinHeight = 34.0
				};
				stackPanel.Children.Add(box);
				stackPanel.Children.Add(FeatureUi.Button("连接", delegate
				{
					password = box.Password;
					w.DialogResult = true;
				}));
				if (w.ShowDialog() != true)
				{
					return;
				}
			}
			finally
			{
				modal = false;
			}
		}
		Run(new BridgeRequest
		{
			Action = "connect",
			Text = n.Name,
			Adapter = n.Adapter,
			Profile = n.Profile,
			Password = password,
			Auth = n.Auth,
			Cipher = n.Cipher
		}, delegate
		{
			QueueWirelessRefresh("WiFi");
		});
	}

	private void Outputs()
	{
		page = "audio";
		Back("音频输出设备");
		foreach (AudioOutput item2 in (snapshot != null && snapshot.Audio != null) ? (snapshot.Audio.Outputs ?? new List<AudioOutput>()) : new List<AudioOutput>())
		{
			AudioOutput item = item2;
			body.Children.Add(ActionButton((item2.Selected ? "✓ " : "") + item2.Name, delegate
			{
				Run(new BridgeRequest
				{
					Action = "output",
					Key = item.Id
				});
			}, 300.0));
		}
		body.Children.Add(ActionButton("更多声音设置  ↗", delegate
		{
			Settings("ms-settings:sound");
		}, 300.0));
	}

	private void Settings(string uri)
	{
		Collapse(immediate: true);
		try
		{
			Process.Start(new ProcessStartInfo(uri)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show(ex.Message, "无法打开系统设置");
		}
	}

	private void PowerMenu(System.Windows.Controls.Button button)
	{
		System.Windows.Controls.ContextMenu contextMenu = MenuTheme.Create(C.Prefs.Light);
		string[] array = new string[3] { "睡眠", "重启", "关机" };
		foreach (string text in array)
		{
			string action = text;
			System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
			{
				Header = text
			};
			menuItem.Click += delegate
			{
				modal = true;
				try
				{
					if (System.Windows.MessageBox.Show(this, "确定要" + action + "电脑吗？请先保存正在编辑的内容。", "电源", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
					{
						if (action == "睡眠")
						{
							System.Windows.Forms.Application.SetSuspendState(PowerState.Suspend, force: false, disableWakeEvent: false);
						}
						else
						{
							Process.Start(new ProcessStartInfo("shutdown.exe", (action == "重启") ? "/r /t 0" : "/s /t 0")
							{
								UseShellExecute = false,
								CreateNoWindow = true
							});
						}
					}
				}
				catch (Exception ex)
				{
					status.Text = ex.Message;
				}
				finally
				{
					modal = false;
				}
			};
			contextMenu.Items.Add(menuItem);
		}
		button.ContextMenu = contextMenu;
		contextMenu.PlacementTarget = button;
		contextMenu.IsOpen = true;
	}

	private new System.Windows.Controls.Button Button(string label, RoutedEventHandler click, double width = 80.0)
	{
		System.Windows.Controls.Button button = base.Button(label, click, width);
		button.Background = Polish.Color("#24FFFFFF");
		button.BorderBrush = Polish.Color("#30FFFFFF");
		button.Padding = new Thickness(8.0, 4.0, 8.0, 4.0);
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(Border));
		frameworkElementFactory.Name = "Frame";
		frameworkElementFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(12.0));
		frameworkElementFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1.0));
		var array = new[]
		{
			new
			{
				Property = Border.BackgroundProperty,
				Name = "Background"
			},
			new
			{
				Property = Border.BorderBrushProperty,
				Name = "BorderBrush"
			},
			new
			{
				Property = Border.PaddingProperty,
				Name = "Padding"
			}
		};
		foreach (var anon in array)
		{
			frameworkElementFactory.SetBinding(anon.Property, new System.Windows.Data.Binding(anon.Name)
			{
				RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
			});
		}
		FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(ContentPresenter));
		frameworkElementFactory2.SetBinding(FrameworkElement.HorizontalAlignmentProperty, new System.Windows.Data.Binding("HorizontalContentAlignment")
		{
			RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
		});
		frameworkElementFactory2.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		frameworkElementFactory.AppendChild(frameworkElementFactory2);
		ControlTemplate controlTemplate = new ControlTemplate(typeof(System.Windows.Controls.Button))
		{
			VisualTree = frameworkElementFactory
		};
		Trigger trigger = new Trigger
		{
			Property = UIElement.IsKeyboardFocusedProperty,
			Value = true
		};
		trigger.Setters.Add(new Setter(Border.BorderBrushProperty, Polish.Color("#BBD6FF"), "Frame"));
		controlTemplate.Triggers.Add(trigger);
		Trigger trigger2 = new Trigger
		{
			Property = UIElement.IsEnabledProperty,
			Value = false
		};
		trigger2.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4, "Frame"));
		controlTemplate.Triggers.Add(trigger2);
		button.Template = controlTemplate;
		return button;
	}

	private Viewbox PanelIcon(string kind, double scale = 1.0)
	{
		Viewbox viewbox = ControlIcons.Create(kind, Polish.Color("#C8DDFF"), 24.0);
		System.Windows.Data.Binding binding = new System.Windows.Data.Binding("PanelGlyphSize")
		{
			Source = this,
			Converter = iconScale,
			ConverterParameter = scale
		};
		viewbox.SetBinding(FrameworkElement.WidthProperty, binding);
		viewbox.SetBinding(FrameworkElement.HeightProperty, binding);
		return viewbox;
	}

	public void RefreshIconSizes()
	{
		PanelGlyphSize = C.Prefs.ControlCenter.PanelIconSize;
	}

	private System.Windows.Controls.Button IconAction(string kind, string text, Action action, double width)
	{
		System.Windows.Controls.Button button = ActionButton("", action, width);
		StackPanel stackPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal
		};
		stackPanel.Children.Add(PanelIcon(kind, 0.65));
		stackPanel.Children.Add(new TextBlock
		{
			Text = text,
			Margin = new Thickness(7.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		});
		button.Content = stackPanel;
		return button;
	}

	private void BuildWirelessCards()
	{
		wirelessCards.Clear();
		AddWirelessCard("WiFi", "Wi-Fi");
		AddWirelessCard("Bluetooth", "蓝牙");
	}

	private void AddWirelessCard(string kind, string title)
	{
		WirelessCard card = new WirelessCard
		{
			Kind = kind
		};
		wirelessCards.Add(card);
		StackPanel stackPanel = new StackPanel();
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		Grid grid2 = new Grid();
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(40.0)
		});
		grid2.ColumnDefinitions.Add(new ColumnDefinition());
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(24.0)
		});
		Viewbox viewbox = PanelIcon((kind == "WiFi") ? "wifi" : "bluetooth");
		viewbox.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
		grid2.Children.Add(viewbox);
		StackPanel stackPanel2 = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel2.Children.Add(new TextBlock
		{
			Text = title,
			FontSize = 15.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = Brushes.White
		});
		card.State = new TextBlock
		{
			Text = "读取中",
			FontSize = 11.0,
			Foreground = Polish.Color("#C4D0E4"),
			Margin = new Thickness(0.0, 3.0, 0.0, 0.0)
		};
		stackPanel2.Children.Add(card.State);
		Grid.SetColumn(stackPanel2, 1);
		grid2.Children.Add(stackPanel2);
		card.Arrow = PanelIcon("chevron", 0.6);
		card.Arrow.RenderTransformOrigin = new Point(0.5, 0.5);
		card.Arrow.RenderTransform = new RotateTransform();
		Grid.SetColumn(card.Arrow, 2);
		grid2.Children.Add(card.Arrow);
		card.Expand = Button("", delegate
		{
			ExpandWireless(card);
		}, 210.0);
		card.Expand.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
		card.Expand.Width = double.NaN;
		card.Expand.Height = 66.0;
		card.Expand.Content = grid2;
		card.Expand.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
		card.Expand.Padding = new Thickness(12.0, 8.0, 8.0, 8.0);
		card.Expand.Background = Brushes.Transparent;
		card.Expand.ToolTip = "展开" + title + "列表";
		grid.Children.Add(card.Expand);
		card.Toggle = Button("—", delegate
		{
			ToggleRadio(kind);
		}, 58.0);
		card.Toggle.Height = 32.0;
		card.Toggle.VerticalAlignment = VerticalAlignment.Center;
		card.Toggle.ToolTip = "开启或关闭" + title;
		Grid.SetColumn(card.Toggle, 1);
		grid.Children.Add(card.Toggle);
		stackPanel.Children.Add(grid);
		card.List = new StackPanel
		{
			Margin = new Thickness(12.0, 2.0, 12.0, 12.0)
		};
		card.Details = new Border
		{
			Child = card.List,
			Visibility = Visibility.Collapsed,
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
			BorderBrush = Polish.Color("#304D668C")
		};
		stackPanel.Children.Add(card.Details);
		body.Children.Add(new Border
		{
			Child = stackPanel,
			CornerRadius = new CornerRadius(18.0),
			Background = Polish.Color("#334F6687"),
			BorderBrush = Polish.Color("#496D82A0"),
			BorderThickness = new Thickness(1.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		});
	}

	private void UpdateWirelessCards()
	{
		foreach (WirelessCard card in wirelessCards)
		{
			List<RadioSnapshot> list = (snapshot.Radios ?? new List<RadioSnapshot>()).Where((RadioSnapshot r) => r.Kind == card.Kind).ToList();
			bool flag = list.Any((RadioSnapshot r) => r.Enabled);
			card.State.Text = ((list.Count == 0) ? "未检测到适配器" : (flag ? "已开启" : "已关闭"));
			card.Toggle.Content = (flag ? "开" : "关");
			card.Toggle.Background = (flag ? Polish.Color("#698FCC") : Polish.Color("#384659"));
			card.Toggle.IsEnabled = !command && list.Any((RadioSnapshot r) => r.Available);
		}
	}

	private void ExpandWireless(WirelessCard card)
	{
		card.Open = !card.Open;
		card.Generation++;
		((RotateTransform)card.Arrow.RenderTransform).Angle = (card.Open ? 180 : 0);
		card.Details.Visibility = ((!card.Open) ? Visibility.Collapsed : Visibility.Visible);
		if (card.Open)
		{
			if (C.Prefs.ControlCenter.Motion)
			{
				card.Details.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180.0)));
			}
			LoadWireless(card);
		}
	}

	private TextBlock WirelessMessage(string text)
	{
		return new TextBlock
		{
			Text = text,
			Foreground = Polish.Color("#CBD7EB"),
			FontSize = 12.0,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(4.0, 10.0, 4.0, 10.0)
		};
	}

	private async void LoadWireless(WirelessCard card)
	{
		int generation = ++card.Generation;
		card.List.Children.Clear();
		card.List.Children.Add(WirelessMessage("正在查找…"));
		try
		{
			BridgeResult bridgeResult = await C.Systems.Send(new BridgeRequest
			{
				Action = ((card.Kind == "WiFi") ? "networks" : "bluetooth"),
				Enabled = true
			});
			if (stopped || page != "home" || !card.Open || generation != card.Generation || !wirelessCards.Contains(card))
			{
				return;
			}
			card.List.Children.Clear();
			if (card.Kind == "WiFi")
			{
				List<WifiNetwork> list = (from n in bridgeResult.Networks ?? new List<WifiNetwork>()
					orderby n.Connected descending, n.Signal descending
					select n).ToList();
				foreach (WifiNetwork item in list)
				{
					WifiNetwork selected = item;
					string detail = (item.Connected ? "已连接 · 点击断开" : ((item.Secure ? "安全网络" : "开放网络") + " · 信号 " + item.Signal + "%"));
					card.List.Children.Add(WirelessItem(item.Connected ? "check" : "wifi", item.Name, detail, delegate
					{
						Connect(selected);
					}));
				}
				if (list.Count == 0)
				{
					card.List.Children.Add(WirelessMessage("未发现网络，请开启 Wi-Fi 后刷新。"));
				}
			}
			else
			{
				List<BluetoothDevice> list2 = (from d in bridgeResult.Devices ?? new List<BluetoothDevice>()
					orderby d.Connected descending, d.Name
					select d).ToList();
				foreach (BluetoothDevice item2 in list2)
				{
					card.List.Children.Add(WirelessItem("bluetooth", item2.Name, item2.Connected ? "已连接" : "已配对 · 点击管理连接", delegate
					{
						Settings("ms-settings:bluetooth");
					}));
				}
				if (list2.Count == 0)
				{
					card.List.Children.Add(WirelessMessage("暂无已配对设备"));
				}
			}
			Grid grid = new Grid
			{
				Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition());
			grid.ColumnDefinitions.Add(new ColumnDefinition());
			System.Windows.Controls.Button element = IconAction("refresh", "刷新", delegate
			{
				LoadWireless(card);
			}, 115.0);
			grid.Children.Add(element);
			System.Windows.Controls.Button element2 = IconAction((card.Kind == "WiFi") ? "settings" : "plus", (card.Kind == "WiFi") ? "网络设置" : "添加设备", delegate
			{
				Settings((card.Kind == "WiFi") ? "ms-settings:network-wifi" : "ms-settings:bluetooth");
			}, 135.0);
			Grid.SetColumn(element2, 1);
			grid.Children.Add(element2);
			card.List.Children.Add(grid);
		}
		catch (Exception ex)
		{
			if (!stopped && card.Open && generation == card.Generation)
			{
				Store.Trace("Control center wireless: " + ex.Message);
				card.List.Children.Clear();
				TextBlock textBlock = WirelessMessage("读取失败，请稍后刷新");
				textBlock.ToolTip = ex.Message;
				card.List.Children.Add(textBlock);
				card.List.Children.Add(ActionButton("重试", delegate
				{
					LoadWireless(card);
				}, 120.0));
			}
		}
	}

	private System.Windows.Controls.Button WirelessItem(string glyph, string name, string detail, Action action)
	{
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(30.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		Viewbox viewbox = PanelIcon(glyph, 0.8);
		viewbox.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
		grid.Children.Add(viewbox);
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(new TextBlock
		{
			Text = name,
			FontSize = 13.0,
			Foreground = Brushes.White,
			TextTrimming = TextTrimming.CharacterEllipsis
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = detail,
			FontSize = 10.0,
			Foreground = Polish.Color("#BECCE2"),
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
			TextTrimming = TextTrimming.CharacterEllipsis
		});
		Grid.SetColumn(stackPanel, 1);
		grid.Children.Add(stackPanel);
		System.Windows.Controls.Button button = ActionButton("", action);
		button.Width = double.NaN;
		button.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
		button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
		button.Height = 60.0;
		button.Padding = new Thickness(10.0, 7.0, 10.0, 7.0);
		button.Content = grid;
		button.ToolTip = name + "\n" + detail;
		return button;
	}

	private void QueueWirelessRefresh(string kind)
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			foreach (WirelessCard item in wirelessCards.Where((WirelessCard c) => c.Open && c.Kind == kind))
			{
				LoadWireless(item);
			}
		});
	}
}
