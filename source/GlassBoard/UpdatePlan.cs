using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public class UpdatePlan
{
	[DataMember]
	public int Pid;

	[DataMember]
	public string Target;

	[DataMember]
	public string Stage;

	[DataMember]
	public string Backup;

	[DataMember]
	public string Version;

	[DataMember]
	public Dictionary<string, string> Files;
}
