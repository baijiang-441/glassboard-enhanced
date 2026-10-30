using System;
using System.Management;

namespace GlassBoard;

public static class NativeBrightness
{
	public static double? Read()
	{
		using (ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=True"))
		{
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectCollection.GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				ManagementObject managementObject = (ManagementObject)managementObjectEnumerator.Current;
				using (managementObject)
				{
					return Convert.ToDouble(managementObject["CurrentBrightness"]);
				}
			}
		}
		return null;
	}

	public static void Set(double value)
	{
		bool flag = false;
		using (ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True"))
		{
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			foreach (ManagementObject item in managementObjectCollection)
			{
				using (item)
				{
					item.InvokeMethod("WmiSetBrightness", new object[2]
					{
						0u,
						(byte)Math.Max(0.0, Math.Min(100.0, value))
					});
					flag = true;
				}
			}
		}
		if (!flag)
		{
			throw new InvalidOperationException("当前屏幕不支持系统亮度调节，请使用显示器按键。");
		}
	}
}
