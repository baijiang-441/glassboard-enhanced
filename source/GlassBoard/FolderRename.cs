using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GlassBoard;

public static class FolderRename
{
    public static string Destination(string source, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.EndsWith(".") ||
            name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Length > 255 || Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])($|\.)", RegexOptions.IgnoreCase))
            throw new ArgumentException("请输入有效文件夹名称，不能包含路径、末尾空格、末尾句点或系统保留名称。");
        source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        string parent = Path.GetDirectoryName(source);
        if (string.IsNullOrEmpty(parent) || (!Directory.Exists(source) && !File.Exists(source)))
            throw new IOException("文件夹不存在，或不能重命名磁盘根目录。");
        string target = Path.Combine(parent, name);
        if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase) && (Directory.Exists(target) || File.Exists(target)))
            throw new IOException("同一位置已存在这个名称，请换一个名称。");
        return target;
    }

    private static string Map(string path, string before, string after)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (string.Equals(path.TrimEnd('\\'), before, StringComparison.OrdinalIgnoreCase)) return after;
        if (path.StartsWith(before + "\\", StringComparison.OrdinalIgnoreCase)) return after + path.Substring(before.Length);
        return path;
    }

    public static Preferences Remap(Preferences original, string before, string after)
    {
        Preferences copy;
        using (var stream = new MemoryStream())
        {
            var serializer = new DataContractJsonSerializer(typeof(Preferences));
            serializer.WriteObject(stream, original);
            stream.Position = 0;
            copy = (Preferences)serializer.ReadObject(stream);
        }
        foreach (Entry entry in copy.Apps)
        {
            bool root = string.Equals(entry.Path.TrimEnd('\\'), before, StringComparison.OrdinalIgnoreCase);
            entry.Path = Map(entry.Path, before, after);
            entry.LaunchTarget = Map(entry.LaunchTarget, before, after);
            entry.IconPath = Map(entry.IconPath, before, after);
            entry.WindowTarget = Map(entry.WindowTarget, before, after);
            if (root) entry.Name = Path.GetFileName(after);
        }
        Func<List<string>, List<string>> mapList = list => list == null ? null : list.Select(path => Map(path, before, after)).ToList();
        copy.DockOrder = mapList(copy.DockOrder);
        copy.Removed = mapList(copy.Removed);
        if (copy.Dock != null)
        {
            copy.Dock.ItemOrder = mapList(copy.Dock.ItemOrder);
            copy.Dock.RunningOrder = mapList(copy.Dock.RunningOrder);
        }
        foreach (Workspace workspace in copy.Workspaces ?? new List<Workspace>()) workspace.Paths = mapList(workspace.Paths);
        return copy;
    }

    private static void MoveEntry(string source, string target)
    {
        if (Directory.Exists(source)) Directory.Move(source, target); else File.Move(source, target);
    }

    private static void Move(string source, string target)
    {
        if (source == target) return;
        if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) { MoveEntry(source, target); return; }
        string temp = Path.Combine(Path.GetDirectoryName(source), ".glassboard-rename-" + Guid.NewGuid().ToString("N"));
        MoveEntry(source, temp);
        try { MoveEntry(temp, target); }
        catch { MoveEntry(temp, source); throw; }
    }

    public static async Task<Preferences> RenameAsync(Preferences original, string source, string name, Action<Preferences> persist)
    {
        source = Path.GetFullPath(source).TrimEnd('\\');
        string target = Destination(source, name);
        if (source == target) return original;
        Preferences candidate = Remap(original, source, target);
        await Task.Run(() => Move(source, target));
        try { persist(candidate); }
        catch (Exception saveError)
        {
            try { await Task.Run(() => Move(target, source)); }
            catch (Exception rollbackError)
            {
                throw new IOException("文件夹已改为「" + target + "」，但配置保存和名称恢复失败。请勿重复改名。", new AggregateException(saveError, rollbackError));
            }
            throw new IOException("配置保存失败，文件夹已恢复原名。", saveError);
        }
        return candidate;
    }
}
