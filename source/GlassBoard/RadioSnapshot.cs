using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public sealed class RadioSnapshot
{
	[DataMember]
	public string Kind;

	[DataMember]
	public string Name;

	[DataMember]
	public bool Enabled;

	[DataMember]
	public bool Available;
}
