using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace mRemoteNG.App.Branding
{
    /// <summary>Uses the Windows Shell on the STA UI thread; never launches a script or requires elevation.</summary>
    internal sealed class WindowsStartMenuShortcutStore : IStartMenuShortcutStore
    {
        public IEnumerable<string> Enumerate(string folder) => Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();

        public string ReadTarget(string path)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(path, 0);
                var arguments = new StringBuilder(32768);
                link.GetArguments(arguments, arguments.Capacity);
                if (arguments.Length != 0) return null; // Preserve user-created session/command shortcuts.
                var target = new StringBuilder(32768);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 4 /* SLGP_RAWPATH, no resolving/network searches */);
                return Environment.ExpandEnvironmentVariables(target.ToString());
            }
            catch (COMException)
            {
                // Invalid/unsupported links are not ours to overwrite or delete.
                return null;
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        public void Write(string path, string executable, string title, string iconPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            bool replacing = File.Exists(path);
            string temporaryPath = Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid().ToString("N") + ".tmp");
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(executable);
                link.SetWorkingDirectory(Path.GetDirectoryName(executable));
                link.SetArguments(string.Empty);
                link.SetDescription(title);
                link.SetShowCmd(1 /* SW_SHOWNORMAL */);
                link.SetIconLocation(iconPath, 0);
                ((IPersistFile)link).Save(temporaryPath, true);
                File.Move(temporaryPath, path, overwrite: true);
                NotifyShell(path, replacing ? 0x00002000u /* SHCNE_UPDATEITEM */ : 0x00000002u /* SHCNE_CREATE */);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        public void Delete(string path)
        {
            File.Delete(path);
            NotifyShell(path, 0x00000004 /* SHCNE_DELETE */);
        }

        private static void NotifyShell(string path, uint change)
        {
            SHChangeNotify(change, 0x0005 /* SHCNF_PATHW */, path, IntPtr.Zero);
            SHChangeNotify(0x00001000 /* SHCNE_UPDATEDIR */, 0x0005, Path.GetDirectoryName(path), IntPtr.Zero);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        // Vtable order is defined by IShellLinkW, including unused methods before SetPath.
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
            void GetIDList(out IntPtr itemIdList);
            void SetIDList(IntPtr itemIdList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int count);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int command);
            void SetShowCmd(int command);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr window, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }
    }
}
