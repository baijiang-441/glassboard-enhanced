using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace GlassBoard;

public static class NativeWireless
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct Connection
	{
		public int Mode;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string Profile;

		public IntPtr Ssid;

		public IntPtr Bssid;

		public int Bss;

		public uint Flags;
	}

	private sealed class Handle : IDisposable
	{
		public IntPtr Value;

		public Handle()
		{
			Check(WlanOpenHandle(2u, IntPtr.Zero, out var _, out Value));
		}

		public void Dispose()
		{
			if (Value != IntPtr.Zero)
			{
				WlanCloseHandle(Value, IntPtr.Zero);
				Value = IntPtr.Zero;
			}
		}
	}

	[DllImport("wlanapi.dll")]
	private static extern uint WlanOpenHandle(uint v, IntPtr r, out uint negotiated, out IntPtr handle);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanCloseHandle(IntPtr h, IntPtr r);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanEnumInterfaces(IntPtr h, IntPtr r, out IntPtr list);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanGetAvailableNetworkList(IntPtr h, ref Guid g, uint flags, IntPtr r, out IntPtr list);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanScan(IntPtr h, ref Guid g, IntPtr ssid, IntPtr ie, IntPtr r);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanDisconnect(IntPtr h, ref Guid g, IntPtr r);

	[DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
	private static extern uint WlanSetProfile(IntPtr h, ref Guid g, uint flags, string xml, string security, bool overwrite, IntPtr reserved, out uint reason);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanConnect(IntPtr h, ref Guid g, ref Connection parameters, IntPtr r);

	[DllImport("wlanapi.dll")]
	private static extern void WlanFreeMemory(IntPtr p);

	private static void Check(uint code)
	{
		if (code != 0)
		{
			throw new Win32Exception((int)code);
		}
	}

	private static List<Guid> Interfaces(IntPtr handle)
	{
		Check(WlanEnumInterfaces(handle, IntPtr.Zero, out var list));
		try
		{
			int num = Marshal.ReadInt32(list);
			if (num < 0 || num > 64)
			{
				throw new InvalidDataException("无线网卡列表无效");
			}
			return (from i in Enumerable.Range(0, num)
				select (Guid)Marshal.PtrToStructure(IntPtr.Add(list, 8 + i * 532), typeof(Guid))).ToList();
		}
		finally
		{
			WlanFreeMemory(list);
		}
	}

	internal static WifiNetwork ReadNetwork(IntPtr item, Guid adapter)
	{
		int num = Marshal.ReadInt32(item, 512);
		if (num < 0 || num > 32)
		{
			throw new InvalidDataException("无线网络名称长度无效");
		}
		byte[] array = new byte[num];
		Marshal.Copy(IntPtr.Add(item, 516), array, 0, num);
		int num2 = Marshal.ReadInt32(item, 620);
		WifiNetwork wifiNetwork = new WifiNetwork();
		wifiNetwork.Name = Encoding.UTF8.GetString(array);
		wifiNetwork.Profile = (Marshal.PtrToStringUni(item, 256) ?? "").TrimEnd(default(char));
		wifiNetwork.Adapter = adapter.ToString();
		wifiNetwork.Signal = Marshal.ReadInt32(item, 604);
		wifiNetwork.Secure = Marshal.ReadInt32(item, 608) != 0;
		wifiNetwork.Connected = (num2 & 1) != 0;
		wifiNetwork.Auth = Marshal.ReadInt32(item, 612);
		wifiNetwork.Cipher = Marshal.ReadInt32(item, 616);
		return wifiNetwork;
	}

	public static WifiNetwork[] Networks(bool scan)
	{
		using Handle handle = new Handle();
		List<WifiNetwork> list = new List<WifiNetwork>();
		foreach (Guid item in Interfaces(handle.Value))
		{
			Guid g = item;
			if (scan)
			{
				WlanScan(handle.Value, ref g, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
			}
			Check(WlanGetAvailableNetworkList(handle.Value, ref g, 0u, IntPtr.Zero, out var list2));
			try
			{
				int num = Marshal.ReadInt32(list2);
				if (num < 0 || num > 2048)
				{
					throw new InvalidDataException("无线网络列表无效");
				}
				for (int i = 0; i < num; i++)
				{
					WifiNetwork wifiNetwork = ReadNetwork(IntPtr.Add(list2, 8 + i * 628), g);
					if (wifiNetwork.Name.Length > 0)
					{
						list.Add(wifiNetwork);
					}
				}
			}
			finally
			{
				WlanFreeMemory(list2);
			}
		}
		return list.ToArray();
	}

	public static void Disconnect(string adapter)
	{
		using Handle handle = new Handle();
		Guid g = Guid.Parse(adapter);
		Check(WlanDisconnect(handle.Value, ref g, IntPtr.Zero));
	}

	internal static string ProfileXml(string ssid, string profile, string password, int auth, int cipher)
	{
		bool flag = auth == 1 && cipher == 0;
		if (!flag && (auth != 7 || cipher != 4))
		{
			throw new InvalidOperationException("此网络需要在 Windows Wi-Fi 设置中连接（企业认证或 WPA3）。");
		}
		if (!flag && (password == null || password.Length < 8 || password.Length > 63))
		{
			throw new ArgumentException("WPA2 密码需要 8–63 个字符。");
		}
		if (string.IsNullOrEmpty(ssid) || Encoding.UTF8.GetByteCount(ssid) > 32)
		{
			throw new ArgumentException("网络名称无效。");
		}
		XNamespace xNamespace = "http://www.microsoft.com/networking/WLAN/profile/v1";
		XElement xElement = new XElement(xNamespace + "security", new XElement(xNamespace + "authEncryption", new XElement(xNamespace + "authentication", flag ? "open" : "WPA2PSK"), new XElement(xNamespace + "encryption", flag ? "none" : "AES"), new XElement(xNamespace + "useOneX", false)));
		if (!flag)
		{
			xElement.Add(new XElement(xNamespace + "sharedKey", new XElement(xNamespace + "keyType", "passPhrase"), new XElement(xNamespace + "protected", false), new XElement(xNamespace + "keyMaterial", password)));
		}
		return new XElement(xNamespace + "WLANProfile", new XElement(xNamespace + "name", profile), new XElement(xNamespace + "SSIDConfig", new XElement(xNamespace + "SSID", new XElement(xNamespace + "hex", BitConverter.ToString(Encoding.UTF8.GetBytes(ssid)).Replace("-", "")), new XElement(xNamespace + "name", ssid))), new XElement(xNamespace + "connectionType", "ESS"), new XElement(xNamespace + "connectionMode", "manual"), new XElement(xNamespace + "MSM", xElement)).ToString();
	}

	public static void Connect(string adapter, string ssid, string profile, string password, int auth, int cipher)
	{
		using Handle handle = new Handle();
		Guid g = Guid.Parse(adapter);
		if (string.IsNullOrWhiteSpace(profile))
		{
			profile = "GlassBoard-" + Guid.NewGuid().ToString("N");
			string xml = ProfileXml(ssid, profile, password, auth, cipher);
			Check(WlanSetProfile(handle.Value, ref g, 2u, xml, null, overwrite: false, IntPtr.Zero, out var _));
		}
		Connection connection = default(Connection);
		connection.Mode = 0;
		connection.Profile = profile;
		connection.Bss = 1;
		Connection parameters = connection;
		Check(WlanConnect(handle.Value, ref g, ref parameters, IntPtr.Zero));
	}
}
