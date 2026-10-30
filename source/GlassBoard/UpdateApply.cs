using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GlassBoard;

public static class UpdateApply
{
	public static Dictionary<string, string> Extract(byte[] bytes, string stage, Version version)
	{
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected O, but got Unknown
		Directory.CreateDirectory(stage);
		Dictionary<string, byte[]> data = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
		long num = 0L;
		using (MemoryStream memoryStream = new MemoryStream(bytes, writable: false))
		{
			ZipArchive val = new ZipArchive((Stream)memoryStream, (ZipArchiveMode)0);
			try
			{
				if (val.Entries.Count > 4096)
				{
					throw new InvalidDataException("更新包文件数量异常");
				}
				foreach (ZipArchiveEntry entry in val.Entries)
				{
					string fullName = entry.FullName;
					if (fullName.EndsWith("/"))
					{
						continue;
					}
					if (!fullName.StartsWith("GlassBoard/", StringComparison.Ordinal))
					{
						throw new InvalidDataException("不是 GlassBoard 用户版");
					}
					fullName = fullName.Substring(11);
					if (!UpdateRules.Allowed(fullName) || data.ContainsKey(fullName) || entry.Length > 201326592 || num + entry.Length > 536870912)
					{
						throw new InvalidDataException("更新包包含不支持或重复的文件：" + fullName);
					}
					num += entry.Length;
					using Stream stream = entry.Open();
					using MemoryStream memoryStream2 = new MemoryStream();
					byte[] array = new byte[8192];
					int num2;
					while ((num2 = stream.Read(array, 0, array.Length)) > 0)
					{
						if (memoryStream2.Length + num2 > entry.Length)
						{
							throw new InvalidDataException("更新文件大小异常");
						}
						memoryStream2.Write(array, 0, num2);
					}
					data.Add(fullName, memoryStream2.ToArray());
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		if (!data.ContainsKey("GlassBoard.exe") || !data.ContainsKey("SHA256SUMS.txt"))
		{
			throw new InvalidDataException("更新包缺少主程序或校验清单");
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string[] array2 = Encoding.UTF8.GetString(data["SHA256SUMS.txt"]).TrimStart('\ufeff').Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array2.Length; i++)
		{
			Match match = Regex.Match(array2[i], "^([a-fA-F0-9]{64})  (.+)$");
			if (!match.Success || !UpdateRules.Allowed(match.Groups[2].Value) || dictionary.ContainsKey(match.Groups[2].Value))
			{
				throw new InvalidDataException("更新包校验清单异常");
			}
			dictionary.Add(match.Groups[2].Value, match.Groups[1].Value);
		}
		foreach (KeyValuePair<string, byte[]> item in data)
		{
			if (item.Key != "SHA256SUMS.txt" && (!dictionary.TryGetValue(item.Key, out var value) || !string.Equals(UpdateRules.Hash(item.Value), value, StringComparison.OrdinalIgnoreCase)))
			{
				throw new InvalidDataException("更新文件校验失败：" + item.Key);
			}
		}
		if (dictionary.Keys.Any((string k) => !data.ContainsKey(k)))
		{
			throw new InvalidDataException("更新包文件不完整");
		}
		foreach (KeyValuePair<string, byte[]> item2 in data)
		{
			string path = Safe(stage, item2.Key);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllBytes(path, item2.Value);
		}
		if (!Version.TryParse(FileVersionInfo.GetVersionInfo(Safe(stage, "GlassBoard.exe")).FileVersion, out var result) || result != version)
		{
			throw new InvalidDataException("主程序版本与更新包名称不一致");
		}
		return data.ToDictionary((KeyValuePair<string, byte[]> item) => item.Key, (KeyValuePair<string, byte[]> item) => UpdateRules.Hash(item.Value), StringComparer.OrdinalIgnoreCase);
	}

	public static string Safe(string root, string name)
	{
		if (!UpdateRules.Allowed(name))
		{
			throw new InvalidDataException("不支持的更新文件");
		}
		string fullPath = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
		string value = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("更新路径越界");
		}
		for (string directoryName = Path.GetDirectoryName(fullPath); directoryName != null; directoryName = Path.GetDirectoryName(directoryName))
		{
			if (Directory.Exists(directoryName) && (File.GetAttributes(directoryName) & FileAttributes.ReparsePoint) != 0)
			{
				throw new IOException("更新目录包含链接，请手动解压新版");
			}
			if (string.Equals(directoryName, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
			{
				break;
			}
		}
		try
		{
			if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
			{
				throw new IOException("更新文件是链接，请手动更新");
			}
		}
		catch (FileNotFoundException)
		{
		}
		catch (DirectoryNotFoundException)
		{
		}
		return fullPath;
	}

	public static void Install(UpdatePlan plan, Action<string> afterFile = null)
	{
		if (plan.Files == null || plan.Files.Count == 0 || plan.Files.Count > 4096 || !plan.Files.ContainsKey("GlassBoard.exe"))
		{
			throw new InvalidDataException("更新计划异常");
		}
		if (string.Equals(Path.GetFullPath(plan.Target), Path.GetFullPath(plan.Stage), StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFullPath(plan.Target), Path.GetFullPath(plan.Backup), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("更新目录冲突");
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> list = new List<string>();
		foreach (KeyValuePair<string, string> file in plan.Files)
		{
			string path = Safe(plan.Stage, file.Key);
			string text = Safe(plan.Target, file.Key);
			string text2 = Safe(plan.Backup, file.Key);
			if (!File.Exists(path) || !string.Equals(UpdateRules.Hash(File.ReadAllBytes(path)), file.Value, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("暂存更新文件校验失败");
			}
			if (File.Exists(text))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(text2));
				File.Copy(text, text2, overwrite: false);
				hashSet.Add(file.Key);
			}
		}
		try
		{
			foreach (KeyValuePair<string, string> item in plan.Files.OrderBy((KeyValuePair<string, string> p) => (p.Key == "GlassBoard.exe") ? 1 : 0))
			{
				string text3 = Safe(plan.Target, item.Key);
				Directory.CreateDirectory(Path.GetDirectoryName(text3));
				list.Add(item.Key);
				File.Copy(Safe(plan.Stage, item.Key), text3, overwrite: true);
				afterFile?.Invoke(item.Key);
			}
		}
		catch (Exception innerException)
		{
			List<string> list2 = new List<string>();
			foreach (string item2 in list.AsEnumerable().Reverse())
			{
				try
				{
					string text4 = Safe(plan.Target, item2);
					if (hashSet.Contains(item2))
					{
						File.Copy(Safe(plan.Backup, item2), text4, overwrite: true);
					}
					else if (File.Exists(text4))
					{
						File.Delete(text4);
					}
				}
				catch
				{
					list2.Add(item2);
				}
			}
			if (list2.Count > 0)
			{
				throw new IOException("自动恢复未完成，备份保留在 " + plan.Backup + "；文件：" + string.Join(",", list2), innerException);
			}
			throw new IOException("更新失败，已恢复原程序；备份保留在 " + plan.Backup, innerException);
		}
	}
}
