using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public sealed class AudioOutput
{
	[DataMember]
	public string Id;

	[DataMember]
	public string Name;

	[DataMember]
	public bool Selected;
}
