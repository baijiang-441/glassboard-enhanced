using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public class Preferences
{
	[DataMember(EmitDefaultValue = false)] public bool DesktopSyncInitialized;
	[DataMember(EmitDefaultValue = false)]
	public string UnassignedLabel;

	[DataMember(EmitDefaultValue = false)]
	public bool UnassignedHidden;

	[DataMember(EmitDefaultValue = false)]
	public int UnassignedPosition;

	public string GroupLabel(string key) => key == "未分组" && !string.IsNullOrWhiteSpace(UnassignedLabel) ? UnassignedLabel : key;

	public List<string> BoardGroups()
	{
		var result = Groups.ToList();
		if (!UnassignedHidden) result.Insert(Math.Max(0, Math.Min(UnassignedPosition, result.Count)), "未分组");
		result.Insert(0, "全部");
		return result;
	}

	public bool EditableGroup(string key) => key == "未分组" || Groups.Contains(key);

	[DataMember(EmitDefaultValue = false)]
	public bool HideWindowsTaskbar;

	[DataMember]
	public List<Entry> Apps = new List<Entry>();

	[DataMember(EmitDefaultValue = false)]
	public List<string> DockOrder = new List<string>();

	[DataMember]
	public int FormatVersion;

	[DataMember(EmitDefaultValue = false)]
	public long SaveRevision;

	[DataMember]
	public bool Light;

	[DataMember]
	public double DockGap = 12.0;

	[DataMember]
	public bool Imported;

	[DataMember]
	public int PanelSize = 1;

	[DataMember]
	public double BoardIconSize = 48.0;

	[DataMember]
	public double BoardIconGap = 5.0;

	[DataMember(EmitDefaultValue = false)]
	public double? OpacityPercent = 72.0;

	[DataMember(EmitDefaultValue = false)]
	public List<string> Removed = new List<string>();

	[DataMember(EmitDefaultValue = false)]
	public List<HotkeySetting> Hotkeys = HotkeyRules.Defaults();

	[DataMember(EmitDefaultValue = false)]
	public DockOptions Dock = new DockOptions();

	[DataMember(EmitDefaultValue = false)]
	public List<string> Groups = new List<string> { "创作", "办公", "游戏" };

	[DataMember(EmitDefaultValue = false)]
	public List<Workspace> Workspaces = new List<Workspace>();

	[DataMember(EmitDefaultValue = false)]
	public PlacementOptions Placement = new PlacementOptions();

	[DataMember(EmitDefaultValue = false)]
	public WidgetOptions Widgets = new WidgetOptions();

	[DataMember]
	public ControlCenterOptions ControlCenter = new ControlCenterOptions();

	[DataMember]
	public bool AutoGrouping = true;

	[DataMember]
	public bool AutoCheckUpdates;

	[DataMember]
	public string StartupTiming = "priority";

	[DataMember]
	public bool UpdateDirectConnection;

	[DataMember]
	public bool BoardDismissOnDeactivate;

	[DataMember]
	public bool BoardHideSearch;

	[DataMember]
	public bool BoardHideGroups;

	[DataMember]
	public bool BoardHideFolders;

	[DataMember]
	public bool GameMode;

	[DataMember]
	public bool AutoGameMode;

	[DataMember]
	public MaterialOptions BoardMaterial = new MaterialOptions();

	[DataMember(EmitDefaultValue = false)]
	public List<string> RemovedIdentities = new List<string>();

	[DataMember]
	public long LastUpdateCheckUtc;

	[DataMember]
	public Dictionary<string, string> LearnedGroups = new Dictionary<string, string>();

	public bool HotkeyMigrationConflict;

	[OnDeserializing]
	private void Defaults(StreamingContext context)
	{
		AutoGrouping = true;
		BoardIconSize = 48.0;
		BoardIconGap = 5.0;
	}

	public void Normalize()
	{
		if (BoardMaterial == null)
		{
			BoardMaterial = new MaterialOptions();
		}
		BoardMaterial.Normalize();
		if (LearnedGroups == null)
		{
			LearnedGroups = new Dictionary<string, string>();
		}
		if (RemovedIdentities == null)
		{
			RemovedIdentities = new List<string>();
		}
		if (Apps == null)
		{
			Apps = new List<Entry>();
		}
		Apps = Apps.Where((Entry a) => a != null && !string.IsNullOrWhiteSpace(a.Path)).ToList();
		foreach (Entry app in Apps)
		{
			if (string.IsNullOrWhiteSpace(app.Name))
			{
				app.Name = Path.GetFileNameWithoutExtension(app.Path);
			}
			FeatureRules.NormalizeMemberships(app);
		}
		Apps = EntryRules.NormalizeEntries(Apps);
		RemovalRules.ReconcileActive(this);
		DockOrderRules.Normalize(this);
		BoardIconSize = LayoutRules.Clamp(BoardIconSize, 28.0, 80.0, 48.0);
		BoardIconGap = LayoutRules.Clamp(BoardIconGap, 0.0, 20.0, 5.0);
		DockGap = LayoutRules.Clamp(DockGap, 0.0, 160.0, 12.0);
		PanelSize = Math.Max(0, Math.Min(2, PanelSize));
		OpacityPercent = LayoutRules.GlassOpacity(OpacityPercent ?? 72.0);
		if (Removed == null)
		{
			Removed = new List<string>();
		}
		bool flag = Hotkeys != null && Hotkeys.All((HotkeySetting k) => k == null || k.ActionId != 7);
		Hotkeys = HotkeyRules.Normalize(Hotkeys);
		HotkeyMigrationConflict = flag && !Hotkeys[6].Enabled;
		if (Dock == null)
		{
			Dock = new DockOptions
			{
				OpacityPercent = OpacityPercent.Value
			};
		}
		Dock.Normalize();
		DockLayoutRules.SyncApps(this);
		Groups = FeatureRules.Groups(Groups, Apps);
		if (Workspaces == null)
		{
			Workspaces = new List<Workspace>();
		}
		Workspaces = Workspaces.Where((Workspace x) => x != null && !string.IsNullOrWhiteSpace(x.Name)).ToList();
		foreach (Workspace workspace in Workspaces)
		{
			workspace.Normalize();
		}
		StartupTiming = StartupRules.Mode(StartupTiming);
		if (ControlCenter == null)
		{
			ControlCenter = new ControlCenterOptions();
			Dock.ShowNetwork = false;
		}
		ControlCenter.Normalize();
		if (Placement == null)
		{
			Placement = new PlacementOptions();
		}
		Placement.Normalize();
		if (Widgets == null)
		{
			Widgets = new WidgetOptions();
		}
		Widgets.Normalize();
	}
}
