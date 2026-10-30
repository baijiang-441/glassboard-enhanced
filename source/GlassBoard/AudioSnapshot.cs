using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public sealed class AudioSnapshot
{
	[DataMember]
	public double Volume;

	[DataMember]
	public bool Muted;

	[DataMember]
	public List<AudioOutput> Outputs;
}
