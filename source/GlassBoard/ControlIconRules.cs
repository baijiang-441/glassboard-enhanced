using System;

namespace GlassBoard;

public static class ControlIconRules
{
	public static readonly string[] Styles = new string[3] { "switches", "tiles", "spark" };

	public static readonly string[] Names = new string[3] { "圆角双拨杆", "四格面板", "四瓣星芒" };

	public static string Style(string value)
	{
		if (Array.IndexOf(Styles, value) >= 0)
		{
			return value;
		}
		return Styles[0];
	}

	public static double OrbGlyphSize(ControlCenterOptions options)
	{
		return Math.Min(options.IconSize, options.Size - 12.0);
	}
}
