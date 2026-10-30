using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GlassBoard;

public static class DesktopEntries
{
 public const string Computer = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
 public const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";
 public static bool IsShell(string path) => path == Computer || path == RecycleBin;
 public static List<Entry> Scan(IEnumerable<string> roots, bool systemIcons = true)
 {
  var result = new List<Entry>();
  foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase)) {
   if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
   try {
    foreach (string path in Directory.GetFileSystemEntries(root)) {
     try {
      var attr = File.GetAttributes(path);
      if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0 || Path.GetFileName(path).Equals("desktop.ini",StringComparison.OrdinalIgnoreCase)) continue;
      result.Add(new Entry { Name = EntryPathRules.Name(path), Path = EntryPathRules.Normalize(path), IsFolder = (attr & FileAttributes.Directory) != 0 });
     } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
   } catch (IOException) { } catch (UnauthorizedAccessException) { }
  }
  if (systemIcons) {
   result.Add(new Entry { Name="此电脑", Path=Computer, IsFolder=true });
   result.Add(new Entry { Name="回收站", Path=RecycleBin, IsFolder=true });
  }
  return result;
 }
 public static int Merge(Preferences prefs, IEnumerable<Entry> desktop, bool includeRemoved)
 {
  var known = new HashSet<string>(prefs.Apps.Select(a=>a.Path),StringComparer.OrdinalIgnoreCase);
  int added = 0;
  foreach (var entry in desktop) {
   if (!known.Add(entry.Path)) continue;
   if (!IsShell(entry.Path)) LaunchInfoService.Enrich(entry);
   if (!includeRemoved && RemovalRules.Blocked(prefs,entry)) continue;
   RemovalRules.Forget(prefs,entry);
   prefs.Apps.Add(entry); added++;
  }
  return added;
 }
}
