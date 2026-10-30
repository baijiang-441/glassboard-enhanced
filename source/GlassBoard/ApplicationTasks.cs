using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GlassBoard;

// Read-only access to persisted Shell links; never rebuilds another app's Jump List.
public sealed class ApplicationTask
{
    public string Title, Path, Arguments, WorkingDirectory, IconPath;
    public int IconIndex;
    public void Launch()
    {
        Process.Start(new ProcessStartInfo(Path, Arguments ?? "") {
            UseShellExecute = true, WorkingDirectory = WorkingDirectory ?? "" });
    }
}

public static class ApplicationTasks
{
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr data, uint flags);
        void GetIDList(out IntPtr list); void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short value); void SetHotkey(short value);
        void GetShowCmd(out int value); void SetShowCmd(int value);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string text, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
    [ComImport, Guid("00000109-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistStream
    {
        void GetClassID(out Guid id);
        [PreserveSig] int IsDirty();
        void Load(IStream stream); void Save(IStream stream, bool clear); void GetSizeMax(out long size);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Value; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count); void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out Variant value);
        void SetValue(ref PropertyKey key, ref Variant value); void Commit();
    }
    [DllImport("shlwapi.dll")] private static extern IStream SHCreateMemStream(byte[] bytes, uint size);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Variant value);
    [DllImport("propsys.dll", CharSet = CharSet.Unicode)] private static extern int PropVariantToStringAlloc(ref Variant value, out IntPtr text);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);

    public static bool CloseWindow(RunningWindow window)
    {
        if (window == null || !IsWindow(window.Handle)) return false;
        GetWindowThreadProcessId(window.Handle, out uint process);
        // Prevent a stale handle from closing a different application's new window.
        return process == window.ProcessId && PostMessage(window.Handle, 0x10, IntPtr.Zero, IntPtr.Zero);
    }
    public static void OpenTaskManager() { Process.Start(new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "Taskmgr.exe")) { UseShellExecute = true }); }
    private static readonly byte[] Header = { 0x4c,0,0,0,1,0x14,2,0,0,0,0,0,0xc0,0,0,0,0,0,0,0x46 };

    // Decode each embedded link with Windows' own ShellLink parser. Ignore malformed records.
    public static List<ApplicationTask> ReadFile(string file)
    {
        var result = new List<ApplicationTask>();
        var info = new FileInfo(file);
        if (!info.Exists || info.Length > 4 * 1024 * 1024) return result;
        byte[] data;
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var memory = new MemoryStream()) { stream.CopyTo(memory); data = memory.ToArray(); }
        var offsets = new List<int>();
        for (int i = 0; i <= data.Length - Header.Length; i++) {
            if (data[i] != Header[0]) continue;
            bool match = true; for (int j = 1; j < Header.Length; j++) if (data[i+j] != Header[j]) { match=false; break; }
            if (match) offsets.Add(i);
        }
        for (int i = 0; i < offsets.Count && i < 128; i++) {
            object link = null; IStream stream = null;
            try {
                int end = i+1 < offsets.Count ? offsets[i+1] : data.Length;
                var bytes = new byte[end - offsets[i]]; Buffer.BlockCopy(data, offsets[i], bytes, 0, bytes.Length);
                stream = SHCreateMemStream(bytes, (uint)bytes.Length); if (stream == null) continue;
                link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")));
                ((IPersistStream)link).Load(stream);
                var shell = (IShellLink)link;
                var key = new PropertyKey { Format = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), Id=2 };
                ((IPropertyStore)link).GetValue(ref key, out var value);
                string title = null;
                try { if (PropVariantToStringAlloc(ref value, out var text) >= 0 && text != IntPtr.Zero) { try { title=Marshal.PtrToStringUni(text); } finally { Marshal.FreeCoTaskMem(text); } } }
                finally { PropVariantClear(ref value); }
                if (string.IsNullOrWhiteSpace(title)) continue; // separators and untitled destinations
                var path = new StringBuilder(32768); var args = new StringBuilder(32768); var directory = new StringBuilder(32768); var icon = new StringBuilder(32768);
                shell.GetPath(path,path.Capacity,IntPtr.Zero,4); shell.GetArguments(args,args.Capacity);
                shell.GetWorkingDirectory(directory,directory.Capacity); shell.GetIconLocation(icon,icon.Capacity,out var iconIndex);
                string target=Environment.ExpandEnvironmentVariables(path.ToString());
                if (string.IsNullOrWhiteSpace(target)) continue;
                result.Add(new ApplicationTask {Title=title,Path=target,Arguments=args.ToString(),WorkingDirectory=directory.ToString(),IconPath=Environment.ExpandEnvironmentVariables(icon.ToString()),IconIndex=iconIndex});
            } catch { }
            finally { if(link!=null)Marshal.ReleaseComObject(link); if(stream!=null)Marshal.ReleaseComObject(stream); }
        }
        return result;
    }
    public static Task<List<ApplicationTask>> ReadAsync(string application)
    {
        // Shell COM objects are created and released on a dedicated STA, off the UI thread.
        var completion = new TaskCompletionSource<List<ApplicationTask>>();
        var thread = new Thread(() => {
            var result = new List<ApplicationTask>();
            try {
                if (!string.IsNullOrWhiteSpace(application)) {
                    string folder=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft", "Windows", "Recent", "CustomDestinations");
                    foreach(var file in Directory.EnumerateFiles(folder,"*.customDestinations-ms").OrderByDescending(File.GetLastWriteTimeUtc).Take(256)) {
                        try { foreach(var task in ReadFile(file)) if (string.Equals(task.Path,application,StringComparison.OrdinalIgnoreCase) && !result.Any(t=>t.Title==task.Title && t.Arguments==task.Arguments)) result.Add(task); } catch { }
                    }
                }
            } catch { }
            completion.SetResult(result.Take(24).ToList());
        }) { IsBackground=true, Name="GlassBoard application tasks" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
