using System;
using System.Drawing;
using System.Windows.Forms;

namespace ASCOM.mytjaAstroBox.Switch
{
    /// <summary>
    /// Shows the warning/error/info history collected from the AstroBox.
    /// </summary>
    internal sealed class DeviceLogForm : Form
    {
        private readonly TextBox logTextBox;
        private readonly Button clearButton;
        private readonly Button closeButton;

        internal DeviceLogForm()
        {
            Text = "AstroBox Device Log";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 500);
            MinimumSize = new Size(600, 300);
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = true;
            FormBorderStyle = FormBorderStyle.Sizable;

            logTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                MaxLength = int.MaxValue,
                BackColor = Color.White,
                Font = new Font("Consolas", 9F),
                HideSelection = false
            };

            clearButton = new Button
            {
                Text = "Clear",
                AutoSize = true,
                Margin = new Padding(6)
            };
            clearButton.Click += ClearButton_Click;

            closeButton = new Button
            {
                Text = "Close",
                AutoSize = true,
                Margin = new Padding(6)
            };
            closeButton.Click += CloseButton_Click;
            CancelButton = closeButton;

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(6),
                WrapContents = false
            };
            buttons.Controls.Add(closeButton);
            buttons.Controls.Add(clearButton);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            layout.Controls.Add(logTextBox, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            Controls.Add(layout);

            Load += DeviceLogForm_Load;
            FormClosed += DeviceLogForm_FormClosed;
        }

        private void DeviceLogForm_Load(object sender, EventArgs e)
        {
            SwitchHardware.DeviceLogChanged += OnDeviceLogChanged;
            RefreshLog();
        }

        private void DeviceLogForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            SwitchHardware.DeviceLogChanged -= OnDeviceLogChanged;
        }

        private void OnDeviceLogChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new EventHandler(OnDeviceLogChanged), sender, e);
                }
                catch (InvalidOperationException)
                {
                    // The form is closing; there is nothing left to update.
                }
                catch (ObjectDisposedException)
                {
                    // The form has already been disposed.
                }
                return;
            }

            RefreshLog();
        }

        private void RefreshLog()
        {
            if (IsDisposed || logTextBox.IsDisposed)
                return;

            try
            {
                string text = SwitchHardware.GetDeviceLogText();
                logTextBox.Text = text;
                logTextBox.SelectionStart = logTextBox.TextLength;
                logTextBox.ScrollToCaret();
            }
            catch (ObjectDisposedException)
            {
                // The form is closing.
            }
            catch (InvalidOperationException)
            {
                // The form has no window handle yet or is closing.
            }
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(
                this,
                "Clear the device log history?",
                "AstroBox Device Log",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
                return;

            SwitchHardware.ClearDeviceLog();
            RefreshLog();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
