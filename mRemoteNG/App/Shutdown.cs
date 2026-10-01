using mRemoteNG.Tools;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.Putty;
using mRemoteNG.Properties;
using mRemoteNG.UI.Controls;
using mRemoteNG.UI.Forms;
using mRemoteNG.Resources.Language;
using System.Runtime.Versioning;

// ReSharper disable ArrangeAccessorOwnerBody

namespace mRemoteNG.App
{
    [SupportedOSPlatform("windows")]
    public static class Shutdown
    {
        private static string? _updateFilePath;
        private static bool _restartRequested;

        private static bool UpdatePending
        {
            get { return !string.IsNullOrEmpty(_updateFilePath); }
        }

        public static void Quit(string? updateFilePath = null)
        {
            _updateFilePath = updateFilePath;
            FrmMain main = FrmMain.Default;
            main.Close();
            if (main.IsDisposed)
                ProgramRoot.CloseSingletonInstanceMutex();
            else
                _updateFilePath = null;
        }

        internal static void Restart()
        {
            FrmMain main = FrmMain.Default;
            if (main.IsClosing || main.IsDisposed) return;

            _restartRequested = true;
            // Use the normal shutdown path so active sessions and resources are released.
            main.Close();
            if (!main.IsDisposed)
                _restartRequested = false;
        }

        /// <summary>
        /// Called only after the UI message loop exits and the single-instance mutex is released.
        /// </summary>
        internal static void StartRestartIfRequested()
        {
            if (!_restartRequested) return;
            _restartRequested = false;
            try
            {
                string executablePath = Environment.ProcessPath ?? Application.ExecutablePath;
                var startInfo = new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Environment.CurrentDirectory
                };
                // Framework-dependent developer launches can run through the dotnet host.
                if (string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    string entryAssembly = Assembly.GetEntryAssembly()?.Location;
                    if (string.IsNullOrEmpty(entryAssembly))
                        throw new InvalidOperationException("The entry assembly could not be located for restart.");
                    startInfo.ArgumentList.Add(entryAssembly);
                }
                // Preserve launch options without logging them or shell concatenation.
                string[] arguments = Environment.GetCommandLineArgs();
                for (int i = 1; i < arguments.Length; i++)
                    startInfo.ArgumentList.Add(arguments[i]);
                Process.Start(startInfo)?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Instance.Log?.Error("The app could not restart. Open it again to apply the saved settings.", ex);
            }
        }

        public static void Cleanup(Control quickConnectToolStrip,
                                   ExternalToolsToolStrip externalToolsToolStrip,
                                   MultiSshToolStrip multiSshToolStrip,
                                   FrmMain frmMain)
        {
            try
            {
                StopPuttySessionWatcher();
                DisposeNotificationAreaIcon();
                SaveConnections();
                SaveSettings(quickConnectToolStrip, externalToolsToolStrip, multiSshToolStrip, frmMain);
                UnregisterBrowsers();
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.SettingsCouldNotBeSavedOrTrayDispose, ex);
            }
        }

        private static void StopPuttySessionWatcher()
        {
            PuttySessionsManager.Instance.StopWatcher();
        }

        private static void DisposeNotificationAreaIcon()
        {
            if (Runtime.NotificationAreaIcon != null && Runtime.NotificationAreaIcon.Disposed == false)
                Runtime.NotificationAreaIcon.Dispose();
        }

        private static void SaveConnections()
        {
            DateTime lastUpdate;
            DateTime updateDate;
            DateTime currentDate = DateTime.Now;

            if ((Properties.OptionsBackupPage.Default.SaveConnectionsFrequency == (int)ConnectionsBackupFrequencyEnum.OnExit))
            {
                Runtime.ConnectionsService.SaveConnections();
				return;
            }	
			lastUpdate = Runtime.ConnectionsService.UsingDatabase ? Runtime.ConnectionsService.LastSqlUpdate : Runtime.ConnectionsService.LastFileUpdate;

            switch (Properties.OptionsBackupPage.Default.SaveConnectionsFrequency)
            {
                case (int)ConnectionsBackupFrequencyEnum.Daily:
                    updateDate = lastUpdate.AddDays(1);
                    break;
                case (int)ConnectionsBackupFrequencyEnum.Weekly:
                    updateDate = lastUpdate.AddDays(7);
                    break;
                default:
                    return;
            }

            if (currentDate >= updateDate)
            {
                Runtime.ConnectionsService.SaveConnections();
            }
        }

        private static void SaveSettings(Control quickConnectToolStrip,
                                         ExternalToolsToolStrip externalToolsToolStrip,
                                         MultiSshToolStrip multiSshToolStrip,
                                         FrmMain frmMain)
        {
            Config.Settings.SettingsSaver.SaveSettings(quickConnectToolStrip, externalToolsToolStrip, multiSshToolStrip,
                                                       frmMain);
        }

        private static void UnregisterBrowsers()
        {
            IeBrowserEmulation.Unregister();
        }

        public static void StartUpdate()
        {
            try
            {
                RunUpdateFile();
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace("The update could not be started.", ex);
            }
        }

        private static void RunUpdateFile()
        {
            if (UpdatePending && !string.IsNullOrEmpty(_updateFilePath))
            {
                // Validate the update file path to prevent command injection
                Tools.PathValidator.ValidateExecutablePathOrThrow(_updateFilePath, nameof(_updateFilePath));
                Process.Start(new ProcessStartInfo(_updateFilePath) { UseShellExecute = true });
            }
        }
    }
}
