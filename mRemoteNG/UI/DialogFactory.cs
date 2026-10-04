using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Dialogs;
using mRemoteNG.App;
using mRemoteNG.App.Info;
using mRemoteNG.Messages;
using mRemoteNG.UI.TaskDialog;
using mRemoteNG.Resources.Language;
using System.Runtime.Versioning;

namespace mRemoteNG.UI
{
    [SupportedOSPlatform("windows")]
    public class DialogFactory
    {
        public static OpenFileDialog BuildLoadConnectionsDialog()
        {
            return new OpenFileDialog
            {
                Title = "",
                CheckFileExists = true,
                InitialDirectory = ConnectionsFileInfo.DefaultConnectionsPath,
                Filter = Language.FiltermRemoteXML + @"|*.xml|" + Language.FilterAll + @"|*.*"
            };
        }

        /// <summary>
        /// Creates and shows a dialog to either create a new connections file, load a different one,
        /// exit, or optionally cancel the operation.
        /// </summary>
        /// <param name="connectionFileName"></param>
        /// <param name="messageText"></param>
        /// <param name="showCancelButton"></param>
        public static void ShowLoadConnectionsFailedDialog(string connectionFileName, string messageText, bool showCancelButton)
            => ShowLoadConnectionsFailedDialog(connectionFileName, messageText, showCancelButton,
                () => Runtime.IsMainWindowClosing);

        internal static void ShowLoadConnectionsFailedDialog(string connectionFileName, string messageText,
            bool showCancelButton, Func<bool> isClosing)
        {
            // Password cancellation can finish after F9 has already disposed
            // its main-window owner. Do not start another modal recovery loop.
            if (isClosing()) return;
            List<string> commandButtons = new()
            {
                Language.ConfigurationCreateNew,
                Language.OpenADifferentFile,
                Language.Exit
            };

            if (showCancelButton)
                commandButtons.Add(Language._Cancel);

            bool answered = false;
            while (!answered && !isClosing())
            {
                try
                {
                    CTaskDialog.ShowTaskDialogBox(GeneralAppInfo.ProductName, messageText, "", "", "", "", "", string.Join(" | ", commandButtons), ETaskDialogButtons.None, ESysIcons.Question, ESysIcons.Question);
                    if (isClosing()) return;

                    switch (CTaskDialog.CommandButtonResult)
                    {
                        case 0: // New
                            using (SaveFileDialog saveAsDialog = ConnectionsSaveAsDialog())
                            {
                                DialogResult result = saveAsDialog.ShowDialog();
                                if (isClosing()) return;
                                if (result != DialogResult.OK) break;
                                Runtime.ConnectionsService.NewConnectionsFile(saveAsDialog.FileName);
                            }
                            answered = true;
                            break;
                        case 1: // Load
                            Runtime.LoadConnections(true);
                            answered = true;
                            break;
                        case 2: // Exit
                            Application.Exit();
                            answered = true;
                            break;
                        case 3: // Cancel
                            answered = true;
                            break;
                    }
                }
                catch (Exception exception)
                {
                    Runtime.MessageCollector.AddExceptionMessage(string .Format(Language.ConnectionsFileCouldNotBeLoadedNew, connectionFileName), exception, MessageClass.WarningMsg);
                }
            }
        }

        /// <summary>
        /// Creates a new dialog that allows the user to select an mRemoteNG
        /// connections file path. Don't forget to dispose the dialog when you
        /// are done!
        /// </summary>
        public static SaveFileDialog ConnectionsSaveAsDialog()
        {
            return new SaveFileDialog
            {
                CheckPathExists = true,
                InitialDirectory = ConnectionsFileInfo.DefaultConnectionsPath,
                FileName = ConnectionsFileInfo.DefaultConnectionsFile,
                OverwritePrompt = true,
                Filter = Language.FiltermRemoteXML + @"|*.xml|" + Language.FilterAll + @"|*.*"
            };
        }

        public static CommonOpenFileDialog SelectFolder(string DialogWindowTitle)
        {
            return new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = DialogWindowTitle,
                EnsurePathExists = true,
                InitialDirectory = Properties.OptionsBackupPage.Default.BackupLocation != "" ? Properties.OptionsBackupPage.Default.BackupLocation : Directory.GetCurrentDirectory()
            };
        }

    }
}
