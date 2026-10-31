using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassBoard;

public static class Native
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct SHFILEINFO
	{
		public IntPtr hIcon;

		public int iIcon;

		public uint attributes;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string displayName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
		public string typeName;
	}

	public const int RestoreMessage = 32815;

	private static readonly BoundedCache<string, ImageSource> icons = new BoundedCache<string, ImageSource>(192, StringComparer.OrdinalIgnoreCase);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindow(string cls, string title);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool PostMessage(IntPtr h, int message, IntPtr w, IntPtr l);

	public static bool RequestRestore()
	{
		IntPtr intPtr = FindWindow(null, "GlassBoard Dock");
		if (intPtr != IntPtr.Zero)
		{
			return PostMessage(intPtr, 32815, IntPtr.Zero, IntPtr.Zero);
		}
		return false;
	}

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);

	[DllImport("user32.dll")]
	public static extern bool UnregisterHotKey(IntPtr h, int id);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool SetWindowDisplayAffinity(IntPtr h, uint value);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool GetWindowDisplayAffinity(IntPtr h, out uint value);

	[DllImport("user32.dll")]
	public static extern bool SetForegroundWindow(IntPtr h);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr SHGetFileInfo(string path, uint attr, out SHFILEINFO info, uint cb, uint flags);

	[DllImport("comctl32.dll")]
	private static extern IntPtr ImageList_GetIcon(IntPtr list, int index, uint flags);

	[DllImport("user32.dll")]
	private static extern bool DestroyIcon(IntPtr h);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr pidl, uint mask, out uint attributes);
	[DllImport("shell32.dll", EntryPoint="SHGetFileInfoW")]
	private static extern IntPtr SHGetPidlInfo(IntPtr pidl, uint attr, out SHFILEINFO info, uint cb, uint flags);

	public static ImageSource Icon(string path, bool showShortcutOverlays = false, bool sharper = false)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		string key = (sharper ? "sharp/" : "normal/") + (showShortcutOverlays ? "overlay/" : "plain/") + path;
		if (icons.TryGet(key, out var value))
		{
			return value;
		}
		if (DesktopEntries.IsShell(path)) {
			IntPtr pidl = IntPtr.Zero;
			try {
				if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out uint attributes) != 0) return null;
				SHGetPidlInfo(pidl, 0, out SHFILEINFO shell, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 0x108);
				if (shell.hIcon == IntPtr.Zero) return null;
				try {
					var bitmap = Imaging.CreateBitmapSourceFromHIcon(shell.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(64,64));
					bitmap.Freeze(); icons.Put(key, bitmap); return bitmap;
				} finally { DestroyIcon(shell.hIcon); }
			} finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
		}
		SHFILEINFO info;
		IntPtr intPtr;
		if (showShortcutOverlays)
		{
			SHGetFileInfo(path, 0u, out info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), sharper ? 16736u : 256u);
			intPtr = info.hIcon;
			if (sharper && intPtr != IntPtr.Zero)
			{
				BitmapSource bitmapSource = ShellIconImages.Read(info.iIcon & 0xFFFFFF, (uint)(info.iIcon >>> 24 << 8));
				if (bitmapSource != null)
				{
					if (intPtr != IntPtr.Zero)
					{
						DestroyIcon(intPtr);
					}
					icons.Put(key, bitmapSource);
					return bitmapSource;
				}
			}
		}
		else
		{
			IntPtr intPtr2 = SHGetFileInfo(path, 0u, out info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 16384u);
			if (sharper && intPtr2 != IntPtr.Zero)
			{
				BitmapSource bitmapSource2 = ShellIconImages.Read(info.iIcon);
				if (bitmapSource2 != null)
				{
					icons.Put(key, bitmapSource2);
					return bitmapSource2;
				}
			}
			intPtr = ((intPtr2 == IntPtr.Zero) ? IntPtr.Zero : ImageList_GetIcon(intPtr2, info.iIcon, 1u));
		}
		if (intPtr == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			BitmapSource bitmapSource3 = Imaging.CreateBitmapSourceFromHIcon(intPtr, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(64, 64));
			bitmapSource3.Freeze();
			icons.Put(key, bitmapSource3);
			return bitmapSource3;
		}
		catch
		{
			return null;
		}
		finally
		{
			DestroyIcon(intPtr);
		}
	}
	public static ImageSource CachedIcon(string path, bool overlays = false, bool sharper = false)
	{
		if (string.IsNullOrWhiteSpace(path)) return null;
		icons.TryGet((sharper ? "sharp/" : "normal/") + (overlays ? "overlay/" : "plain/") + path, out var image);
		return image;
	}

	public static void ClearIconCache()
	{
		icons.Clear();
	}

	public static void Computer()
	{
		Process.Start(new ProcessStartInfo("explorer.exe", "shell:MyComputerFolder")
		{
			UseShellExecute = true
		});
	}
}
