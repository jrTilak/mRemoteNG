using System;
using System.IO;

namespace mRemoteNG.App.Branding
{
    // Independent of WinForms/Windows so preference and shortcut safety rules can be tested on any OS.
    internal static class BrandingPreferences
    {
        internal static string NormalizeDisplayName(string value)
        {
            string name = (value ?? string.Empty).Trim();
            if (name.Length == 0) return name;
            if (name.Length > 80 || name.EndsWith('.') || name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0)
                throw new ArgumentException("Use an app title of at most 80 characters, without Windows filename symbols or a final period.");
            foreach (char character in name)
                if (char.IsControl(character))
                    throw new ArgumentException("The app title cannot contain control characters.");
            string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
            bool numberedDevice = stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) &&
                "123456789¹²³".Contains(stem[3]);
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" || numberedDevice)
                throw new ArgumentException("The app title cannot be a reserved Windows device name.");
            return name;
        }

        internal static bool SameTarget(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
    }
}
