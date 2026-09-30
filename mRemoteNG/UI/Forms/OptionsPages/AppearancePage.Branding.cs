using System;
using System.Windows.Forms;
using mRemoteNG.App.Branding;
using mRemoteNG.UI.Controls;

namespace mRemoteNG.UI.Forms.OptionsPages
{
    public sealed partial class AppearancePage
    {
        private MrngTextBox txtApplicationDisplayName;
        private MrngTextBox txtApplicationIconPath;
        private MrngCheckBox chkHideFromStartMenu;

        private void InitializeBrandingControls()
        {
            // Keep these additional options in the existing, themed, scrollable page.
            AutoScroll = true;
            pnlOptions.AutoSize = true;
            pnlOptions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tableLayoutPanelAppearance.Dock = DockStyle.Top;
            tableLayoutPanelAppearance.AutoSize = true;
            tableLayoutPanelAppearance.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            txtApplicationDisplayName = new MrngTextBox
            {
                Name = "txtApplicationDisplayName",
                Dock = DockStyle.Fill,
                MaxLength = 80,
                PlaceholderText = ApplicationBranding.DefaultDisplayName,
                Margin = new Padding(6, 3, 24, 6)
            };
            txtApplicationIconPath = new MrngTextBox
            {
                Name = "txtApplicationIconPath",
                Dock = DockStyle.Fill,
                PlaceholderText = "Use the build's default icon",
                Margin = new Padding(0, 3, 6, 3)
            };
            var browse = new MrngButton
            {
                Name = "btnBrowseApplicationIcon",
                Text = "Browse…",
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 0)
            };
            browse.Click += BrowseApplicationIcon;

            var iconRow = new TableLayoutPanel
            {
                Name = "pnlApplicationIcon",
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(6, 3, 24, 6)
            };
            iconRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            iconRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            iconRow.Controls.Add(txtApplicationIconPath, 0, 0);
            iconRow.Controls.Add(browse, 1, 0);

            chkHideFromStartMenu = new MrngCheckBox
            {
                Name = "chkHideFromStartMenu",
                Text = "Hide this app's shortcut from my Start menu",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(6, 3, 24, 6)
            };
            var reset = new MrngButton
            {
                Name = "btnResetApplicationBranding",
                Text = "Reset app name, icon and Start menu setting to build defaults",
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                Margin = new Padding(6, 3, 24, 6)
            };
            reset.Click += (_, _) =>
            {
                txtApplicationDisplayName.Clear();
                txtApplicationIconPath.Clear();
                chkHideFromStartMenu.Checked = !ApplicationBranding.DefaultShowInStartMenu;
            };

            AddBrandingRow(CreateBrandingLabel("Application name (leave blank to use the build default)"));
            AddBrandingRow(txtApplicationDisplayName);
            AddBrandingRow(CreateBrandingLabel("Application icon (.ico, up to 4 MiB)"));
            AddBrandingRow(iconRow);
            AddBrandingRow(chkHideFromStartMenu);
            AddBrandingRow(reset);
            AddBrandingRow(CreateBrandingLabel("Changes apply after restarting. The executable name and file icon are set when building."));
        }

        private static MrngLabel CreateBrandingLabel(string text) => new MrngLabel
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 6, 24, 3)
        };

        private void AddBrandingRow(Control control)
        {
            int row = tableLayoutPanelAppearance.RowCount++;
            tableLayoutPanelAppearance.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.TabIndex = row;
            tableLayoutPanelAppearance.Controls.Add(control, 0, row);
        }

        private void BrowseApplicationIcon(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Choose an application icon",
                Filter = "Windows icon (*.ico)|*.ico",
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                txtApplicationIconPath.Text = dialog.FileName;
        }

        private void LoadBrandingPreferences()
        {
            var settings = Properties.OptionsAppearancePage.Default;
            txtApplicationDisplayName.Text = settings.ApplicationDisplayName;
            txtApplicationIconPath.Text = settings.ApplicationIconPath;
            chkHideFromStartMenu.Checked = !(settings.HasCustomStartMenuPreference
                ? settings.ShowInStartMenu
                : ApplicationBranding.DefaultShowInStartMenu);
        }

        internal void ValidateBrandingPreferences() =>
            ApplicationBranding.ValidatePreferences(txtApplicationDisplayName.Text, txtApplicationIconPath.Text);

        internal bool SaveBrandingPreferences()
        {
            bool changed = ApplicationBranding.SavePreferences(
                txtApplicationDisplayName.Text, txtApplicationIconPath.Text, !chkHideFromStartMenu.Checked);
            // The service copies a selected icon into managed storage; show its persisted path.
            LoadBrandingPreferences();
            return changed;
        }
    }
}
