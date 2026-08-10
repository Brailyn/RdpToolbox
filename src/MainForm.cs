using BomgarMultiScreenRDP.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BomgarMultiScreenRDP
{
    public class MainForm : Form
    {
        private class MonitorInfo
        {
            public int Index;
            public string Value;
            public int X, Y, Width, Height;
            public bool Primary;
        }

        private class PreviewRectInfo
        {
            public string MonitorValue;
            public RectangleF Rect;
        }

        private readonly string settingsFile;
        private readonly string rdpFile;

        private List<MonitorInfo> monitorData = new List<MonitorInfo>();
        private List<PreviewRectInfo> previewRects = new List<PreviewRectInfo>();
        private List<string> selectedMonitorValues = new List<string>();

        private TextBox textServer;
        private TextBox textUser;
        private Label statusLabel;
        private Panel previewPanel;
        private CheckBox checkClipboard;
        private CheckBox checkAutoConnect;
        private TextBox textSettingsPath;
        private TextBox textRdpPath;

        public MainForm()
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Bomgar", "MultiScreenRdp");

            settingsFile = Path.Combine(appDataDir, "settings.ini");
            rdpFile = Path.Combine(appDataDir, "BOMGAR-MULTI-SCREEN.rdp");

            Directory.CreateDirectory(appDataDir);

            InitializeComponent();

            var settings = SettingsService.Load(settingsFile);
            checkClipboard.Checked = settings.Clipboard != "0";
            checkAutoConnect.Checked = settings.AutoConnect != "0";
            textSettingsPath.Text = settingsFile;
            textRdpPath.Text = rdpFile;

            LoadMonitors(settings.Monitors);
        }

        private void InitializeComponent()
        {
            Text = "BOMGAR Multi-Screen RDP Tool";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(860, 640);
            MinimumSize = new Size(860, 640);

            var labelServer = new Label { Text = "Server Address:", Location = new Point(20, 20), AutoSize = true };
            Controls.Add(labelServer);

            textServer = new TextBox { Location = new Point(140, 16), Size = new Size(680, 24) };
            Controls.Add(textServer);

            var labelUser = new Label { Text = "Username:", Location = new Point(20, 55), AutoSize = true };
            Controls.Add(labelUser);

            textUser = new TextBox { Location = new Point(140, 51), Size = new Size(680, 24) };
            Controls.Add(textUser);

            statusLabel = new Label { Location = new Point(20, 95), Size = new Size(800, 20) };
            Controls.Add(statusLabel);

            previewPanel = new Panel
            {
                Location = new Point(20, 125),
                Size = new Size(800, 320),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.WhiteSmoke
            };
            previewPanel.Paint += PreviewPanel_Paint;
            previewPanel.MouseDown += PreviewPanel_MouseDown;
            Controls.Add(previewPanel);

            var buttonDetect = new Button { Text = "Detect Monitors", Location = new Point(20, 460), Size = new Size(120, 30) };
            buttonDetect.Click += ButtonDetect_Click;
            Controls.Add(buttonDetect);

            var buttonAll = new Button { Text = "Select All", Location = new Point(150, 460), Size = new Size(100, 30) };
            buttonAll.Click += ButtonAll_Click;
            Controls.Add(buttonAll);

            var buttonClear = new Button { Text = "Clear All", Location = new Point(260, 460), Size = new Size(100, 30) };
            buttonClear.Click += ButtonClear_Click;
            Controls.Add(buttonClear);

            checkClipboard = new CheckBox { Text = "Enable Clipboard redirection", Location = new Point(20, 505), AutoSize = true };
            Controls.Add(checkClipboard);

            checkAutoConnect = new CheckBox { Text = "Auto-click Connect (skip security prompt)", Location = new Point(230, 505), AutoSize = true };
            Controls.Add(checkAutoConnect);

            var buttonLaunch = new Button { Text = "Launch RDP", Location = new Point(640, 505), Size = new Size(90, 32) };
            buttonLaunch.Click += ButtonLaunch_Click;
            Controls.Add(buttonLaunch);

            var buttonCancel = new Button { Text = "Cancel", Location = new Point(740, 505), Size = new Size(80, 32) };
            buttonCancel.Click += (s, e) => Close();
            Controls.Add(buttonCancel);

            var labelSettings = new Label { Text = "Settings file:", Location = new Point(20, 545), AutoSize = true };
            Controls.Add(labelSettings);

            textSettingsPath = new TextBox { Location = new Point(110, 542), Size = new Size(710, 24), ReadOnly = true };
            Controls.Add(textSettingsPath);

            var labelRdp = new Label { Text = "RDP file:", Location = new Point(20, 575), AutoSize = true };
            Controls.Add(labelRdp);

            textRdpPath = new TextBox { Location = new Point(110, 572), Size = new Size(710, 24), ReadOnly = true };
            Controls.Add(textRdpPath);
        }

        private List<MonitorInfo> GetDetectedMonitors()
        {
            var screens = Screen.AllScreens;
            var list = new List<MonitorInfo>();

            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                list.Add(new MonitorInfo
                {
                    Index = i,
                    Value = i.ToString(),
                    X = s.Bounds.X,
                    Y = s.Bounds.Y,
                    Width = s.Bounds.Width,
                    Height = s.Bounds.Height,
                    Primary = s.Primary
                });
            }

            return list;
        }

        private void SetSelectedMonitorValues(IEnumerable<string> values)
        {
            selectedMonitorValues = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        }

        private string GetSelectedMonitorString()
        {
            return string.Join(",", selectedMonitorValues.OrderBy(v => int.Parse(v)));
        }

        private void LoadMonitors(string savedMonitors)
        {
            monitorData = GetDetectedMonitors();

            var saved = string.IsNullOrWhiteSpace(savedMonitors)
                ? new string[0]
                : savedMonitors.Split(',').Select(v => v.Trim()).Where(v => v != "").ToArray();

            if (saved.Length > 0)
                SetSelectedMonitorValues(saved);
            else if (monitorData.Count > 0)
                SetSelectedMonitorValues(new[] { monitorData[0].Value });
            else
                SetSelectedMonitorValues(new string[0]);

            statusLabel.Text = string.Format(
                "Detected {0} monitor(s). Click to select. Ctrl+Click to toggle.",
                monitorData.Count);
            previewPanel.Invalidate();
        }

        private void PreviewPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.WhiteSmoke);

            previewRects = new List<PreviewRectInfo>();

            if (monitorData.Count == 0)
                return;

            int minX = monitorData.Min(m => m.X);
            int minY = monitorData.Min(m => m.Y);
            int maxRight = monitorData.Max(m => m.X + m.Width);
            int maxBottom = monitorData.Max(m => m.Y + m.Height);

            double virtualWidth = maxRight - minX;
            double virtualHeight = maxBottom - minY;

            if (virtualWidth <= 0 || virtualHeight <= 0)
                return;

            const int padding = 20;
            double usableWidth = previewPanel.ClientSize.Width - (padding * 2);
            double usableHeight = previewPanel.ClientSize.Height - (padding * 2);

            double scaleX = usableWidth / virtualWidth;
            double scaleY = usableHeight / virtualHeight;
            double scale = Math.Min(scaleX, scaleY);

            double drawWidth = virtualWidth * scale;
            double drawHeight = virtualHeight * scale;

            int offsetX = (int)((previewPanel.ClientSize.Width - drawWidth) / 2);
            int offsetY = (int)((previewPanel.ClientSize.Height - drawHeight) / 2);

            foreach (var mon in monitorData)
            {
                float x = (float)(offsetX + (mon.X - minX) * scale);
                float y = (float)(offsetY + (mon.Y - minY) * scale);
                float w = (float)Math.Max(90, mon.Width * scale);
                float h = (float)Math.Max(60, mon.Height * scale);

                var rect = new RectangleF(x, y, w, h);
                previewRects.Add(new PreviewRectInfo { MonitorValue = mon.Value, Rect = rect });

                bool isSelected = selectedMonitorValues.Contains(mon.Value);

                var fillColor = isSelected
                    ? Color.FromArgb(190, 208, 230, 255)
                    : Color.FromArgb(180, 225, 225, 225);

                using (var brush = new SolidBrush(fillColor))
                using (var pen = new Pen(Color.DimGray, 2))
                using (var font = new Font("Segoe UI", 20, FontStyle.Bold))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var textBrush = new SolidBrush(Color.Black))
                {
                    g.FillRectangle(brush, rect);
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    g.DrawString((mon.Index + 1).ToString(), font, textBrush, rect, sf);

                    if (mon.Primary)
                    {
                        using (var smallFont = new Font("Segoe UI", 8, FontStyle.Regular))
                        {
                            g.DrawString("Primary", smallFont, textBrush, rect.X + 6, rect.Y + 6);
                        }
                    }
                }
            }
        }

        private void PreviewPanel_MouseDown(object sender, MouseEventArgs e)
        {
            PreviewRectInfo hit = null;
            foreach (var item in previewRects)
            {
                if (item.Rect.Contains(e.X, e.Y))
                {
                    hit = item;
                    break;
                }
            }

            if (hit == null)
                return;

            bool ctrlPressed = (ModifierKeys & Keys.Control) == Keys.Control;
            string value = hit.MonitorValue;

            if (ctrlPressed)
            {
                if (selectedMonitorValues.Contains(value))
                    selectedMonitorValues.Remove(value);
                else
                    selectedMonitorValues.Add(value);
            }
            else
            {
                SetSelectedMonitorValues(new[] { value });
            }

            previewPanel.Invalidate();
        }

        private void WriteRdpFile(string server, string username, string selectedMonitors, bool clipboard)
        {
            int clipboardValue = clipboard ? 1 : 0;
            int disableClipboardValue = clipboard ? 0 : 1;

            var lines = new[]
            {
                "full address:s:" + server,
                "username:s:" + username,
                "prompt for credentials:i:1",
                "administrative session:i:1",
                "screen mode id:i:1",
                "use multimon:i:1",
                "selectedmonitors:s:" + selectedMonitors,
                "redirectclipboard:i:" + clipboardValue,
                "disableclipboardredirection:i:" + disableClipboardValue,
                "redirectprinters:i:0",
                "redirectsmartcards:i:0"
            };

            File.WriteAllLines(rdpFile, lines, System.Text.Encoding.ASCII);
        }

        private void ButtonDetect_Click(object sender, EventArgs e)
        {
            var saved = GetSelectedMonitorString();
            LoadMonitors(saved);
        }

        private void ButtonAll_Click(object sender, EventArgs e)
        {
            SetSelectedMonitorValues(monitorData.Select(m => m.Value));
            previewPanel.Invalidate();
        }

        private void ButtonClear_Click(object sender, EventArgs e)
        {
            SetSelectedMonitorValues(new string[0]);
            previewPanel.Invalidate();
        }

        private void ButtonLaunch_Click(object sender, EventArgs e)
        {
            var server = textServer.Text.Trim();
            var username = textUser.Text.Trim();
            var selectedMonitors = GetSelectedMonitorString();

            if (string.IsNullOrWhiteSpace(server) && string.IsNullOrWhiteSpace(username))
            {
                if (File.Exists(rdpFile))
                {
                    LaunchRdp();
                    return;
                }

                MessageBox.Show("No existing RDP file was found. Enter a server and username first.", "Validation");
                return;
            }

            if (string.IsNullOrWhiteSpace(server))
            {
                MessageBox.Show("Please enter a server address, or leave both server and username blank to open the existing RDP file.", "Validation");
                return;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Please enter a username, or leave both server and username blank to open the existing RDP file.", "Validation");
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedMonitors))
            {
                MessageBox.Show("Please select at least one monitor.", "Validation");
                return;
            }

            SettingsService.Save(settingsFile, selectedMonitors, checkClipboard.Checked, checkAutoConnect.Checked);
            WriteRdpFile(server, username, selectedMonitors, checkClipboard.Checked);
            textSettingsPath.Text = settingsFile;
            textRdpPath.Text = rdpFile;
            LaunchRdp();
        }

        private void LaunchRdp()
        {
            var checkboxNames = new List<string> { "Don't ask me again for connections to this computer" };
            if (checkClipboard.Checked)
                checkboxNames.Add("Clipboard");

            RdpAutoConnectService.Connect(rdpFile, checkAutoConnect.Checked, checkboxNames.ToArray());
        }
    }
}
