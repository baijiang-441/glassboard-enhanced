using System;
using System.IO;
using System.Reflection;
using System.Security;
using System.Xml;

namespace GlassBoard;

public static class StartupRules
{
	public static readonly string[] Modes = new string[3] { "priority", "standard", "delayed" };

	public static readonly string[] Names = new string[3] { "高 · 登录后优先启动", "普通 · Windows 标准启动项", "低 · 登录后延迟 60 秒" };

	public static string Command(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || path.IndexOf('"') >= 0 || path.IndexOf('\r') >= 0 || path.IndexOf('\n') >= 0)
		{
			throw new ArgumentException("启动路径无效");
		}
		return "\"" + path + "\" --startup";
	}

	public static bool Enabled(string target, byte[] approved)
	{
		if (!string.IsNullOrWhiteSpace(target))
		{
			if (approved != null && approved.Length != 0)
			{
				if (approved[0] != 3)
				{
					return approved[0] != 7;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	public static string Mode(string value)
	{
		if (!(value == "standard") && !(value == "delayed"))
		{
			return "priority";
		}
		return value;
	}

	public static bool MissingTask(Exception error)
	{
		while (error is TargetInvocationException && error.InnerException != null)
		{
			error = error.InnerException;
		}
		if (error != null)
		{
			if (error.HResult != -2147024894)
			{
				return error.HResult == -2147216625;
			}
			return true;
		}
		return false;
	}

	private static string Escape(string value)
	{
		return SecurityElement.Escape(value ?? "");
	}

	public static string TaskXml(string path, string sid, string mode)
	{
		Command(path);
		if (string.IsNullOrWhiteSpace(sid))
		{
			throw new ArgumentException("用户标识无效");
		}
		return "<?xml version=\"1.0\" encoding=\"UTF-16\"?><Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><RegistrationInfo><Description>GlassBoard current-user logon startup</Description></RegistrationInfo><Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + Escape(sid) + "</UserId><Delay>" + ((Mode(mode) == "delayed") ? "PT60S" : "PT0S") + "</Delay></LogonTrigger></Triggers><Principals><Principal id=\"User\"><UserId>" + Escape(sid) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals><Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>6</Priority></Settings><Actions Context=\"User\"><Exec><Command>" + Escape(path) + "</Command><Arguments>--startup</Arguments><WorkingDirectory>" + Escape(Path.GetDirectoryName(path)) + "</WorkingDirectory></Exec></Actions></Task>";
	}

	public static string TaskMode(string xml)
	{
		XmlDocument xmlDocument = new XmlDocument();
		xmlDocument.XmlResolver = null;
		xmlDocument.LoadXml(xml);
		XmlNodeList elementsByTagName = xmlDocument.GetElementsByTagName("Delay");
		if (elementsByTagName.Count <= 0 || !(elementsByTagName[0].InnerText == "PT60S"))
		{
			return "priority";
		}
		return "delayed";
	}

	public static string TaskCommand(string xml)
	{
		XmlDocument xmlDocument = new XmlDocument();
		xmlDocument.XmlResolver = null;
		xmlDocument.LoadXml(xml);
		XmlNodeList elementsByTagName = xmlDocument.GetElementsByTagName("Command");
		if (elementsByTagName.Count != 0)
		{
			return Command(elementsByTagName[0].InnerText);
		}
		return "";
	}
}
