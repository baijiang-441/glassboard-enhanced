using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public sealed class BluetoothDevice
{
	[DataMember]
	public string Id;

	[DataMember]
	public string Name;

	[DataMember]
	public bool Paired;

	[DataMember]
	public bool Connected;
}
