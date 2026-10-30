using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace GlassBoard;

public static class StartupService
{
	private sealed class Scheduler : IDisposable
	{
		private readonly List<object> objects = new List<object>();

		private readonly object folder;

		private readonly string user;

		private readonly string name;

		private object Keep(object value)
		{
			if (value != null)
			{
				objects.Add(value);
			}
			return value;
		}

		private static object Call(object o, string method, params object[] args)
		{
			return o.GetType().InvokeMember(method, BindingFlags.InvokeMethod | BindingFlags.OptionalParamBinding, null, o, args);
		}

		private static object Get(object o, string property)
		{
			return o.GetType().InvokeMember(property, BindingFlags.GetProperty, null, o, null);
		}

		public Scheduler()
		{
			using (WindowsIdentity windowsIdentity = WindowsIdentity.GetCurrent())
			{
				user = windowsIdentity.User.Value;
			}
			name = "GlassBoard-" + user;
			Type typeFromProgID = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true);
			object o = Keep(Activator.CreateInstance(typeFromProgID));
			Call(o, "Connect");
			folder = Keep(Call(o, "GetFolder", "\\"));
		}

		public string Read(out bool enabled)
		{
			enabled = false;
			object o;
			try
			{
				o = Keep(Call(folder, "GetTask", name));
			}
			catch (Exception error)
			{
				if (StartupRules.MissingTask(error))
				{
					return null;
				}
				throw;
			}
			enabled = (bool)Get(o, "Enabled");
			return (string)Get(o, "Xml");
		}

		public void Register(string xml)
		{
			Keep(Call(folder, "RegisterTask", name, xml, 6, user, null, 3, null));
		}

		public void Delete()
		{
			if (Read(out var _) != null)
			{
				Call(folder, "DeleteTask", name, 0);
			}
		}

		public void Dispose()
		{
			for (int num = objects.Count - 1; num >= 0; num--)
			{
				if (Marshal.IsComObject(objects[num]))
				{
					Marshal.ReleaseComObject(objects[num]);
				}
			}
		}
	}

	private const string Key = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string Approved = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";

	private const string Name = "GlassBoard";

	private static bool RunEnabled
	{
		get
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			using RegistryKey registryKey2 = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run");
			return StartupRules.Enabled((registryKey == null) ? null : (registryKey.GetValue("GlassBoard") as string), (registryKey2 == null) ? null : (registryKey2.GetValue("GlassBoard") as byte[]));
		}
	}

	public static bool Enabled
	{
		get
		{
			using Scheduler scheduler = new Scheduler();
			bool enabled;
			return (scheduler.Read(out enabled) != null && enabled) || RunEnabled;
		}
	}

	public static string Mode
	{
		get
		{
			using Scheduler scheduler = new Scheduler();
			bool enabled;
			string text = scheduler.Read(out enabled);
			return (text != null && enabled) ? StartupRules.TaskMode(text) : "standard";
		}
	}

	public static string Target
	{
		get
		{
			using (Scheduler scheduler = new Scheduler())
			{
				bool enabled;
				string text = scheduler.Read(out enabled);
				if (text != null && enabled)
				{
					return StartupRules.TaskCommand(text);
				}
			}
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			return (registryKey == null) ? null : (registryKey.GetValue("GlassBoard") as string);
		}
	}

	private static void WriteRun(string value, byte[] approved)
	{
		using (RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
		{
			if (value == null)
			{
				registryKey.DeleteValue("GlassBoard", throwOnMissingValue: false);
			}
			else
			{
				registryKey.SetValue("GlassBoard", value, RegistryValueKind.String);
			}
		}
		using RegistryKey registryKey2 = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run");
		if (approved == null)
		{
			registryKey2.DeleteValue("GlassBoard", throwOnMissingValue: false);
		}
		else
		{
			registryKey2.SetValue("GlassBoard", approved, RegistryValueKind.Binary);
		}
	}

	public static void Set(bool enabled)
	{
		Set(enabled, "standard");
	}

	public static void Set(bool enabled, string mode)
	{
		mode = StartupRules.Mode(mode);
		string value;
		using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
		{
			value = ((registryKey == null) ? null : (registryKey.GetValue("GlassBoard") as string));
		}
		byte[] approved;
		using (RegistryKey registryKey2 = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run"))
		{
			approved = ((registryKey2 == null) ? null : (registryKey2.GetValue("GlassBoard") as byte[]));
		}
		using Scheduler scheduler = new Scheduler();
		bool enabled2;
		string text = scheduler.Read(out enabled2);
		try
		{
			if (enabled && mode != "standard")
			{
				string value2;
				using (WindowsIdentity windowsIdentity = WindowsIdentity.GetCurrent())
				{
					value2 = windowsIdentity.User.Value;
				}
				scheduler.Register(StartupRules.TaskXml(Assembly.GetExecutingAssembly().Location, value2, mode));
				WriteRun(null, null);
			}
			else
			{
				WriteRun(enabled ? StartupRules.Command(Assembly.GetExecutingAssembly().Location) : null, null);
				scheduler.Delete();
			}
		}
		catch (Exception ex2)
		{
			try
			{
				WriteRun(value, approved);
				if (text == null)
				{
					scheduler.Delete();
				}
				else
				{
					scheduler.Register(text);
				}
			}
			catch (Exception ex)
			{
				throw new IOException("启动设置失败，恢复原设置也未完成：" + ex.GetBaseException().Message, ex2);
			}
			throw new IOException("启动设置未完成，已恢复原启动项：" + ex2.GetBaseException().Message, ex2);
		}
	}
}
