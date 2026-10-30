using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public sealed class SystemSnapshot
{
	[DataMember]
	public AudioSnapshot Audio;

	[DataMember]
	public double? Brightness;

	[DataMember]
	public List<RadioSnapshot> Radios;

	[DataMember]
	public List<string> Errors;
}
