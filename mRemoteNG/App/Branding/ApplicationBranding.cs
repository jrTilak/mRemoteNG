using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;
using mRemoteNG.App.Info;
using AppearanceSettings = mRemoteNG.Properties.OptionsAppearancePage;

namespace mRemoteNG.App.Branding
{
    /// <summary>Shortcut branding is a launch snapshot; window titles are empty and settings identities stay stable.</summary>
    internal static class ApplicationBranding
    {
        private const int MaximumIconBytes = 4 * 1024 * 1024;
        private static string _displayName;
        private static string _iconPath;
        private static string _executablePath;
        private static Icon _icon;
        private static bool _initialized;

        internal static event Action Initialized;

        internal static string DefaultDisplayName => GetMetadata("BrandingDisplayName") ??
            typeof(ApplicationBranding).Assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? "mRemoteNG";

        internal static bool DefaultShowInStartMenu =>
            !bool.TryParse(GetMetadata("BrandingShowInStartMenu"), out bool show) || show;

        internal static string DisplayName
        {
            get
            {
                if (_displayName != null) return _displayName;
                try { return GetDisplayName(AppearanceSettings.Default.ApplicationDisplayName); }
                catch (ArgumentException) { return DefaultDisplayName; }
            }
        }
        internal static string IconPath => _iconPath ?? Application.ExecutablePath;

        private static string GetMetadata(string key) => typeof(ApplicationBranding).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == key)?.Value;

        private static string GetDisplayName(string preference)
        {
            string name = BrandingPreferences.NormalizeDisplayName(preference);
            return name.Length == 0 ? DefaultDisplayName : name;
        }

        /// <summary>Called on the main UI thread after settings migration and singleton selection.</summary>
        internal static string Initialize()
        {
            if (_initialized) return null;
            var failures = new List<string>();
            try { _displayName = GetDisplayName(AppearanceSettings.Default.ApplicationDisplayName); }
            catch (ArgumentException exception)
            {
                _displayName = DefaultDisplayName;
                failures.Add(exception.Message);
            }

            try
            {
                _executablePath = FindExecutablePath();
                _iconPath = _executablePath;
            }
            catch (IOException exception)
            {
                failures.Add(exception.Message);
            }
            string customPath = AppearanceSettings.Default.ApplicationIconPath;
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                try
                {
                    _icon = LoadIcon(ReadIconBytes(customPath));
                    _iconPath = customPath;
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    failures.Add("The saved app icon could not be loaded. The build icon will be used. " + exception.Message);
                }
            }
            if (_icon == null && _executablePath != null)
            {
                try { _icon = Icon.ExtractAssociatedIcon(_executablePath); }
                catch (Exception exception) when (exception is ArgumentException or ExternalException or IOException)
                {
                    failures.Add("The build icon could not be loaded. " + exception.Message);
                }
            }

            _initialized = true;
            Initialized?.Invoke();
            try
            {
                bool show = AppearanceSettings.Default.HasCustomStartMenuPreference
                    ? AppearanceSettings.Default.ShowInStartMenu : DefaultShowInStartMenu;
                string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                if (string.IsNullOrWhiteSpace(programs))
                    throw new IOException("Windows did not provide the current user's Start menu folder.");
                if (_executablePath != null)
                    new StartMenuShortcutManager(new WindowsStartMenuShortcutStore()).Apply(
                        Path.Combine(programs, "mRemoteNG"), _executablePath, _displayName, IconPath, show);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException or ArgumentException)
            {
                failures.Add("The Start menu shortcut could not be updated. " + exception.Message);
            }
            return failures.Count == 0 ? null : string.Join(Environment.NewLine, failures);
        }

        internal static Icon CreateWindowIcon() => _icon == null ? null : (Icon)_icon.Clone();

        private static string FindExecutablePath()
        {
            string executable = Application.ExecutablePath;
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(executable), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                // A development launch through dotnet must not create a shortcut
                // that just opens the dotnet host without the application DLL.
                executable = Path.Combine(AppContext.BaseDirectory, "Capture2Text.exe");
            }
            if (!File.Exists(executable))
                throw new IOException("The app executable was not found. Build and launch Capture2Text.exe to update the Start menu shortcut.");
            return executable;
        }

        internal static void ValidatePreferences(string displayName, string iconPath)
        {
            BrandingPreferences.NormalizeDisplayName(displayName);
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                using Icon validatedIcon = LoadIcon(ReadIconBytes(iconPath.Trim()));
            }
        }

        internal static bool SavePreferences(string displayName, string iconPath, bool showInStartMenu)
        {
            string name = BrandingPreferences.NormalizeDisplayName(displayName);
            string path = string.Empty;
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                byte[] bytes = ReadIconBytes(iconPath.Trim());
                using Icon validatedIcon = LoadIcon(bytes);
                string folder = Path.Combine(SettingsFileInfo.SettingsPath, "Branding");
                Directory.CreateDirectory(folder);
                // Immutable filenames keep the running app/old shortcut's icon intact
                // if the user declines restart; no dependence on the selected source file.
                path = Path.Combine(folder, Convert.ToHexString(SHA256.HashData(bytes)) + ".ico");
                if (!File.Exists(path))
                {
                    string temporaryPath = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        File.WriteAllBytes(temporaryPath, bytes);
                        File.Move(temporaryPath, path, overwrite: true);
                    }
                    finally
                    {
                        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                    }
                }
            }

            var settings = AppearanceSettings.Default;
            string previousName = settings.ApplicationDisplayName;
            string previousIcon = settings.ApplicationIconPath;
            bool previousShow = settings.ShowInStartMenu;
            bool previousOverride = settings.HasCustomStartMenuPreference;
            bool hadShow = previousOverride ? previousShow : DefaultShowInStartMenu;
            bool changed = !string.Equals(previousName, name, StringComparison.Ordinal) ||
                !string.Equals(previousIcon, path, StringComparison.OrdinalIgnoreCase) || hadShow != showInStartMenu;
            try
            {
                settings.ApplicationDisplayName = name;
                settings.ApplicationIconPath = path;
                settings.ShowInStartMenu = showInStartMenu;
                settings.HasCustomStartMenuPreference = showInStartMenu != DefaultShowInStartMenu;
                settings.Save();
            }
            catch
            {
                settings.ApplicationDisplayName = previousName;
                settings.ApplicationIconPath = previousIcon;
                settings.ShowInStartMenu = previousShow;
                settings.HasCustomStartMenuPreference = previousOverride;
                throw;
            }
            return changed;
        }

        private static byte[] ReadIconBytes(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".ico", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a Windows .ico file for the app icon.");
            using var stream = File.OpenRead(path);
            if (stream.Length == 0 || stream.Length > MaximumIconBytes)
                throw new ArgumentException("Choose a non-empty .ico file no larger than 4 MB.");
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }

        private static Icon LoadIcon(byte[] bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var icon = new Icon(stream);
                return (Icon)icon.Clone();
            }
            catch (Exception exception) when (exception is ArgumentException or ExternalException)
            {
                throw new ArgumentException("The selected file is not a valid Windows icon.", exception);
            }
        }

        internal static void Dispose()
        {
            _icon?.Dispose();
            _icon = null;
        }
    }
}
