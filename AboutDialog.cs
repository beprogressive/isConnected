using System.Diagnostics;

namespace IsConnected;

internal sealed class AboutDialog : Form
{
    public AboutDialog()
    {
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(360, 180);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "About IsConnected";

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(20, 20),
            Text = "IsConnected",
        };

        var versionLabel = new Label
        {
            AutoSize = true,
            Location = new Point(20, 52),
            Text = $"Version {ApplicationInfo.Version}",
        };

        var repositoryLabel = new Label
        {
            AutoSize = true,
            Location = new Point(20, 84),
            Text = "GitHub repository:",
        };

        var repositoryLink = new LinkLabel
        {
            AutoSize = true,
            Location = new Point(20, 108),
            Text = ApplicationInfo.RepositoryUrl,
        };
        repositoryLink.LinkClicked += (_, _) => OpenRepository();

        var okButton = new Button
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK,
            Location = new Point(265, 140),
            Size = new Size(75, 25),
            Text = "OK",
        };

        AcceptButton = okButton;
        CancelButton = okButton;
        Controls.Add(titleLabel);
        Controls.Add(versionLabel);
        Controls.Add(repositoryLabel);
        Controls.Add(repositoryLink);
        Controls.Add(okButton);
    }

    private static void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ApplicationInfo.RepositoryUrl)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open repository link: {ex.Message}",
                "IsConnected",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
