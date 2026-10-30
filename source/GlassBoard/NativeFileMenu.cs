using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace GlassBoard;

// The menu and its verbs are supplied by Windows Shell, including installed extensions.
public sealed class NativeFileMenu : IDisposable
{
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(UIntPtr offset, uint flags, IntPtr reserved, IntPtr text, uint length);
    }
    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(UIntPtr offset, uint flags, IntPtr reserved, IntPtr text, uint length);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr w, IntPtr l);
    }
    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(UIntPtr offset, uint flags, IntPtr reserved, IntPtr text, uint length);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr w, IntPtr l);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr w, IntPtr l, out IntPtr result);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InvokeInfo
    {
        public int Size; public uint Mask; public IntPtr Window;
        public IntPtr Verb, Parameters, Directory;
        public int Show; public uint Hotkey; public IntPtr Icon;
        public IntPtr Title, VerbW, ParametersW, DirectoryW, TitleW;
        public int X, Y;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, out IShellItem item);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string label);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(IntPtr menu, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuString(IntPtr menu, uint index, StringBuilder text, int length, uint flags);

    private IShellItem item;
    private IContextMenu context;
    private IContextMenu2 context2;
    private IContextMenu3 context3;
    private IntPtr menu;
    private const uint Manage = 0x7000;

    public NativeFileMenu(string path)
    {
        try
        {
            Guid iid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item);
            Guid handler = new Guid("3981E225-F559-11D3-8E3A-00C04F6837D5");
            iid = typeof(IContextMenu).GUID;
            item.BindToHandler(IntPtr.Zero, ref handler, ref iid, out object value);
            context = (IContextMenu)value;
            context2 = value as IContextMenu2;
            context3 = value as IContextMenu3;
            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) throw new InvalidOperationException("无法创建系统菜单。");
            Marshal.ThrowExceptionForHR(context.QueryContextMenu(menu, 0, 1, Manage - 1, 0x10));
            AppendMenu(menu, 0x800, UIntPtr.Zero, null);
            AppendMenu(menu, 0, new UIntPtr(Manage), "GlassBoard 管理…");
        }
        catch { Dispose(); throw; }
    }

    private string Verb(uint id)
    {
        IntPtr buffer = Marshal.AllocCoTaskMem(1024);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            int hr = context.GetCommandString(new UIntPtr(id - 1), 4, IntPtr.Zero, buffer, 512);
            return hr >= 0 ? Marshal.PtrToStringUni(buffer) : "";
        }
        finally { Marshal.FreeCoTaskMem(buffer); }
    }

    public List<string> Commands()
    {
        var result = new List<string>();
        for (int i = 0; i < GetMenuItemCount(menu); i++)
        {
            var text = new StringBuilder(512);
            GetMenuString(menu, (uint)i, text, text.Capacity, 0x400);
            uint id = GetMenuItemID(menu, i);
            result.Add(text + "|" + (id > 0 && id < Manage ? Verb(id) : ""));
        }
        return result;
    }

    // Returns a host action; all other verbs execute through Windows Shell.
    public string Show(Window owner)
    {
        IntPtr handle = new WindowInteropHelper(owner).EnsureHandle();
        HwndSource source = HwndSource.FromHwnd(handle);
        HwndSourceHook hook = (IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (message == 0x117 || message == 0x2B || message == 0x2C || message == 0x120)
            {
                if (context3 != null && context3.HandleMenuMsg2((uint)message, w, l, out IntPtr value) == 0)
                { handled = true; return value; }
                if (context2 != null && message != 0x120 && context2.HandleMenuMsg((uint)message, w, l) == 0)
                { handled = true; return message == 0x2B || message == 0x2C ? new IntPtr(1) : IntPtr.Zero; }
            }
            return IntPtr.Zero;
        };
        source.AddHook(hook);
        try
        {
            GetCursorPos(out Point point);
            uint command = TrackPopupMenuEx(menu, 0x100 | 0x2, point.X, point.Y, handle, IntPtr.Zero);
            if (command == 0) return null;
            if (command == Manage) return "manage";
            string verb = Verb(command);
            if (string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase)) return "rename";
            var info = new InvokeInfo {
                Size = Marshal.SizeOf(typeof(InvokeInfo)), Mask = 0x4000 | 0x20000000 | 0x100,
                Window = handle, Verb = new IntPtr(command - 1), VerbW = new IntPtr(command - 1),
                Show = 1, X = point.X, Y = point.Y
            };
            Marshal.ThrowExceptionForHR(context.InvokeCommand(ref info));
            return verb;
        }
        finally { source.RemoveHook(hook); PostMessage(handle, 0, IntPtr.Zero, IntPtr.Zero); }
    }

    public void Dispose()
    {
        if (menu != IntPtr.Zero) { DestroyMenu(menu); menu = IntPtr.Zero; }
        context2 = null; context3 = null;
        if (context != null) { Marshal.ReleaseComObject(context); context = null; }
        if (item != null) { Marshal.ReleaseComObject(item); item = null; }
    }
}
