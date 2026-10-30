using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GlassBoard;

public static class NativeAudio
{
	[ComImport]
	[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
	private class Enumerator
	{
	}

	[ComImport]
	[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IEnum
	{
		[PreserveSig]
		int EnumAudioEndpoints(int flow, uint mask, out ICollection devices);

		[PreserveSig]
		int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);

		[PreserveSig]
		int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);

		[PreserveSig]
		int RegisterEndpointNotificationCallback(IntPtr p);

		[PreserveSig]
		int UnregisterEndpointNotificationCallback(IntPtr p);
	}

	[ComImport]
	[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface ICollection
	{
		[PreserveSig]
		int GetCount(out uint count);

		[PreserveSig]
		int Item(uint index, out IDevice device);
	}

	[ComImport]
	[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IDevice
	{
		[PreserveSig]
		int Activate(ref Guid id, uint context, IntPtr args, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);

		[PreserveSig]
		int OpenPropertyStore(uint mode, out IStore store);

		[PreserveSig]
		int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

		[PreserveSig]
		int GetState(out uint state);
	}

	private struct Key
	{
		public Guid Format;

		public uint Id;
	}

	[StructLayout(LayoutKind.Explicit, Size = 24)]
	private struct Variant
	{
		[FieldOffset(0)]
		public ushort Type;

		[FieldOffset(8)]
		public IntPtr Pointer;
	}

	[ComImport]
	[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IStore
	{
		[PreserveSig]
		int GetCount(out uint count);

		[PreserveSig]
		int GetAt(uint index, out Key key);

		[PreserveSig]
		int GetValue(ref Key key, out Variant value);

		[PreserveSig]
		int SetValue(ref Key key, ref Variant value);

		[PreserveSig]
		int Commit();
	}

	[ComImport]
	[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IVolume
	{
		[PreserveSig]
		int RegisterControlChangeNotify(IntPtr p);

		[PreserveSig]
		int UnregisterControlChangeNotify(IntPtr p);

		[PreserveSig]
		int GetChannelCount(out uint n);

		[PreserveSig]
		int SetMasterVolumeLevel(float v, ref Guid context);

		[PreserveSig]
		int SetMasterVolumeLevelScalar(float v, ref Guid context);

		[PreserveSig]
		int GetMasterVolumeLevel(out float v);

		[PreserveSig]
		int GetMasterVolumeLevelScalar(out float v);

		[PreserveSig]
		int SetChannelVolumeLevel(uint n, float v, ref Guid context);

		[PreserveSig]
		int SetChannelVolumeLevelScalar(uint n, float v, ref Guid context);

		[PreserveSig]
		int GetChannelVolumeLevel(uint n, out float v);

		[PreserveSig]
		int GetChannelVolumeLevelScalar(uint n, out float v);

		[PreserveSig]
		int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

		[PreserveSig]
		int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
	}

	[ComImport]
	[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
	private class Policy
	{
	}

	[ComImport]
	[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPolicy
	{
		[PreserveSig]
		int GetMixFormat(IntPtr a, IntPtr b);

		[PreserveSig]
		int GetDeviceFormat(IntPtr a, int b, IntPtr c);

		[PreserveSig]
		int ResetDeviceFormat(IntPtr a);

		[PreserveSig]
		int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);

		[PreserveSig]
		int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);

		[PreserveSig]
		int SetProcessingPeriod(IntPtr a, IntPtr b);

		[PreserveSig]
		int GetShareMode(IntPtr a, IntPtr b);

		[PreserveSig]
		int SetShareMode(IntPtr a, IntPtr b);

		[PreserveSig]
		int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);

		[PreserveSig]
		int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);

		[PreserveSig]
		int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);

		[PreserveSig]
		int SetEndpointVisibility(IntPtr a, int b);
	}

	[DllImport("ole32.dll")]
	private static extern int PropVariantClear(ref Variant v);

	private static void Check(int hr)
	{
		Marshal.ThrowExceptionForHR(hr);
	}

	private static void Release(object o)
	{
		if (o != null && Marshal.IsComObject(o))
		{
			Marshal.ReleaseComObject(o);
		}
	}

	private static string Name(IDevice d)
	{
		IStore store = null;
		Variant value = default(Variant);
		try
		{
			Check(d.OpenPropertyStore(0u, out store));
			Key key = default(Key);
			key.Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0");
			key.Id = 14u;
			Key key2 = key;
			Check(store.GetValue(ref key2, out value));
			return (value.Type == 31) ? (Marshal.PtrToStringUni(value.Pointer) ?? "音频设备") : "音频设备";
		}
		finally
		{
			PropVariantClear(ref value);
			Release(store);
		}
	}

	public static AudioSnapshot State()
	{
		IEnum @enum = null;
		IDevice device = null;
		IVolume volume = null;
		ICollection devices = null;
		try
		{
			@enum = (IEnum)new Enumerator();
			Check(@enum.GetDefaultAudioEndpoint(0, 1, out device));
			Check(device.GetId(out var id));
			Guid id2 = typeof(IVolume).GUID;
			Check(device.Activate(ref id2, 23u, IntPtr.Zero, out var endpoint));
			volume = (IVolume)endpoint;
			Check(volume.GetMasterVolumeLevelScalar(out var v));
			Check(volume.GetMute(out var mute));
			Check(@enum.EnumAudioEndpoints(0, 1u, out devices));
			Check(devices.GetCount(out var count));
			List<AudioOutput> list = new List<AudioOutput>();
			for (uint num = 0u; num < count; num++)
			{
				IDevice device2 = null;
				try
				{
					Check(devices.Item(num, out device2));
					Check(device2.GetId(out var id3));
					list.Add(new AudioOutput
					{
						Id = id3,
						Name = Name(device2),
						Selected = (id3 == id)
					});
				}
				finally
				{
					Release(device2);
				}
			}
			return new AudioSnapshot
			{
				Volume = v,
				Muted = mute,
				Outputs = list
			};
		}
		finally
		{
			Release(devices);
			Release(volume);
			Release(device);
			Release(@enum);
		}
	}

	public static void Set(double? level, bool? muted)
	{
		IEnum @enum = null;
		IDevice device = null;
		IVolume volume = null;
		try
		{
			@enum = (IEnum)new Enumerator();
			Check(@enum.GetDefaultAudioEndpoint(0, 1, out device));
			Guid id = typeof(IVolume).GUID;
			Check(device.Activate(ref id, 23u, IntPtr.Zero, out var endpoint));
			volume = (IVolume)endpoint;
			Guid context = Guid.Empty;
			if (level.HasValue)
			{
				Check(volume.SetMasterVolumeLevelScalar((float)Math.Max(0.0, Math.Min(1.0, level.Value)), ref context));
			}
			if (muted.HasValue)
			{
				Check(volume.SetMute(muted.Value, ref context));
			}
		}
		finally
		{
			Release(volume);
			Release(device);
			Release(@enum);
		}
	}

	public static void Output(string id)
	{
		IEnum @enum = null;
		IDevice device = null;
		IPolicy policy = null;
		try
		{
			@enum = (IEnum)new Enumerator();
			Check(@enum.GetDevice(id, out device));
			Check(device.GetState(out var state));
			if (state != 1)
			{
				throw new InvalidOperationException("音频设备当前不可用");
			}
			policy = (IPolicy)new Policy();
			for (int i = 0; i < 3; i++)
			{
				Check(policy.SetDefaultEndpoint(id, i));
			}
		}
		finally
		{
			Release(policy);
			Release(device);
			Release(@enum);
		}
	}
}
