using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace GlassBoard;

public static class DesktopPersistence
{
	private sealed class Entry
	{
		public Window Window;

		public IntPtr Handle;

		public HwndSource Source;

		public HwndSourceHook Hook;
	}

	private delegate void WinEvent(IntPtr hook, uint type, IntPtr window, int obj, int child, uint thread, uint time);

	private static readonly List<Entry> entries;

	private static readonly WinEvent callback;

	private static IntPtr foregroundHook;

	private static IntPtr reorderHook;

	private static readonly DispatcherTimer settle;

	private static bool applying;

	static DesktopPersistence()
	{
		entries = new List<Entry>();
		callback = Changed;
		settle = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(100.0)
		};
		settle.Tick += delegate
		{
			settle.Stop();
			RefreshOrder();
		};
	}

	[DllImport("user32.dll")]
	private static extern IntPtr GetShellWindow();

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

	[DllImport("user32.dll")]
	private static extern IntPtr GetWindow(IntPtr window, uint command);


	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(IntPtr w, StringBuilder b, int n);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(IntPtr w);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr w);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr w, int command);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr w, IntPtr after, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	private static extern IntPtr SetLong64(IntPtr w, int index, IntPtr value);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
	private static extern int SetLong32(IntPtr w, int index, int value);

	[DllImport("user32.dll")]
	private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEvent proc, uint process, uint thread, uint flags);

	[DllImport("user32.dll")]
	private static extern bool UnhookWinEvent(IntPtr hook);

	private static bool Desktop(IntPtr window)
	{
		if (window == IntPtr.Zero)
		{
			return false;
		}
		StringBuilder stringBuilder = new StringBuilder(80);
		GetClassName(window, stringBuilder, 80);
		if (!(stringBuilder.ToString() == "Progman") && !(stringBuilder.ToString() == "WorkerW"))
		{
			return window == GetShellWindow();
		}
		return true;
	}

	private static void Own(Entry e)
	{
		IntPtr shellWindow = GetShellWindow();
		if (shellWindow != IntPtr.Zero && GetWindow(e.Handle, 4) != shellWindow)
		{
			if (IntPtr.Size == 8)
			{
				SetLong64(e.Handle, -8, shellWindow);
			}
			else
			{
				SetLong32(e.Handle, -8, shellWindow.ToInt32());
			}
		}
	}

	public static void Attach(Window window)
	{
		if (entries.Any((Entry item) => item.Window == window))
		{
			return;
		}
		Entry e = new Entry
		{
			Window = window,
			Handle = new WindowInteropHelper(window).Handle
		};
		if (e.Handle == IntPtr.Zero)
		{
			return;
		}
		e.Source = HwndSource.FromHwnd(e.Handle);
		e.Hook = delegate(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled)
		{
			if (message == 274 && (w.ToInt64() & 0xFFF0) == 61472)
			{
				handled = true;
				return IntPtr.Zero;
			}
			if (message == 5 && w.ToInt64() == 1 && window.IsVisible)
			{
				Schedule();
			}
			return IntPtr.Zero;
		};
		e.Source.AddHook(e.Hook);
		entries.Add(e);
		Own(e);
		window.IsVisibleChanged += delegate
		{
			if (window.IsVisible)
			{
				Schedule();
			}
		};
		window.Closed += delegate
		{
			entries.Remove(e);
			if (e.Source != null && !e.Source.IsDisposed)
			{
				e.Source.RemoveHook(e.Hook);
			}
			if (entries.Count == 0)
			{
				settle.Stop();
				if (foregroundHook != IntPtr.Zero)
				{
					UnhookWinEvent(foregroundHook);
				}
				if (reorderHook != IntPtr.Zero)
				{
					UnhookWinEvent(reorderHook);
				}
				foregroundHook = (reorderHook = IntPtr.Zero);
			}
		};
		if (foregroundHook == IntPtr.Zero)
		{
			foregroundHook = SetWinEventHook(3u, 3u, IntPtr.Zero, callback, 0u, 0u, 0u);
		}
		if (reorderHook == IntPtr.Zero)
		{
			reorderHook = SetWinEventHook(32772u, 32772u, IntPtr.Zero, callback, 0u, 0u, 2u);
		}
		Schedule();
	}

	private static void Changed(IntPtr h, uint type, IntPtr window, int obj, int child, uint thread, uint time)
	{
		if (type == 3 || Desktop(window))
		{
			Schedule();
		}
	}

	private static void Schedule()
	{
		if (applying || Application.Current == null || Application.Current.Dispatcher.HasShutdownStarted)
		{
			return;
		}
		Application.Current.Dispatcher.BeginInvoke((Action)delegate
		{
			if (!settle.IsEnabled)
			{
				settle.Start();
			}
		});
	}

	private static void RefreshOrder()
	{
		if (applying)
		{
			return;
		}
		applying = true;
		try
		{
			IntPtr foreground = GetForegroundWindow();
			bool flag = Desktop(foreground);
			GetWindowThreadProcessId(foreground, out uint foregroundProcess);
			// Dialogs, common file pickers and menus are not in entries. Do not reorder their owners.
			if (foregroundProcess == (uint)Process.GetCurrentProcess().Id
				|| entries.Any(e => !e.Window.IsEnabled
					|| (e.Window is BoardWindow board && board.ModalBusy)
					|| e.Window.OwnedWindows.Cast<Window>().Any(w => w.IsVisible)))
			{
				return;
			}
			foreach (Entry item in entries.ToList())
			{
				if (item.Window.Visibility == Visibility.Visible)
				{
					if (IsIconic(item.Handle) || !IsWindowVisible(item.Handle))
					{
						ShowWindow(item.Handle, 4);
					}
					if (!item.Window.Topmost)
					{
						SetWindowPos(item.Handle, flag ? IntPtr.Zero : new IntPtr(1), 0, 0, 0, 0, 19u);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Store.Log(ex);
		}
		finally
		{
			applying = false;
		}
	}
}
