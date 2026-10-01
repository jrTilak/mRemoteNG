using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace mRemoteNG.App.Branding
{
    internal interface IStartMenuShortcutStore
    {
        IEnumerable<string> Enumerate(string folder);
        string ReadTarget(string path);
        void Write(string path, string executable, string title, string iconPath);
        void Delete(string path);
    }

    internal sealed class StartMenuShortcutManager(IStartMenuShortcutStore store)
    {
        internal void Apply(string folder, string executable, string title, string iconPath, bool show)
        {
            string name = BrandingPreferences.NormalizeDisplayName(title);
            if (name.Length == 0) throw new ArgumentException("A Start menu shortcut needs an app title.");
            string desiredPath = Path.Combine(folder, name + ".lnk");
            string legacyExecutable = string.Equals(Path.GetFileName(executable), "Capture2Text.exe", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable)), "mRemoteNG.exe") : null;
            string[] paths = store.Enumerate(folder).ToArray();
            var ownedPaths = new List<string>();
            foreach (string path in paths)
            {
                // Include this installation's prior executable name when upgrading
                // in place; another directory or an unrelated target is never owned.
                string target = store.ReadTarget(path);
                bool owned = BrandingPreferences.SameTarget(target, executable) ||
                    BrandingPreferences.SameTarget(target, legacyExecutable);
                if (owned) ownedPaths.Add(path);
                if (show && string.Equals(path, desiredPath, StringComparison.OrdinalIgnoreCase) && !owned)
                    throw new IOException("Another shortcut already uses this app title. Choose a different title.");
            }

            // Save the replacement first; an icon/write failure must not remove the old entry.
            if (show) store.Write(desiredPath, executable, name, iconPath);
            foreach (string path in ownedPaths)
                if (!show || !string.Equals(path, desiredPath, StringComparison.OrdinalIgnoreCase))
                    store.Delete(path);
        }
    }
}
