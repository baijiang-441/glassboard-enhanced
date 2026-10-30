using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GlassBoard;

public sealed class MusicEngine : IDisposable
{
	private readonly Controller C;

	private MediaPlayer player;

	private readonly MediaSessionBridge bridge = new MediaSessionBridge
	{
		ArtworkOnly = true
	};

	private readonly MediaSessionBridge nativeBridge = new MediaSessionBridge(native: true)
	{
		ArtworkOnly = true
	};

	private MediaSnapshot nativeCache;

	private DateTime nativeAt = DateTime.MinValue;

	private DateTime lastNative = DateTime.MinValue;

	private string nativeStatus = "尚未尝试";

	private string primaryStatus = "尚未尝试";

	private MediaSnapshot discovered;

	private DateTime discoveredAt = DateTime.MinValue;

	private DateTime lastDiscovery = DateTime.MinValue;

	private DateTime nextPrimary = DateTime.MinValue;

	private readonly DispatcherTimer timer = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(1.0)
	};

	private readonly Random random = new Random();

	private bool stopped;

	private bool polling;

	private bool opened;

	private bool playing;

	private int metadataVersion;

	private DateTime lastPoll = DateTime.MinValue;

	private string systemKey = "";

	private string loadedPath = "";

	private bool loadedLocal;

	private DateTime sessionAt = DateTime.UtcNow;

	private DateTime seekUntil = DateTime.MinValue;

	private double? seekTarget;

	private readonly MediaSeekQueue seeks = new MediaSeekQueue();

	private string iconSource = "";

	public string Title = "等待音乐";

	public string Artist = "选择本地音乐，或在播放器中开始播放";

	public string Album = "";

	public string Status = "";

	public ImageSource Artwork;

	public ImageSource AppIcon;

	private readonly TrackArtworkCache<ImageSource> artworkCache = new TrackArtworkCache<ImageSource>();

	private DateTime lastAvailable = DateTime.MinValue;

	private DateTime stableMetadataAt = DateTime.MinValue;

	private MediaSnapshot stableMetadata;

	private string lastCoverFingerprint = "";

	public MediaSnapshot Session = new MediaSnapshot();

	private DateTime lastArtworkPoll = DateTime.MinValue;

	public bool Local => C.Prefs.Widgets.MusicMode == "local";

	public bool Playing
	{
		get
		{
			if (!Local)
			{
				return Session.Playing;
			}
			return playing;
		}
	}

	public double Duration
	{
		get
		{
			if (!Local)
			{
				return Session.Duration;
			}
			if (player == null || !opened || !player.NaturalDuration.HasTimeSpan)
			{
				return 0.0;
			}
			return player.NaturalDuration.TimeSpan.TotalSeconds;
		}
	}

	public double Position
	{
		get
		{
			if (Local)
			{
				if (player == null || !opened)
				{
					return 0.0;
				}
				return player.Position.TotalSeconds;
			}
			if (seekTarget.HasValue && DateTime.UtcNow < seekUntil)
			{
				return MediaTimeline.ClampSeek(seekTarget.Value, Session.MinSeek, Session.MaxSeek, Duration);
			}
			return MediaTimeline.Position(Session.Position, Session.Duration, Session.Playing, Session.Rate, Session.Age + (DateTime.UtcNow - sessionAt).TotalSeconds);
		}
	}

	public bool CanSeek
	{
		get
		{
			if (!Local)
			{
				if (Session.Available && Session.CanSeek)
				{
					return Session.Duration > 0.0;
				}
				return false;
			}
			if (player != null && opened)
			{
				return player.NaturalDuration.HasTimeSpan;
			}
			return false;
		}
	}

	public event Action Changed;

	public MusicEngine(Controller c)
	{
		C = c;
		timer.Tick += delegate
		{
			if (!stopped)
			{
				timer.Interval = TimeSpan.FromSeconds(C.MusicVisible ? 1 : 5);
				if (!Local && (DateTime.UtcNow - lastPoll).TotalSeconds >= BackgroundWorkRules.MediaPollSeconds(C.MusicVisible, Session.Available, Session.Playing))
				{
					Poll();
				}
				DrainSeek();
			}
		};
	}

	public void Wake()
	{
		if (!stopped)
		{
			timer.Interval = TimeSpan.FromSeconds(1.0);
			if (!Local)
			{
				Poll();
			}
		}
	}

	public void Start()
	{
		if (!stopped && !timer.IsEnabled)
		{
			timer.Start();
			if (Local)
			{
				Load(autoplay: false);
			}
			else
			{
				Poll();
			}
		}
	}

	private void EnsurePlayer()
	{
		if (player != null)
		{
			return;
		}
		MediaPlayer mediaPlayer = new MediaPlayer();
		try
		{
			mediaPlayer.Volume = C.Prefs.Widgets.Volume;
			mediaPlayer.MediaOpened += delegate
			{
				if (!stopped)
				{
					opened = true;
					Notify();
				}
			};
			mediaPlayer.MediaFailed += delegate
			{
				opened = false;
				playing = false;
				Status = "此文件暂时无法播放，请检查音频格式";
				Notify();
			};
			mediaPlayer.MediaEnded += delegate
			{
				if (!stopped && Local)
				{
					Advance(ended: true);
				}
			};
			player = mediaPlayer;
		}
		catch
		{
			mediaPlayer.Close();
			throw;
		}
	}

	private void Notify()
	{
		if (!stopped && this.Changed != null)
		{
			this.Changed();
		}
	}

	public void Update()
	{
		bridge.UseNative = false;
		if (!C.Prefs.Widgets.AutoNativeMediaCompatibility && !C.Prefs.Widgets.NativeMediaCompatibility)
		{
			nativeCache = null;
			lastNative = DateTime.MinValue;
		}
		if (player != null)
		{
			player.Volume = C.Prefs.Widgets.Volume;
		}
		WidgetOptions widgets = C.Prefs.Widgets;
		string text = ((widgets.Playlist.Count == 0) ? "" : widgets.Playlist[widgets.TrackIndex]);
		if (loadedLocal != Local)
		{
			metadataVersion++;
			if (player != null)
			{
				player.Pause();
			}
			playing = false;
			loadedLocal = Local;
			Artwork = null;
			if (Local)
			{
				Load(autoplay: false);
			}
			else
			{
				systemKey = "";
				stableMetadata = null;
				lastCoverFingerprint = "";
				Poll();
			}
		}
		else if (Local && loadedPath != text)
		{
			Load(autoplay: false);
		}
		Notify();
	}

	private async void Load(bool autoplay)
	{
		int version = ++metadataVersion;
		WidgetOptions widgets = C.Prefs.Widgets;
		loadedLocal = true;
		loadedPath = ((widgets.Playlist.Count == 0) ? "" : widgets.Playlist[widgets.TrackIndex]);
		if (widgets.Playlist.Count == 0)
		{
			if (player != null)
			{
				player.Close();
			}
			Title = "选择本地音乐";
			Artist = "支持批量添加播放列表";
			Artwork = null;
			playing = false;
			opened = false;
			Notify();
			return;
		}
		string path = widgets.Playlist[widgets.TrackIndex];
		try
		{
			opened = false;
			EnsurePlayer();
			player.Open(new Uri(path));
			playing = autoplay;
			if (autoplay)
			{
				player.Play();
			}
			Title = Path.GetFileNameWithoutExtension(path);
			Artist = "本地音乐";
			Album = "";
			Artwork = null;
			Status = "";
			Notify();
			TrackMetadata trackMetadata = await Task.Run(() => TrackMetadata.Read(path));
			if (!stopped && Local && version == metadataVersion)
			{
				Title = trackMetadata.Title;
				Artist = trackMetadata.Artist;
				Album = trackMetadata.Album;
				Artwork = Image(trackMetadata.Cover);
				Notify();
			}
		}
		catch (Exception ex)
		{
			playing = false;
			Status = ex.Message;
			Notify();
		}
	}

	public void Pick(Window owner, bool append = false)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = (append ? "添加到播放列表" : "选择音乐，可多选"),
			Multiselect = true,
			Filter = "音频|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac|所有文件|*.*"
		};
		if (openFileDialog.ShowDialog(owner) == true)
		{
			WidgetOptions widgets = C.Prefs.Widgets;
			bool num = append && Local && widgets.Playlist.Count > 0;
			if (append)
			{
				widgets.Playlist = widgets.Playlist.Concat(openFileDialog.FileNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			}
			else
			{
				widgets.Playlist = openFileDialog.FileNames.ToList();
				widgets.TrackIndex = 0;
			}
			widgets.MusicMode = "local";
			widgets.Normalize();
			C.Save();
			if (num)
			{
				Notify();
			}
			else
			{
				Load(autoplay: true);
			}
		}
	}

	public void Mode(bool local)
	{
		if (Local != local)
		{
			seeks.Clear();
			seekTarget = null;
			metadataVersion++;
			if (player != null)
			{
				player.Pause();
			}
			playing = false;
			C.Prefs.Widgets.MusicMode = (local ? "local" : "system");
			loadedLocal = local;
			C.Save();
			Artwork = null;
			if (local)
			{
				Load(autoplay: false);
			}
			else
			{
				Title = "等待系统音乐";
				Artist = "在支持媒体会话的播放器中开始播放";
				systemKey = "";
				stableMetadata = null;
				lastCoverFingerprint = "";
				Poll();
			}
			Notify();
		}
	}

	public async void Action(string action)
	{
		if (stopped)
		{
			return;
		}
		if (action != "toggle")
		{
			seeks.Clear();
			seekTarget = null;
		}
		try
		{
			if (Local)
			{
				if (action == "toggle")
				{
					if (!opened)
					{
						Load(autoplay: true);
						return;
					}
					if (playing)
					{
						player.Pause();
					}
					else
					{
						player.Play();
					}
					playing = !playing;
				}
				else if (action == "previous")
				{
					WidgetOptions widgets = C.Prefs.Widgets;
					if (widgets.Playlist.Count == 0)
					{
						return;
					}
					widgets.TrackIndex = (widgets.TrackIndex - 1 + widgets.Playlist.Count) % widgets.Playlist.Count;
					C.Save();
					Load(autoplay: true);
				}
				else
				{
					Advance(ended: false);
				}
				Notify();
				return;
			}
			while (polling && !stopped && !Local)
			{
				await Task.Delay(50);
			}
			if (stopped || Local)
			{
				return;
			}
			polling = true;
			try
			{
				bool direct = Session.Backend == "NPSMLib direct";
				MediaSnapshot mediaSnapshot = ((!direct) ? (await bridge.Send(action)) : (await nativeBridge.Send(action, 0.0, Session.Key, Session)));
				MediaSnapshot mediaSnapshot2 = mediaSnapshot;
				if (stopped || Local)
				{
					return;
				}
				if (direct)
				{
					lastDiscovery = DateTime.MinValue;
				}
				Accept(mediaSnapshot2);
				if (!mediaSnapshot2.Available && !direct)
				{
					SystemMedia.Key((ushort)((action == "previous") ? 177 : ((action == "next") ? 176 : 179)));
					Status = "已发送媒体键；系统曲目信息暂不可用";
				}
			}
			finally
			{
				polling = false;
				DrainSeek();
			}
			Notify();
		}
		catch (Exception ex)
		{
			Status = ex.Message;
			Notify();
		}
	}

	private void Advance(bool ended)
	{
		WidgetOptions widgets = C.Prefs.Widgets;
		int num = WidgetRules.NextTrack(widgets.TrackIndex, widgets.Playlist.Count, widgets.RepeatMode, widgets.Shuffle, random.Next(), ended);
		if (num < 0)
		{
			playing = false;
			if (player != null)
			{
				player.Stop();
			}
			Notify();
		}
		else
		{
			widgets.TrackIndex = num;
			C.Save();
			Load(autoplay: true);
		}
	}

	public void Select(int index)
	{
		WidgetOptions widgets = C.Prefs.Widgets;
		if (index >= 0 && index < widgets.Playlist.Count)
		{
			widgets.TrackIndex = index;
			widgets.MusicMode = "local";
			C.Save();
			Load(autoplay: true);
		}
	}

	public void Remove(int index)
	{
		WidgetOptions widgets = C.Prefs.Widgets;
		if (index >= 0 && index < widgets.Playlist.Count)
		{
			bool num = index == widgets.TrackIndex;
			widgets.Playlist.RemoveAt(index);
			if (index < widgets.TrackIndex)
			{
				widgets.TrackIndex--;
			}
			widgets.Normalize();
			C.Save();
			if (num && Local)
			{
				Load(autoplay: false);
			}
			Notify();
		}
	}

	public void Seek(double seconds)
	{
		if (!CanSeek)
		{
			Status = ((Duration > 0.0) ? "播放器未开放进度调整" : "播放器未提供时间轴");
			Notify();
			return;
		}
		seconds = (Local ? LayoutRules.Clamp(seconds, 0.0, Duration, 0.0) : MediaTimeline.ClampSeek(seconds, Session.MinSeek, Session.MaxSeek, Duration));
		if (Local)
		{
			try
			{
				player.Position = TimeSpan.FromSeconds(seconds);
			}
			catch (Exception ex)
			{
				Status = "无法调整播放进度：" + ex.Message;
			}
			Notify();
		}
		else
		{
			seeks.Put(seconds, Session.Key);
			seekTarget = seconds;
			seekUntil = DateTime.UtcNow.AddSeconds(5.0);
			Notify();
			DrainSeek();
		}
	}

	private async void DrainSeek()
	{
		if (stopped || Local || polling || !seeks.Take(Session.Key, out var seconds))
		{
			return;
		}
		string key = Session.Key;
		polling = true;
		try
		{
			bool isolated = Session.Backend == "NPSMLib isolated";
			MediaSnapshot mediaSnapshot = ((!isolated) ? (await bridge.Send("seek", seconds, key)) : (await nativeBridge.Send("seek", seconds, key, Session)));
			MediaSnapshot mediaSnapshot2 = mediaSnapshot;
			if (isolated && mediaSnapshot2 != null && mediaSnapshot2.Available)
			{
				nativeCache = mediaSnapshot2;
				nativeAt = DateTime.UtcNow;
				MediaSnapshot mediaSnapshot3 = MediaFallback.Merge(Session, mediaSnapshot2);
				mediaSnapshot3.Status = mediaSnapshot2.Status;
				mediaSnapshot2 = mediaSnapshot3;
			}
			if (!stopped && !Local)
			{
				bool flag = mediaSnapshot2 != null && mediaSnapshot2.Key == key;
				if (flag && mediaSnapshot2.Available && mediaSnapshot2.Status == "OK")
				{
					Accept(mediaSnapshot2);
					seekTarget = seconds;
					seekUntil = DateTime.UtcNow.AddSeconds(1.5);
					Status = "";
				}
				else
				{
					seekTarget = null;
					Accept(mediaSnapshot2);
					Status = (flag ? "播放器拒绝调整进度" : "曲目或播放器已切换，请重试");
				}
				Notify();
			}
		}
		catch (Exception ex)
		{
			seekTarget = null;
			Status = "无法调整播放进度：" + ex.Message;
			Notify();
		}
		finally
		{
			polling = false;
			if (!stopped && !Local)
			{
				DrainSeek();
			}
		}
	}

	public void SetVolume(double value, bool save = true)
	{
		C.Prefs.Widgets.Volume = LayoutRules.Clamp(value, 0.0, 1.0, 0.6);
		if (player != null)
		{
			player.Volume = C.Prefs.Widgets.Volume;
		}
		if (save)
		{
			C.Save();
		}
		Notify();
	}

	public void SystemVolume(bool up)
	{
		try
		{
			SystemMedia.Key((ushort)(up ? 175 : 174));
		}
		catch (Exception ex)
		{
			Status = ex.Message;
		}
		Notify();
	}

	public void Repeat()
	{
		C.Prefs.Widgets.RepeatMode = (C.Prefs.Widgets.RepeatMode + 1) % 3;
		C.Save();
		Notify();
	}

	public void Shuffle()
	{
		C.Prefs.Widgets.Shuffle = !C.Prefs.Widgets.Shuffle;
		C.Save();
		Notify();
	}

	private MediaSnapshot CachedSupplement()
	{
		if (nativeCache == null)
		{
			return null;
		}
		MediaSnapshot mediaSnapshot = nativeCache.Copy();
		mediaSnapshot.Age += Math.Max(0.0, (DateTime.UtcNow - nativeAt).TotalSeconds);
		return mediaSnapshot;
	}

	private async void Poll()
	{
		if (polling || stopped || Local)
		{
			return;
		}
		polling = true;
		lastPoll = DateTime.UtcNow;
		try
		{
			bool flag = string.IsNullOrEmpty(systemKey) || (DateTime.UtcNow - lastArtworkPoll).TotalSeconds >= 10.0;
			if (flag)
			{
				lastArtworkPoll = DateTime.UtcNow;
			}
			bool primaryDue = DateTime.UtcNow >= nextPrimary;
			MediaSnapshot mediaSnapshot = (primaryDue ? ((await bridge.Send(flag ? "cover" : "refresh")) ?? new MediaSnapshot
			{
				Status = "系统媒体桥未返回数据"
			}) : new MediaSnapshot
			{
				Status = primaryStatus
			});
			MediaSnapshot data = mediaSnapshot;
			if (stopped || Local)
			{
				return;
			}
			primaryStatus = data.Status ?? "未返回状态";
			if (!data.Available || string.IsNullOrWhiteSpace(data.Title))
			{
				if ((DateTime.UtcNow - lastDiscovery).TotalSeconds >= BackgroundWorkRules.MediaPollSeconds(C.MusicVisible, Session.Available, Session.Playing))
				{
					lastDiscovery = DateTime.UtcNow;
					MediaSnapshot mediaSnapshot2 = await nativeBridge.Send("discover");
					if (stopped || Local)
					{
						return;
					}
					nativeStatus = ((mediaSnapshot2 == null) ? "未返回数据" : mediaSnapshot2.Status);
					if (mediaSnapshot2 != null && mediaSnapshot2.Available && !string.IsNullOrWhiteSpace(mediaSnapshot2.Title))
					{
						discovered = mediaSnapshot2;
						discoveredAt = DateTime.UtcNow;
						if (primaryDue)
						{
							nextPrimary = DateTime.UtcNow.AddSeconds(10.0);
						}
					}
					else
					{
						discovered = null;
					}
				}
				if (discovered != null && (DateTime.UtcNow - discoveredAt).TotalSeconds < 4.0)
				{
					data = discovered.Copy();
					data.Age += (DateTime.UtcNow - discoveredAt).TotalSeconds;
				}
				else
				{
					data.Status = "主通道：" + primaryStatus + "；兼容通道：" + nativeStatus;
				}
			}
			else
			{
				discovered = null;
				nextPrimary = DateTime.MinValue;
			}
			bool flag2 = nativeCache != null && nativeCache.Key != data.Key;
			if (flag2)
			{
				nativeCache = null;
			}
			bool flag3 = C.Prefs.Widgets.AutoNativeMediaCompatibility || C.Prefs.Widgets.NativeMediaCompatibility;
			Accept(flag3 ? MediaFallback.Merge(data, CachedSupplement()) : data);
			Notify();
			bool flag4 = data.Available && data.Backend != "NPSMLib direct" && string.IsNullOrEmpty(data.Cover) && Artwork == null;
			if (!(flag3 && flag4) || (!flag2 && !((DateTime.UtcNow - lastNative).TotalSeconds >= 30.0)))
			{
				return;
			}
			lastNative = DateTime.UtcNow;
			MediaSnapshot mediaSnapshot3 = await nativeBridge.Send("cover", 0.0, data.Key, data);
			if (!stopped && !Local && !(Session.Key != data.Key))
			{
				nativeStatus = ((mediaSnapshot3 == null) ? "未返回数据" : (mediaSnapshot3.Available ? (mediaSnapshot3.Status + "；封面：" + mediaSnapshot3.CoverStatus) : mediaSnapshot3.Status));
				if (mediaSnapshot3 != null && mediaSnapshot3.Available && mediaSnapshot3.Key == data.Key)
				{
					nativeCache = mediaSnapshot3;
					nativeAt = DateTime.UtcNow;
					Accept(MediaFallback.Merge(data, mediaSnapshot3));
					Notify();
				}
			}
		}
		catch (Exception ex)
		{
			Status = "音乐读取异常：" + ex.GetBaseException().Message;
			Store.Log(ex);
			Notify();
		}
		finally
		{
			polling = false;
		}
	}

	public string Diagnostics()
	{
		MediaSnapshot session = Session;
		return "GlassBoard v" + Assembly.GetExecutingAssembly().GetName().Version?.ToString() + "\n程序位置：" + Assembly.GetExecutingAssembly().Location + "\n主通道：" + primaryStatus + "\n读取后端：" + session.Backend + "\n播放器：" + session.Source + "\n曲目：" + Title + "\n封面：" + session.CoverStatus + "\n曲目信息：" + session.MetadataStatus + "\n封面补充：" + C.Prefs.Widgets.AutoNativeMediaCompatibility + "\n兼容通道：" + nativeStatus + "\n状态：" + session.Status;
	}

	private void Accept(MediaSnapshot data)
	{
		sessionAt = DateTime.UtcNow;
		Session = MusicMetadataRecovery.Recover(((DateTime.UtcNow - stableMetadataAt).TotalSeconds < 5.0) ? stableMetadata : null, data ?? new MediaSnapshot());
		Status = ((!Session.Available) ? ("系统媒体读取失败 · " + (Session.Status ?? "未获得会话")) : ((Session.Status != "OK") ? "播放器暂不支持此操作" : ""));
		if (!Session.Available)
		{
			if (string.IsNullOrEmpty(systemKey) || !((DateTime.UtcNow - lastAvailable).TotalSeconds < 5.0))
			{
				Title = "等待系统音乐";
				Artist = "开始播放后显示曲名与封面";
				Artwork = null;
				AppIcon = null;
				iconSource = "";
				seekTarget = null;
				systemKey = "";
			}
			return;
		}
		lastAvailable = DateTime.UtcNow;
		if (Session.MetadataStatus == "OK")
		{
			stableMetadata = Session.Copy();
			stableMetadataAt = DateTime.UtcNow;
		}
		Title = (string.IsNullOrWhiteSpace(Session.Title) ? "正在播放" : Session.Title);
		Artist = (string.IsNullOrWhiteSpace(Session.Artist) ? "系统播放器" : Session.Artist);
		Album = Session.Album ?? "";
		if (!string.IsNullOrEmpty(Session.MetadataStatus) && Session.MetadataStatus != "OK")
		{
			Status = "曲目信息读取失败 · " + Session.MetadataStatus;
		}
		if (Session.Key != systemKey)
		{
			systemKey = Session.Key;
			Artwork = artworkCache.Get(systemKey, DateTime.UtcNow);
			lastCoverFingerprint = "";
			seekTarget = null;
		}
		if (iconSource != (Session.Source ?? ""))
		{
			iconSource = Session.Source ?? "";
			AppIcon = null;
			LoadAppIcon(iconSource);
		}
		if (string.IsNullOrEmpty(Session.Cover))
		{
			return;
		}
		string text = CoverFingerprint.Of(Session.Cover);
		if (!(text != lastCoverFingerprint))
		{
			return;
		}
		try
		{
			ImageSource imageSource = Image(Convert.FromBase64String(Session.Cover));
			if (imageSource != null)
			{
				Artwork = imageSource;
				lastCoverFingerprint = text;
				artworkCache.Put(systemKey, imageSource, DateTime.UtcNow);
			}
		}
		catch
		{
		}
	}

	private async void LoadAppIcon(string source)
	{
		if (string.IsNullOrWhiteSpace(source))
		{
			return;
		}
		string text = await Task.Run(delegate
		{
			string result = null;
			int num = 0;
			Process[] processes = Process.GetProcesses();
			foreach (Process process in processes)
			{
				try
				{
					string processName = process.ProcessName;
					if (processName.Length >= 3 && processName.Length > num && source.IndexOf(processName, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						string fileName = process.MainModule.FileName;
						if (File.Exists(fileName))
						{
							result = fileName;
							num = processName.Length;
						}
					}
				}
				catch
				{
				}
				finally
				{
					process.Dispose();
				}
			}
			return result;
		});
		if (stopped || Local || iconSource != source)
		{
			return;
		}
		try
		{
			if (text != null)
			{
				AppIcon = Native.Icon(text);
			}
		}
		catch
		{
		}
		Notify();
	}

	public static ImageSource Image(byte[] bytes)
	{
		if (bytes == null || bytes.Length == 0)
		{
			return null;
		}
		try
		{
			return ThumbnailImages.Read(bytes, 600);
		}
		catch
		{
			return null;
		}
	}

	public void Dispose()
	{
		stopped = true;
		metadataVersion++;
		timer.Stop();
		nativeBridge.Dispose();
		try
		{
			if (player != null)
			{
				player.Close();
			}
		}
		finally
		{
			bridge.Dispose();
			this.Changed = null;
		}
	}
}
