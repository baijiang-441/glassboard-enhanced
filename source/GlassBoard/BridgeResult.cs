using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public sealed class BridgeResult
{
	[DataMember]
	public SystemSnapshot System;

	[DataMember]
	public List<WifiNetwork> Networks;

	[DataMember]
	public List<BluetoothDevice> Devices;
}
