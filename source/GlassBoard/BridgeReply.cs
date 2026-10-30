using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public sealed class BridgeReply
{
	[DataMember]
	public int Version;

	[DataMember]
	public int Id;

	[DataMember]
	public bool Ok;

	[DataMember]
	public string Error;

	[DataMember]
	public BridgeResult Result;
}
