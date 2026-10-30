using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public sealed class BridgeRequest
{
	[DataMember]
	public int Version = 1;

	[DataMember]
	public int Id;

	[DataMember]
	public string Action = "";

	[DataMember]
	public string Text = "";

	[DataMember]
	public string Key = "";

	[DataMember]
	public string Adapter = "";

	[DataMember]
	public string Profile = "";

	[DataMember]
	public string Password = "";

	[DataMember]
	public string Title = "";

	[DataMember]
	public string Artist = "";

	[DataMember]
	public string Album = "";

	[DataMember]
	public string Source = "";

	[DataMember]
	public string Path = "";

	[DataMember]
	public double Value;

	[DataMember]
	public double Position;

	[DataMember]
	public double Duration;

	[DataMember]
	public bool Enabled;

	[DataMember]
	public bool Playing;

	[DataMember]
	public bool Translation;

	[DataMember]
	public int Offset;

	[DataMember]
	public int Auth;

	[DataMember]
	public int Cipher;
}
