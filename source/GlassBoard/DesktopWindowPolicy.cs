using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;

namespace GlassBoard;

public static class DesktopWindowPolicy
{
	[DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
	private static extern int GetWindowLong(IntPtr window, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
	private static extern int SetWindowLong(IntPtr window, int index, int value);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
	[StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
	[DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
	[DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
	[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
	public static bool RestoreIfCovered(Window window, int x, int y)
	{
		IntPtr hit = GetAncestor(WindowFromPoint(new Point { X=x, Y=y }), 2);
		GetWindowThreadProcessId(hit, out uint process);
		// Leave our preview, context menu and dialogs above their owner.
		if (hit == IntPtr.Zero || process == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return false;
		RaiseWithoutActivation(window);
		return true;
	}

	public static int ToolStyle(int style)
	{
		return (style | 0x80) & -262145;
	}

	public static void Apply(Window window)
	{
		try
		{
			IntPtr handle = new WindowInteropHelper(window).Handle;
			SetWindowLong(handle, -20, ToolStyle(GetWindowLong(handle, -20)));
			if (!SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 55u))
			{
				Store.Log(new Win32Exception(Marshal.GetLastWin32Error(), "刷新桌面窗口类型失败"));
			}
		}
		catch (Exception ex)
		{
			Store.Log(ex);
		}
	}

	public static void RaiseWithoutActivation(Window window)
	{
		IntPtr handle = new WindowInteropHelper(window).Handle;
		if (handle != IntPtr.Zero)
		{
			window.Topmost = true;
			SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
		}
	}

	public static Rect WorkArea(Window window, bool pointer = false, bool fullScreen = false)
	{
		try
		{
			IntPtr handle = new WindowInteropHelper(window).Handle;
			Screen screen;
			if (pointer)
			{
				screen = Screen.FromPoint(Cursor.Position);
			}
			else if (handle != IntPtr.Zero)
			{
				screen = Screen.FromHandle(handle);
			}
			else if (!double.IsNaN(window.Left) && !double.IsNaN(window.Top))
			{
				PresentationSource presentationSource = PresentationSource.FromVisual(window);
				System.Windows.Point point = ((presentationSource != null && presentationSource.CompositionTarget != null) ? presentationSource.CompositionTarget.TransformToDevice : Matrix.Identity).Transform(new System.Windows.Point(window.Left, window.Top));
				screen = Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y));
			}
			else
			{
				screen = Screen.PrimaryScreen;
			}
			Rectangle rectangle = (fullScreen ? screen.Bounds : screen.WorkingArea);
			PresentationSource presentationSource2 = PresentationSource.FromVisual(window);
			Matrix matrix = ((presentationSource2 != null && presentationSource2.CompositionTarget != null) ? presentationSource2.CompositionTarget.TransformFromDevice : Matrix.Identity);
			if (presentationSource2 == null)
			{
				using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
				matrix = new Matrix(96.0 / (double)graphics.DpiX, 0.0, 0.0, 96.0 / (double)graphics.DpiY, 0.0, 0.0);
			}
			System.Windows.Point point2 = matrix.Transform(new System.Windows.Point(rectangle.Left, rectangle.Top));
			System.Windows.Point point3 = matrix.Transform(new System.Windows.Point(rectangle.Right, rectangle.Bottom));
			return new Rect(point2, point3);
		}
		catch
		{
			return SystemParameters.WorkArea;
		}
	}
}
