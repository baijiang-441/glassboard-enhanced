namespace GlassBoard;

public static class BackgroundWorkRules
{
	public static double MediaPollSeconds(bool visible, bool available, bool playing)
	{
		if (visible)
		{
			if (available)
			{
				if (!playing)
				{
					return 3.0;
				}
				return 1.5;
			}
			return 5.0;
		}
		return 10.0;
	}
}
