using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public sealed class ControlCenterOptions
{
	[DataMember]
	public bool Enabled = true;

	[DataMember]
	public bool Locked;

	[DataMember]
	public bool Topmost;

	[DataMember]
	public bool Motion = true;

	[DataMember]
	public double? Left;

	[DataMember]
	public double? Top;

	[DataMember]
	public double Size = 54.0;

	[DataMember]
	public double Opacity = 80.0;

	[DataMember]
	public double IconSize = 26.0;

	[DataMember]
	public double PanelIconSize = 24.0;

	[DataMember]
	public string IconStyle = "switches";

	[DataMember]
	public string Color = "#A9C9FF";

	[OnDeserializing]
	private void Defaults(StreamingContext context)
	{
		Enabled = true;
		Motion = true;
		Size = 54.0;
		Opacity = 80.0;
		IconSize = 26.0;
		PanelIconSize = 24.0;
		IconStyle = "switches";
		Color = "#A9C9FF";
	}

	public void Normalize()
	{
		IconSize = LayoutRules.Clamp(IconSize, 12.0, 72.0, 26.0);
		PanelIconSize = LayoutRules.Clamp(PanelIconSize, 12.0, 36.0, 24.0);
		IconStyle = ControlIconRules.Style(IconStyle);
		Size = LayoutRules.Clamp(Size, 36.0, 100.0, 54.0);
		Opacity = LayoutRules.Clamp(Opacity, 15.0, 100.0, 80.0);
		if (Left.HasValue && (double.IsNaN(Left.Value) || double.IsInfinity(Left.Value)))
		{
			Left = null;
		}
		if (Top.HasValue && (double.IsNaN(Top.Value) || double.IsInfinity(Top.Value)))
		{
			Top = null;
		}
		string text = WidgetStyle.Color(Color);
		Color = (string.IsNullOrEmpty(text) ? "#A9C9FF" : text);
	}
}
