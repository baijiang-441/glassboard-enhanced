using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public sealed class WifiNetwork
{
	[DataMember]
	public string Name;

	[DataMember]
	public string Profile;

	[DataMember]
	public string Adapter;

	[DataMember]
	public int Signal;

	[DataMember]
	public int Auth;

	[DataMember]
	public int Cipher;

	[DataMember]
	public bool Secure;

	[DataMember]
	public bool Connected;
}
