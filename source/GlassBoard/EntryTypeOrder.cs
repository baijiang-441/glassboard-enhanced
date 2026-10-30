using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GlassBoard;

public static class EntryTypeOrder
{
 public static int Category(Entry entry)
 {
  if (DesktopEntries.IsShell(entry.Path)) return 0;
  if (entry.IsFolder || Directory.Exists(entry.Path)) return 1;
  string ext = System.IO.Path.GetExtension(entry.Path ?? "").ToLowerInvariant();
  if (new[]{".exe",".com",".appref-ms",".msi",".msix"}.Contains(ext)) return 2;
  if (new[]{".lnk",".url"}.Contains(ext)) return 3;
  if (new[]{".bat",".cmd",".ps1",".vbs",".js",".py",".sh"}.Contains(ext)) return 4;
  if (new[]{".txt",".md",".pdf",".doc",".docx",".xls",".xlsx",".ppt",".pptx",".csv",".rtf",".odt",".ods"}.Contains(ext)) return 5;
  if (new[]{".png",".jpg",".jpeg",".gif",".bmp",".webp",".svg",".ico",".heic"}.Contains(ext)) return 6;
  if (new[]{".mp3",".wav",".flac",".aac",".m4a",".ogg"}.Contains(ext)) return 7;
  if (new[]{".mp4",".mkv",".avi",".mov",".webm",".wmv"}.Contains(ext)) return 8;
  if (new[]{".zip",".7z",".rar",".tar",".gz"}.Contains(ext)) return 9;
  return 10;
 }
 public static List<Entry> Arrange(IEnumerable<Entry> entries) => entries.OrderBy(Category).ThenBy(a=>a.Name ?? "",StringComparer.CurrentCultureIgnoreCase).ToList();
}
