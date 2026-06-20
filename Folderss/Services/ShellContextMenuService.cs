using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace Folderss.Services
{
    public static class ShellContextMenuService
    {
        private const uint CmdFirst = 1;
        private const uint CmdLast = 0x7FFF;
        private const uint CmfNormal = 0;
        private const uint TpmReturnCmd = 0x0100;
        private const uint TpmRightButton = 0x0002;
        private const int CmInvokeUnicode = 0x00004000;
        private const int SwShowNormal = 1;
        private const int WmInitMenuPopup = 0x0117;
        private const int WmDrawItem = 0x002B;
        private const int WmMeasureItem = 0x002C;
        private const int WmMenuChar = 0x0120;

        public static void Show(IntPtr ownerHandle, IEnumerable<string> paths, int x, int y)
        {
            var selectedPaths = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selectedPaths.Count == 0)
                return;

            var parentPath = Path.GetDirectoryName(selectedPaths[0]);
            if (string.IsNullOrWhiteSpace(parentPath) ||
                selectedPaths.Any(path => !string.Equals(Path.GetDirectoryName(path), parentPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("같은 폴더에 있는 항목만 함께 선택할 수 있습니다.");
            }

            IShellFolder desktopFolder = null;
            IShellFolder parentFolder = null;
            IContextMenu contextMenu = null;
            var parentPidl = IntPtr.Zero;
            var childPidls = new List<IntPtr>();
            var menuHandle = IntPtr.Zero;

            try
            {
                ThrowIfFailed(SHGetDesktopFolder(out desktopFolder));

                uint eaten;
                uint attributes = 0;
                ThrowIfFailed(desktopFolder.ParseDisplayName(
                    ownerHandle,
                    IntPtr.Zero,
                    parentPath,
                    out eaten,
                    out parentPidl,
                    ref attributes));

                var shellFolderId = typeof(IShellFolder).GUID;
                object parentObject;
                ThrowIfFailed(desktopFolder.BindToObject(parentPidl, IntPtr.Zero, ref shellFolderId, out parentObject));
                parentFolder = (IShellFolder)parentObject;

                foreach (var path in selectedPaths)
                {
                    IntPtr childPidl;
                    attributes = 0;
                    ThrowIfFailed(parentFolder.ParseDisplayName(
                        ownerHandle,
                        IntPtr.Zero,
                        Path.GetFileName(path),
                        out eaten,
                        out childPidl,
                        ref attributes));
                    childPidls.Add(childPidl);
                }

                var contextMenuId = typeof(IContextMenu).GUID;
                IntPtr contextMenuPointer;
                ThrowIfFailed(parentFolder.GetUIObjectOf(
                    ownerHandle,
                    (uint)childPidls.Count,
                    childPidls.ToArray(),
                    ref contextMenuId,
                    IntPtr.Zero,
                    out contextMenuPointer));

                try
                {
                    contextMenu = (IContextMenu)Marshal.GetTypedObjectForIUnknown(contextMenuPointer, typeof(IContextMenu));
                }
                finally
                {
                    Marshal.Release(contextMenuPointer);
                }

                menuHandle = CreatePopupMenu();
                if (menuHandle == IntPtr.Zero)
                    throw new InvalidOperationException("Windows 컨텍스트 메뉴를 만들지 못했습니다.");

                ThrowIfFailed(contextMenu.QueryContextMenu(menuHandle, 0, CmdFirst, CmdLast, CmfNormal));

                using (var messageForwarder = new MenuMessageForwarder(ownerHandle, contextMenu))
                {
                    var command = TrackPopupMenuEx(
                        menuHandle,
                        TpmReturnCmd | TpmRightButton,
                        x,
                        y,
                        ownerHandle,
                        IntPtr.Zero);

                    if (command >= CmdFirst)
                        InvokeCommand(contextMenu, ownerHandle, command - CmdFirst, parentPath);
                }
            }
            finally
            {
                if (menuHandle != IntPtr.Zero)
                    DestroyMenu(menuHandle);

                foreach (var childPidl in childPidls)
                {
                    if (childPidl != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(childPidl);
                }

                if (parentPidl != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(parentPidl);

                ReleaseComObject(contextMenu);
                ReleaseComObject(parentFolder);
                ReleaseComObject(desktopFolder);
            }
        }

        private static void InvokeCommand(IContextMenu contextMenu, IntPtr ownerHandle, uint commandOffset, string directory)
        {
            var invoke = new CmInvokeCommandInfoEx
            {
                Size = Marshal.SizeOf(typeof(CmInvokeCommandInfoEx)),
                Mask = CmInvokeUnicode,
                Owner = ownerHandle,
                Verb = new IntPtr(commandOffset),
                VerbW = new IntPtr(commandOffset),
                Directory = directory,
                DirectoryW = directory,
                Show = SwShowNormal,
                InvokePoint = new Point()
            };

            ThrowIfFailed(contextMenu.InvokeCommand(ref invoke));
        }

        private static void ThrowIfFailed(int result)
        {
            if (result < 0)
                Marshal.ThrowExceptionForHR(result);
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }

        private sealed class MenuMessageForwarder : IDisposable
        {
            private readonly HwndSource _source;
            private readonly IContextMenu2 _menu2;
            private readonly IContextMenu3 _menu3;

            public MenuMessageForwarder(IntPtr ownerHandle, IContextMenu contextMenu)
            {
                _source = HwndSource.FromHwnd(ownerHandle);
                _menu3 = contextMenu as IContextMenu3;
                _menu2 = contextMenu as IContextMenu2;

                if (_source != null)
                    _source.AddHook(WndProc);
            }

            public void Dispose()
            {
                if (_source != null)
                    _source.RemoveHook(WndProc);
            }

            private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (message != WmInitMenuPopup && message != WmDrawItem &&
                    message != WmMeasureItem && message != WmMenuChar)
                {
                    return IntPtr.Zero;
                }

                if (_menu3 != null)
                {
                    IntPtr result;
                    var hr = _menu3.HandleMenuMsg2((uint)message, wParam, lParam, out result);
                    handled = hr >= 0;
                    return result;
                }

                if (_menu2 != null)
                {
                    var hr = _menu2.HandleMenuMsg((uint)message, wParam, lParam);
                    handled = hr >= 0;
                }

                return IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CmInvokeCommandInfoEx
        {
            public int Size;
            public int Mask;
            public IntPtr Owner;
            public IntPtr Verb;
            [MarshalAs(UnmanagedType.LPStr)]
            public string Parameters;
            [MarshalAs(UnmanagedType.LPStr)]
            public string Directory;
            public int Show;
            public int HotKey;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.LPStr)]
            public string Title;
            public IntPtr VerbW;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string ParametersW;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string DirectoryW;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string TitleW;
            public Point InvokePoint;
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214E6-0000-0000-C000-000000000046")]
        private interface IShellFolder
        {
            [PreserveSig]
            int ParseDisplayName(
                IntPtr hwnd,
                IntPtr bindContext,
                [MarshalAs(UnmanagedType.LPWStr)] string displayName,
                out uint eaten,
                out IntPtr itemIdList,
                ref uint attributes);

            [PreserveSig]
            int EnumObjects(IntPtr hwnd, int flags, out IntPtr enumIdList);

            [PreserveSig]
            int BindToObject(
                IntPtr itemIdList,
                IntPtr bindContext,
                ref Guid interfaceId,
                [MarshalAs(UnmanagedType.Interface)] out object result);

            [PreserveSig]
            int BindToStorage(IntPtr itemIdList, IntPtr bindContext, ref Guid interfaceId, out IntPtr result);

            [PreserveSig]
            int CompareIDs(IntPtr lParam, IntPtr firstIdList, IntPtr secondIdList);

            [PreserveSig]
            int CreateViewObject(IntPtr hwndOwner, ref Guid interfaceId, out IntPtr result);

            [PreserveSig]
            int GetAttributesOf(uint count, IntPtr[] itemIdLists, ref uint attributes);

            [PreserveSig]
            int GetUIObjectOf(
                IntPtr hwndOwner,
                uint count,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] itemIdLists,
                ref Guid interfaceId,
                IntPtr reserved,
                out IntPtr result);

            [PreserveSig]
            int GetDisplayNameOf(IntPtr itemIdList, uint flags, out IntPtr name);

            [PreserveSig]
            int SetNameOf(IntPtr hwnd, IntPtr itemIdList, string name, uint flags, out IntPtr renamedIdList);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214E4-0000-0000-C000-000000000046")]
        private interface IContextMenu
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr menu, uint index, uint commandFirst, uint commandLast, uint flags);

            [PreserveSig]
            int InvokeCommand(ref CmInvokeCommandInfoEx invokeInfo);

            [PreserveSig]
            int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, StringBuilder name, uint maxName);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F4-0000-0000-C000-000000000046")]
        private interface IContextMenu2
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr menu, uint index, uint commandFirst, uint commandLast, uint flags);

            [PreserveSig]
            int InvokeCommand(ref CmInvokeCommandInfoEx invokeInfo);

            [PreserveSig]
            int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, StringBuilder name, uint maxName);

            [PreserveSig]
            int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
        private interface IContextMenu3
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr menu, uint index, uint commandFirst, uint commandLast, uint flags);

            [PreserveSig]
            int InvokeCommand(ref CmInvokeCommandInfoEx invokeInfo);

            [PreserveSig]
            int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, StringBuilder name, uint maxName);

            [PreserveSig]
            int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);

            [PreserveSig]
            int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetDesktopFolder(out IShellFolder desktopFolder);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint TrackPopupMenuEx(
            IntPtr menu,
            uint flags,
            int x,
            int y,
            IntPtr owner,
            IntPtr parameters);
    }
}
