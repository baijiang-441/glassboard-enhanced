using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassBoard;

public static class RunningWindows
{
	private delegate bool Callback(IntPtr window, IntPtr value);

	private struct FLASHWINFO
	{
		public uint Size;

		public IntPtr Window;

		public uint Flags;

		public uint Count;

		public uint Timeout;
	}

	private sealed class TargetInfo
	{
		public DateTime Stamp;

		public string Target;

		public string Arguments;
	}

	private static readonly List<RunningWindow> windows = new List<RunningWindow>();

	private static readonly BoundedCache<string, TargetInfo> targets = new BoundedCache<string, TargetInfo>(192, StringComparer.OrdinalIgnoreCase);

	private static readonly BoundedCache<string, long> last = new BoundedCache<string, long>(192, StringComparer.OrdinalIgnoreCase);

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(Callback callback, IntPtr value);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr window);

	[DllImport("user32.dll")]
	private static extern bool IsWindow(IntPtr window);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(IntPtr window);

	[DllImport("user32.dll")]
	private static extern IntPtr GetWindow(IntPtr window, uint command);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
	private static extern int GetWindowLong(IntPtr window, int index);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);

	[DllImport("user32.dll")]
	private static extern bool ShowWindowAsync(IntPtr window, int command);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern IntPtr GetLastActivePopup(IntPtr window);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint access, bool inherit, uint id);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int length);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(IntPtr handle);

	[DllImport("dwmapi.dll")]
	private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

	[DllImport("user32.dll")]
	private static extern bool FlashWindowEx(ref FLASHWINFO info);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(IntPtr window, StringBuilder text, int length);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);

	[DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
	private static extern IntPtr ClassIcon64(IntPtr window, int index);

	[DllImport("user32.dll", EntryPoint = "GetClassLongW")]
	private static extern uint ClassIcon32(IntPtr window, int index);

	public static ImageSource Icon(RunningWindow window, bool highResolution = false)
	{
		try
		{
			SendMessageTimeout(window.Handle, 127u, new IntPtr(1), IntPtr.Zero, 2u, 80u, out var result);
			if (result == IntPtr.Zero)
			{
				SendMessageTimeout(window.Handle, 127u, new IntPtr(2), IntPtr.Zero, 2u, 80u, out result);
			}
			if (result == IntPtr.Zero)
			{
				result = ((IntPtr.Size == 8) ? ClassIcon64(window.Handle, -14) : new IntPtr((int)ClassIcon32(window.Handle, -14)));
			}
			if (result != IntPtr.Zero)
			{
				BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHIcon(result, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
				bitmapSource.Freeze();
                if(highResolution && bitmapSource.PixelWidth<96 && !string.IsNullOrEmpty(window.Path)) {
                    var large=Native.Icon(window.Path,false,true) as BitmapSource;
                    if(large!=null && large.PixelWidth>bitmapSource.PixelWidth)return large;
                }
				return bitmapSource;
			}
			if (!string.IsNullOrEmpty(window.Path))
			{
				ImageSource imageSource = Native.Icon(window.Path,false,highResolution);
				if (imageSource != null)
				{
					return imageSource;
				}
			}
		}
		catch
		{
		}
		BitmapSource bitmapSource2 = Imaging.CreateBitmapSourceFromHIcon(SystemIcons.Application.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
		bitmapSource2.Freeze();
		return bitmapSource2;
	}

	public static string ProcessPath(uint id)
	{
		IntPtr intPtr = OpenProcess(4096u, inherit: false, id);
		if (intPtr == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			int length = 512;
			StringBuilder stringBuilder = new StringBuilder(length);
			if (!QueryFullProcessImageName(intPtr, 0u, stringBuilder, ref length))
			{
				if (Marshal.GetLastWin32Error() != 122)
				{
					return null;
				}
				length = 32768;
				stringBuilder = new StringBuilder(length);
				if (!QueryFullProcessImageName(intPtr, 0u, stringBuilder, ref length))
				{
					return null;
				}
			}
			return WindowActivationRules.NormalizePath(stringBuilder.ToString());
		}
		finally
		{
			CloseHandle(intPtr);
		}
	}

	public static void Scan()
	{
		windows.Clear();
		Dictionary<uint, string> byPid = new Dictionary<uint, string>();
		int own = Process.GetCurrentProcess().Id;
		EnumWindows(delegate(IntPtr handle, IntPtr value)
		{
			if (!IsWindowVisible(handle))
			{
				return true;
			}
			int windowLong = GetWindowLong(handle, -20);
			if (((uint)windowLong & 0x80u) != 0 || ((windowLong & 0x40000) == 0 && GetWindow(handle, 4u) != IntPtr.Zero && IsWindowVisible(GetWindow(handle, 4u))))
			{
				return true;
			}
			try
			{
				if (DwmGetWindowAttribute(handle, 14, out var value2, 4) == 0 && value2 != 0)
				{
					return true;
				}
			}
			catch
			{
			}
			GetWindowThreadProcessId(handle, out var id);
			if (id == own)
			{
				return true;
			}
			if (!byPid.TryGetValue(id, out var value3))
			{
				try
				{
					value3 = ProcessPath(id);
				}
				catch
				{
					value3 = null;
				}
				byPid[id] = value3;
			}
			StringBuilder stringBuilder = new StringBuilder(1024);
			GetWindowText(handle, stringBuilder, stringBuilder.Capacity);
			if (string.IsNullOrWhiteSpace(stringBuilder.ToString()))
			{
				return true;
			}
			StringBuilder stringBuilder2 = new StringBuilder(128);
			GetClassName(handle, stringBuilder2, stringBuilder2.Capacity);
			if (new string[4] { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" }.Contains(stringBuilder2.ToString()))
			{
				return true;
			}
			windows.Add(new RunningWindow
			{
				Handle = handle,
				ProcessId = id,
				Path = value3,
				Title = stringBuilder.ToString()
			});
			return true;
		}, IntPtr.Zero);
	}

	private static void Resolve(Entry entry)
	{
		if (entry == null || entry.IsFolder)
		{
			return;
		}
		string path = entry.Path;
		if (string.IsNullOrEmpty(path))
		{
			return;
		}
		if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
		{
			if (string.IsNullOrWhiteSpace(entry.LaunchTarget))
			{
				LaunchInfoService.Enrich(entry);
			}
			return;
		}
		DateTime dateTime;
		try
		{
			dateTime = File.GetLastWriteTimeUtc(path);
		}
		catch
		{
			dateTime = DateTime.MinValue;
		}
		if (targets.TryGet(path, out var value) && value.Stamp == dateTime)
		{
			entry.LaunchTarget = value.Target;
			entry.LaunchArguments = value.Arguments;
			return;
		}
		LaunchInfoService.Enrich(entry);
		targets.Put(path, new TargetInfo
		{
			Stamp = dateTime,
			Target = entry.LaunchTarget,
			Arguments = entry.LaunchArguments
		});
	}

	public static List<RunningWindow> For(Entry entry)
	{
		if (entry == null || entry.IsFolder || Directory.Exists(entry.Path))
		{
			return new List<RunningWindow>();
		}
		Resolve(entry);
		string[] paths = WindowActivationRules.Executables(entry);
		return (from w in windows
			where paths.Contains(w.Path, StringComparer.OrdinalIgnoreCase)
			orderby w.Handle.ToInt64()
			select w).ToList();
	}

	public static List<RunningWindow> Snapshot()
	{
		return windows.ToList();
	}

	public static bool IsRunning(Entry entry)
	{
		return For(entry).Count > 0;
	}

    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    public static IntPtr ExternalForeground()
    {
        IntPtr window = GetForegroundWindow();
        GetWindowThreadProcessId(window, out uint process);
        return process != 0 && process != (uint)Process.GetCurrentProcess().Id ? window : IntPtr.Zero;
    }

    public static bool MinimizeForeground(IEnumerable<IntPtr> handles, IntPtr beforeDockActivation = default(IntPtr))
    {
        IntPtr foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out uint process);
        // WPF mouse capture / UI Automation can activate the Dock before Click is delivered.
        // Only substitute when our own window took focus, never when another app is foreground.
        if ((foreground == IntPtr.Zero || process == (uint)Process.GetCurrentProcess().Id) && beforeDockActivation != IntPtr.Zero)
            foreground = beforeDockActivation;
        if (foreground == IntPtr.Zero) return false;
        IntPtr owner = GetAncestor(foreground, 3);
        foreach (IntPtr handle in handles)
            if (IsWindow(handle) && !IsIconic(handle) && (handle == foreground || GetAncestor(handle, 3) == owner))
            {
                bool sent = ShowWindowAsync(handle, 6);
                Store.Trace("DOCK minimize handle=" + handle + " previousForeground=" + foreground + " sent=" + sent);
                return sent;
            }
        return false;
    }

	public static bool ActivateHandle(IntPtr handle)
	{
		if (!IsWindow(handle))
		{
			return false;
		}
		if (IsIconic(handle))
		{
			ShowWindowAsync(handle, 9);
		}
		IntPtr lastActivePopup = GetLastActivePopup(handle);
		IntPtr intPtr = ((lastActivePopup != IntPtr.Zero && IsWindowVisible(lastActivePopup)) ? lastActivePopup : handle);
		if (!Native.SetForegroundWindow(intPtr))
		{
			FLASHWINFO fLASHWINFO = default(FLASHWINFO);
			fLASHWINFO.Size = (uint)Marshal.SizeOf(typeof(FLASHWINFO));
			fLASHWINFO.Window = intPtr;
			fLASHWINFO.Flags = 3u;
			fLASHWINFO.Count = 2u;
			FLASHWINFO info = fLASHWINFO;
			FlashWindowEx(ref info);
			Store.Trace("WINDOW_SWITCH foreground request blocked handle=" + intPtr);
		}
		return true;
	}

	public static bool Activate(Entry entry)
	{
		Scan();
		List<RunningWindow> list = For(entry);
		if (list.Count == 0)
		{
			return false;
		}
		IntPtr foregroundWindow = GetForegroundWindow();
		GetWindowThreadProcessId(foregroundWindow, out var id);
		last.TryGet(entry.Path, out var value);
		long value2 = WindowActivationRules.Select(list.Select((RunningWindow w) => w.Handle.ToInt64()), foregroundWindow.ToInt64(), value, id == (uint)Process.GetCurrentProcess().Id);
		if (!ActivateHandle(new IntPtr(value2)))
		{
			return false;
		}
		last.Put(entry.Path, value2);
		return true;
	}
}
