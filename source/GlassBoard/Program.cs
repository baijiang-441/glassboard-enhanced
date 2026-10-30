using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace GlassBoard;

public static class Program
{
	[STAThread]
	public static int Main(string[] args)
	{
		if (args.Length != 0 && args[0] == "--netease-ui-bridge")
		{
			return NeteaseUiReader.Run();
		}
		if (args.Length != 0 && args[0] == "--self-test")
		{
			return SelfTest.Run();
		}
		if (args.Length != 0 && args[0] == "--restore-shell")
		{
			try
			{
				if (Native.RequestRestore())
				{
					return 0;
				}
				try
				{
					TaskbarAutoHide.ShowAndClear();
				}
				catch (Exception ex)
				{
					Store.Log(ex);
				}
				DesktopVisibility.ShowAll();
				MessageBox.Show("已执行任务栏和桌面图标恢复。", "GlassBoard");
				return 0;
			}
			catch (Exception ex2)
			{
				MessageBox.Show(ex2.Message, "GlassBoard");
				return 1;
			}
		}
		bool createdNew;
		using (new Mutex(initiallyOwned: true, "Local\\GlassBoard.Desktop.v1", out createdNew))
		{
			if (!createdNew)
			{
				MessageBox.Show("已有 GlassBoard 在运行，本次启动的 v0.9.17 尚未运行。\n请先从托盘退出已有程序，再启动此版本。", "GlassBoard v0.9.17");
				return 0;
			}
			try
			{
				Store.Trace("START exe=" + Assembly.GetExecutingAssembly().Location + " OS=" + Environment.OSVersion?.ToString() + " CLR=" + Environment.Version);
				try
				{
					DesktopVisibility.Recover();
				}
				catch (Exception ex3)
				{
					Store.Log(ex3);
				}
				try
				{
					TaskbarAutoHide.Restore();
				}
				catch (Exception ex4)
				{
					Store.Log(ex4);
				}
				Application application = new Application();
				application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
				application.Exit += delegate
				{
					try
					{
						DesktopVisibility.RestoreOriginal();
					}
					catch (Exception ex7)
					{
						Store.Log(ex7);
					}
					try
					{
						TaskbarAutoHide.Restore();
					}
					catch (Exception ex8)
					{
						Store.Log(ex8);
					}
				};
				Controller controller = null;
				application.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
				{
					Store.Log(e.Exception);
					if (controller != null)
					{
						try
						{
							Store.Save(controller.Prefs);
						}
						catch (Exception ex6)
						{
							Store.Log(ex6);
						}
					}
					MessageBox.Show("操作失败，详情已记录在配置文件夹 error.log。\n" + e.Exception.Message, "GlassBoard");
					e.Handled = true;
				};
				controller = new Controller();
				application.SessionEnding += delegate
				{
					controller.SaveSession();
				};
				if (args.Any((string a) => a == "--recover-widgets"))
				{
					controller.RecoverWidgets();
				}
				application.Run();
				GC.KeepAlive(controller);
				return 0;
			}
			catch (Exception ex5)
			{
				Store.Log(ex5);
				MessageBox.Show("启动失败：" + ex5.Message, "GlassBoard");
				return 1;
			}
		}
	}
}
