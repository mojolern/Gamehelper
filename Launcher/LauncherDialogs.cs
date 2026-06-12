namespace Launcher
{
    using System;
    using System.Drawing;
    using System.Windows.Forms;

    internal static class LauncherDialogs
    {
        internal static bool ShowConfirm(string message, string title)
        {
            using var form = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(460, 180),
                BackColor = Color.FromArgb(26, 28, 34),
                ForeColor = Color.FromArgb(235, 237, 245),
                Font = new Font("Segoe UI", 10f),
            };

            var icon = new PictureBox
            {
                Image = SystemIcons.Warning.ToBitmap(),
                Location = new Point(20, 20),
                Size = new Size(32, 32),
                BackColor = Color.Transparent,
            };

            var label = new Label
            {
                AutoSize = false,
                Location = new Point(64, 16),
                Size = new Size(376, 100),
                Text = message,
                ForeColor = Color.FromArgb(235, 237, 245),
            };

            var yesButton = CreateButton(
                LauncherLocalization.L("Yes", "Ja"),
                Color.FromArgb(92, 140, 240),
                new Point(236, 128),
                new Size(100, 32));
            yesButton.DialogResult = DialogResult.Yes;

            var noButton = CreateButton(
                LauncherLocalization.L("No", "Nein"),
                Color.FromArgb(55, 58, 72),
                new Point(344, 128),
                new Size(100, 32));
            noButton.DialogResult = DialogResult.No;

            form.Controls.Add(icon);
            form.Controls.Add(label);
            form.Controls.Add(yesButton);
            form.Controls.Add(noButton);
            form.AcceptButton = yesButton;
            form.CancelButton = noButton;

            return form.ShowDialog() == DialogResult.Yes;
        }

        internal static void ShowError(string message, string title = "GameHelper")
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private static Button CreateButton(string text, Color back, Point location, Size size)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }
    }
}
