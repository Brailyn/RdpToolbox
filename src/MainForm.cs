using RdpToolbox.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace RdpToolbox
{
    public class MainForm : Form
    {
        private class MonitorInfo
        {
            public int Index;
            public string Value;
            public string DisplayNumber;
            public int X, Y, Width, Height;
            public bool Primary;
        }

        private class PreviewRectInfo
        {
            public string MonitorValue;
            public RectangleF Rect;
        }

        private const string FullScreenResolutionOption = "Full screen (span selected monitor)";

        private const string ClientAuto = "Automatic";
        private const string ClientMstsc = "Built-in (mstsc)";
        private const string ClientMsrdc = "Remote Desktop (msrdc)";

        private static readonly Size[] TypicalResolutions =
        {
            new Size(1024, 768), new Size(1152, 864), new Size(1280, 720), new Size(1280, 800),
            new Size(1280, 1024), new Size(1366, 768), new Size(1440, 900), new Size(1600, 900),
            new Size(1680, 1050), new Size(1920, 1080), new Size(1920, 1200), new Size(2560, 1080),
            new Size(2560, 1440), new Size(2560, 1600), new Size(3440, 1440), new Size(3840, 2160)
        };

        private readonly string appDataDir;
        private readonly string settingsFile;
        private readonly string historyFile;
        private readonly string rdpFile;

        private List<MonitorInfo> monitorData = new List<MonitorInfo>();
        private List<PreviewRectInfo> previewRects = new List<PreviewRectInfo>();
        private List<string> selectedMonitorValues = new List<string>();

        private ComboBox comboServer;
        private Button buttonHistory;
        private TextBox textUser;
        private TextBox textPassword;
        private CheckBox checkShowPassword;
        private Label statusLabel;
        private Panel previewPanel;
        private Label labelResolution;
        private ComboBox comboResolution;
        private ComboBox comboClient;
        private CheckBox checkAdminSession;
        private CheckBox checkWindowedSpan;
        private GroupBox groupAutoClick;
        private CheckBox checkAutoConnect;
        private CheckBox checkAutoClickAll;
        private CheckBox checkAutoClickWebAuthn;
        private CheckBox checkAutoClickDrives;
        private CheckBox checkAutoClickClipboard;
        private CheckBox checkAutoClickPrinters;

        public MainForm()
        {
            appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RdpToolbox");

            settingsFile = Path.Combine(appDataDir, "settings.ini");
            historyFile = Path.Combine(appDataDir, "server-history.txt");
            rdpFile = Path.Combine(appDataDir, "RDP-Toolbox-Session.rdp");

            Directory.CreateDirectory(appDataDir);

            InitializeComponent();

            var settings = SettingsService.Load(settingsFile);
            checkAutoConnect.Checked = settings.AutoConnect != "0";
            checkAutoClickAll.Checked = settings.AutoClickAll != "0";
            checkAutoClickWebAuthn.Checked = settings.AutoClickWebAuthn != "0";
            checkAutoClickDrives.Checked = settings.AutoClickDrives != "0";
            checkAutoClickClipboard.Checked = settings.AutoClickClipboard != "0";
            checkAutoClickPrinters.Checked = settings.AutoClickPrinters != "0";
            comboClient.SelectedItem =
                settings.Client == "mstsc" ? ClientMstsc :
                settings.Client == "msrdc" ? ClientMsrdc : ClientAuto;
            checkAdminSession.Checked = settings.AdminSession == "1";
            checkWindowedSpan.Checked = settings.WindowedSpan == "1";
            UpdateAutoClickEnabledState();

            RefreshServerHistoryItems();
            LoadMonitors(settings.Monitors);
        }

        private void InitializeComponent()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "RDP Toolbox";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(940, 758);
            MinimumSize = new Size(940, 758);
            Icon = LoadEmbeddedIcon();

            var labelServer = new Label { Text = "Server Address:", Location = new Point(20, 20), AutoSize = true };
            Controls.Add(labelServer);

            comboServer = new ComboBox
            {
                Location = new Point(150, 16),
                Size = new Size(600, 24),
                DropDownStyle = ComboBoxStyle.DropDown,
                TabIndex = 0
            };
            Controls.Add(comboServer);

            var labelUser = new Label { Text = "Username:", Location = new Point(20, 55), AutoSize = true };
            Controls.Add(labelUser);

            textUser = new TextBox { Location = new Point(150, 51), Size = new Size(760, 24), TabIndex = 1 };
            Controls.Add(textUser);

            var labelPassword = new Label { Text = "Password:", Location = new Point(20, 90), AutoSize = true };
            Controls.Add(labelPassword);

            textPassword = new TextBox { Location = new Point(150, 86), Size = new Size(600, 24), UseSystemPasswordChar = true, TabIndex = 2 };
            Controls.Add(textPassword);

            checkShowPassword = new CheckBox { Text = "Show", Location = new Point(760, 88), AutoSize = true, TabIndex = 3 };
            checkShowPassword.CheckedChanged += (s, e) => textPassword.UseSystemPasswordChar = !checkShowPassword.Checked;
            Controls.Add(checkShowPassword);

            buttonHistory = new Button { Text = "Manage History", Location = new Point(760, 15), Size = new Size(150, 26), TabIndex = 4 };
            buttonHistory.Click += ButtonHistory_Click;
            Controls.Add(buttonHistory);

            statusLabel = new Label { Location = new Point(20, 125), Size = new Size(900, 20) };
            Controls.Add(statusLabel);

            previewPanel = new Panel
            {
                Location = new Point(20, 150),
                Size = new Size(900, 300),
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

            labelResolution = new Label { Text = "Resolution (single monitor only):", Location = new Point(400, 466), AutoSize = true };
            Controls.Add(labelResolution);

            comboResolution = new ComboBox { Location = new Point(620, 462), Size = new Size(300, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            comboResolution.SelectedIndexChanged += ComboResolution_SelectedIndexChanged;
            Controls.Add(comboResolution);

            groupAutoClick = new GroupBox
            {
                Text = "Auto-click the connection prompt",
                Location = new Point(20, 500),
                Size = new Size(900, 110)
            };
            Controls.Add(groupAutoClick);

            checkAutoConnect = new CheckBox { Text = "Auto Connect", Location = new Point(15, 25), AutoSize = true };
            groupAutoClick.Controls.Add(checkAutoConnect);

            checkAutoClickAll = new CheckBox { Text = "All", Location = new Point(15, 55), AutoSize = true };
            checkAutoClickAll.CheckedChanged += (s, e) => UpdateAutoClickEnabledState();
            groupAutoClick.Controls.Add(checkAutoClickAll);

            checkAutoClickWebAuthn = new CheckBox { Text = "WebAuthn", Location = new Point(90, 55), AutoSize = true };
            groupAutoClick.Controls.Add(checkAutoClickWebAuthn);

            checkAutoClickDrives = new CheckBox { Text = "Drives", Location = new Point(210, 55), AutoSize = true };
            groupAutoClick.Controls.Add(checkAutoClickDrives);

            checkAutoClickClipboard = new CheckBox { Text = "Clipboard", Location = new Point(310, 55), AutoSize = true };
            groupAutoClick.Controls.Add(checkAutoClickClipboard);

            checkAutoClickPrinters = new CheckBox { Text = "Printers", Location = new Point(430, 55), AutoSize = true };
            groupAutoClick.Controls.Add(checkAutoClickPrinters);

            var labelClient = new Label { Text = "Client:", Location = new Point(300, 626), AutoSize = true };
            Controls.Add(labelClient);

            comboClient = new ComboBox
            {
                Location = new Point(348, 622),
                Size = new Size(210, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            comboClient.Items.AddRange(new object[] { ClientAuto, ClientMstsc, ClientMsrdc });
            comboClient.SelectedIndex = 0;
            Controls.Add(comboClient);

            checkAdminSession = new CheckBox
            {
                Text = "Admin session",
                Location = new Point(20, 622),
                AutoSize = true
            };
            Controls.Add(checkAdminSession);

            checkWindowedSpan = new CheckBox
            {
                Text = "Windowed span",
                Location = new Point(150, 622),
                AutoSize = true
            };
            Controls.Add(checkWindowedSpan);

            var tips = new ToolTip();
            tips.SetToolTip(checkWindowedSpan,
                "Spans the selected monitors as a positioned window instead of full screen.\r\n" +
                "Works around mstsc opening a spanned session on the wrong monitor when\r\n" +
                "monitors use different scaling - at the cost of a title bar, and the\r\n" +
                "session cannot be made full screen.");
            tips.SetToolTip(checkAdminSession,
                "Connects to the console/administrative session.\r\n" +
                "Rarely needed, and can prevent the advanced graphics pipeline from\r\n" +
                "negotiating, which leaves the session on slower legacy encoding.");

            var buttonOpenDataFolder = new Button { Text = "Open Data Folder", Location = new Point(20, 668), Size = new Size(140, 32) };
            buttonOpenDataFolder.Click += (s, e) => Process.Start("explorer.exe", "\"" + appDataDir + "\"");
            Controls.Add(buttonOpenDataFolder);

            var buttonDiagnostics = new Button { Text = "Diagnostics", Location = new Point(168, 668), Size = new Size(100, 32) };
            buttonDiagnostics.Click += ButtonDiagnostics_Click;
            Controls.Add(buttonDiagnostics);

            var buttonLaunch = new Button { Text = "Launch RDP", Location = new Point(700, 668), Size = new Size(110, 32) };
            buttonLaunch.Click += ButtonLaunch_Click;
            Controls.Add(buttonLaunch);

            var buttonCancel = new Button { Text = "Cancel", Location = new Point(820, 668), Size = new Size(100, 32) };
            buttonCancel.Click += (s, e) => Close();
            Controls.Add(buttonCancel);

            AcceptButton = buttonLaunch;
        }

        private static Icon LoadEmbeddedIcon()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("RdpToolbox.AppIcon.ico"))
            {
                return stream != null ? new Icon(stream) : null;
            }
        }

        private void UpdateAutoClickEnabledState()
        {
            bool notAll = !checkAutoClickAll.Checked;
            checkAutoClickWebAuthn.Enabled = notAll;
            checkAutoClickDrives.Enabled = notAll;
            checkAutoClickClipboard.Enabled = notAll;
            checkAutoClickPrinters.Enabled = notAll;
        }

        private void RefreshServerHistoryItems()
        {
            var current = comboServer.Text;
            comboServer.Items.Clear();
            foreach (var server in ServerHistoryService.Load(historyFile))
                comboServer.Items.Add(server);
            comboServer.Text = current;
        }

        private void ButtonHistory_Click(object sender, EventArgs e)
        {
            using (var form = new ServerHistoryForm(historyFile))
            {
                if (form.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(form.SelectedServer))
                    comboServer.Text = form.SelectedServer;

                RefreshServerHistoryItems();
            }
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
                    // selectedmonitors uses Screen.AllScreens' own HMONITOR enumeration order.
                    Value = i.ToString(),
                    DisplayNumber = ParseDisplayNumber(s.DeviceName, i).ToString(),
                    X = s.Bounds.X,
                    Y = s.Bounds.Y,
                    Width = s.Bounds.Width,
                    Height = s.Bounds.Height,
                    Primary = s.Primary
                });
            }

            return list;
        }

        private static int ParseDisplayNumber(string deviceName, int fallbackIndex)
        {
            var match = Regex.Match(deviceName ?? "", @"(\d+)$");
            return match.Success ? int.Parse(match.Value) : fallbackIndex + 1;
        }

        private void SetSelectedMonitorValues(IEnumerable<string> values)
        {
            selectedMonitorValues = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        }

        private string GetSelectedMonitorString()
        {
            return string.Join(",", selectedMonitorValues.OrderBy(v => int.Parse(v)));
        }

        private List<MonitorInfo> GetSelectedMonitors()
        {
            return selectedMonitorValues
                .Select(v => monitorData.FirstOrDefault(m => m.Value == v))
                .Where(m => m != null)
                .OrderBy(m => int.Parse(m.Value))
                .ToList();
        }

        // Two monitors are adjacent when they share a real edge segment (not just a corner point).
        // Windows can report monitor bounds with a few pixels of slop (rounding from mixed
        // per-monitor DPI scaling, or imprecise manual arrangement in Display Settings), so
        // treat edges within this many pixels of each other as touching.
        private const int AdjacencyToleranceInPixels = 12;

        private static bool AreAdjacent(MonitorInfo a, MonitorInfo b)
        {
            bool verticalOverlap = a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
            bool horizontalTouch =
                Math.Abs((a.X + a.Width) - b.X) <= AdjacencyToleranceInPixels ||
                Math.Abs((b.X + b.Width) - a.X) <= AdjacencyToleranceInPixels;

            bool horizontalOverlap = a.X < b.X + b.Width && b.X < a.X + a.Width;
            bool verticalTouch =
                Math.Abs((a.Y + a.Height) - b.Y) <= AdjacencyToleranceInPixels ||
                Math.Abs((b.Y + b.Height) - a.Y) <= AdjacencyToleranceInPixels;

            return (verticalOverlap && horizontalTouch) || (horizontalOverlap && verticalTouch);
        }

        private bool IsConnectedSet(IEnumerable<string> values)
        {
            var monitors = values.Select(v => monitorData.FirstOrDefault(m => m.Value == v)).Where(m => m != null).ToList();
            if (monitors.Count <= 1)
                return true;

            var visited = new HashSet<string> { monitors[0].Value };
            var queue = new Queue<MonitorInfo>();
            queue.Enqueue(monitors[0]);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var other in monitors)
                {
                    if (visited.Contains(other.Value))
                        continue;
                    if (AreAdjacent(cur, other))
                    {
                        visited.Add(other.Value);
                        queue.Enqueue(other);
                    }
                }
            }

            return visited.Count == monitors.Count;
        }

        private List<string> LargestConnectedGroup()
        {
            var visitedAll = new HashSet<string>();
            var best = new List<string>();

            foreach (var start in monitorData)
            {
                if (visitedAll.Contains(start.Value))
                    continue;

                var component = new List<string>();
                var visited = new HashSet<string> { start.Value };
                var queue = new Queue<MonitorInfo>();
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    component.Add(cur.Value);
                    visitedAll.Add(cur.Value);

                    foreach (var other in monitorData)
                    {
                        if (visited.Contains(other.Value))
                            continue;
                        if (AreAdjacent(cur, other))
                        {
                            visited.Add(other.Value);
                            queue.Enqueue(other);
                        }
                    }
                }

                if (component.Count > best.Count)
                    best = component;
            }

            return best;
        }

        private void LoadMonitors(string savedMonitors)
        {
            monitorData = GetDetectedMonitors();

            // Only keep saved values that match a monitor that actually exists. Settings carried
            // between machines can name monitors this one does not have, and an unmatched value
            // would otherwise survive validation while resolving to no monitor at launch -
            // producing "use multimon" with an empty selectedmonitors list, which makes the
            // client span every monitor instead of the intended ones.
            var saved = string.IsNullOrWhiteSpace(savedMonitors)
                ? new string[0]
                : savedMonitors.Split(',')
                    .Select(v => v.Trim())
                    .Where(v => v != "" && monitorData.Any(m => m.Value == v))
                    .ToArray();

            if (saved.Length > 0 && IsConnectedSet(saved))
                SetSelectedMonitorValues(saved);
            else if (monitorData.Count > 0)
                SetSelectedMonitorValues(new[] { monitorData[0].Value });
            else
                SetSelectedMonitorValues(new string[0]);

            statusLabel.Text = DefaultStatusText();

            UpdateResolutionOptions();
            previewPanel.Invalidate();
        }

        private string DefaultStatusText()
        {
            return string.Format(
                "Detected {0} monitor(s). Click a monitor to add or remove it from the selection (must stay adjacent).",
                monitorData.Count);
        }

        private void UpdateResolutionOptions()
        {
            comboResolution.Items.Clear();
            comboResolution.Items.Add(FullScreenResolutionOption);

            if (selectedMonitorValues.Count == 1)
            {
                var mon = monitorData.FirstOrDefault(m => m.Value == selectedMonitorValues[0]);
                if (mon != null)
                {
                    var options = TypicalResolutions
                        .Where(res => res.Width <= mon.Width && res.Height <= mon.Height)
                        .ToList();

                    var nativeSize = new Size(mon.Width, mon.Height);
                    if (!options.Contains(nativeSize))
                        options.Add(nativeSize);

                    foreach (var res in options.OrderBy(r => r.Width * r.Height))
                        comboResolution.Items.Add(res.Width + " x " + res.Height);
                }
                comboResolution.Enabled = true;
            }
            else
            {
                comboResolution.Enabled = false;
            }

            comboResolution.SelectedIndex = 0;
        }

        // Picking a specific resolution opens a plain movable window at that size rather than a
        // session tied to a monitor's bounds, so the monitor selection is no longer meaningful -
        // clear it instead of leaving a monitor highlighted that the launch won't actually use.
        private void ComboResolution_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboResolution.SelectedItem == null)
                return;
            if (comboResolution.SelectedItem.ToString() == FullScreenResolutionOption)
                return;
            if (selectedMonitorValues.Count == 0)
                return;

            SetSelectedMonitorValues(new string[0]);
            statusLabel.Text = DefaultStatusText();
            previewPanel.Invalidate();
        }

        private Size? GetSelectedCustomResolution()
        {
            if (comboResolution.SelectedItem == null)
                return null;

            var text = comboResolution.SelectedItem.ToString();
            if (text == FullScreenResolutionOption)
                return null;

            var parts = text.Split('x');
            return new Size(int.Parse(parts[0].Trim()), int.Parse(parts[1].Trim()));
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
                    g.DrawString(mon.DisplayNumber, font, textBrush, rect, sf);

                    if (mon.Primary)
                    {
                        using (var smallFont = new Font("Segoe UI", 8, FontStyle.Regular))
                        {
                            g.DrawString("Primary", smallFont, textBrush, rect.X + 6, rect.Y + 6);
                        }
                    }

                    using (var resFont = new Font("Segoe UI", 8, FontStyle.Regular))
                    using (var resSf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far })
                    {
                        g.DrawString(mon.Width + " x " + mon.Height, resFont, textBrush,
                            new RectangleF(rect.X, rect.Y, rect.Width, rect.Height - 4), resSf);
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

            string value = hit.MonitorValue;

            var candidate = selectedMonitorValues.Contains(value)
                ? selectedMonitorValues.Where(v => v != value).ToList()
                : selectedMonitorValues.Concat(new[] { value }).ToList();

            if (IsConnectedSet(candidate))
            {
                selectedMonitorValues = candidate;
                statusLabel.Text = DefaultStatusText();
            }
            else
            {
                statusLabel.Text = "Only side-by-side (adjacent) monitors can be selected together.";
                return;
            }

            UpdateResolutionOptions();
            previewPanel.Invalidate();
        }

        private void WriteRdpFile(
            string server,
            string username,
            List<MonitorInfo> selectedMonitors,
            Size? customResolution,
            bool redirectClipboard,
            bool redirectDrives,
            bool redirectPrinters,
            bool redirectWebAuthn,
            bool promptForCredentials,
            bool adminSession,
            bool spanAsPositionedWindow,
            bool downgradeCodecForTunnel)
        {
            var lines = new List<string>
            {
                "full address:s:" + server,
                "username:s:" + username,
                "prompt for credentials:i:" + (promptForCredentials ? 1 : 0),
                // Console/admin session. Inherited from the original batch script and applied
                // unconditionally, but rarely needed - and admin sessions can be refused the
                // advanced graphics pipeline, leaving the session on legacy bitmap encoding.
                "administrative session:i:" + (adminSession ? 1 : 0)
            };

            if (customResolution.HasValue)
            {
                // Windowed so smart sizing can scale the fixed desktop size within the window.
                lines.Add("screen mode id:i:1");
                lines.Add("use multimon:i:0");
                lines.Add("smart sizing:i:1");
                lines.Add("desktopwidth:i:" + customResolution.Value.Width);
                lines.Add("desktopheight:i:" + customResolution.Value.Height);
            }
            else if (selectedMonitors.Count > 1 && spanAsPositionedWindow)
            {
                // mstsc computes the spanned canvas correctly but anchors it to its own monitor 0,
                // so a span that excludes that monitor opens on the wrong screen. Dragging the
                // session onto the intended monitors fills them exactly - the size is right, only
                // the placement is wrong - and maximising sends it back, because the fault is in
                // the full-screen path. So skip that path: open a window already sized and
                // positioned over the selected monitors.
                int minX = selectedMonitors.Min(m => m.X);
                int minY = selectedMonitors.Min(m => m.Y);
                int spanWidth = selectedMonitors.Max(m => m.X + m.Width) - minX;
                int spanHeight = selectedMonitors.Max(m => m.Y + m.Height) - minY;

                lines.Add("screen mode id:i:1");
                lines.Add("use multimon:i:0");
                lines.Add("smart sizing:i:0");
                lines.Add("desktopwidth:i:" + spanWidth);
                lines.Add("desktopheight:i:" + spanHeight);
                lines.Add("winposstr:s:0,1," + minX + "," + minY + "," + (minX + spanWidth) + "," + (minY + spanHeight));
            }
            else if (selectedMonitors.Count > 1)
            {
                // Full screen is required for multimon spanning to respect physical monitor
                // boundaries - windowed mode does not, and produces a shrunk/distorted session.
                lines.Add("screen mode id:i:2");
                lines.Add("use multimon:i:1");
                lines.Add("selectedmonitors:s:" + string.Join(",", selectedMonitors.Select(m => m.Value)));

                // Anchor hint. mstsc chooses where a full-screen session lands from where its
                // own window sits, and both observed misplacements landed on the monitor the
                // launcher was on rather than the selected ones. winposstr sets that initial
                // window placement, so plant it on the selected monitors; clients that place
                // correctly anyway (msrdc) ignore the hint's monitor when going full screen.
                int minX = selectedMonitors.Min(m => m.X);
                int minY = selectedMonitors.Min(m => m.Y);
                int spanWidth = selectedMonitors.Max(m => m.X + m.Width) - minX;
                int spanHeight = selectedMonitors.Max(m => m.Y + m.Height) - minY;
                lines.Add("winposstr:s:0,1," + minX + "," + minY + "," + (minX + spanWidth) + "," + (minY + spanHeight));
            }
            else
            {
                // A single monitor, or none resolved. Never write "use multimon" with an empty
                // selectedmonitors list: the client reads that as "span everything", producing a
                // session covering every monitor - a huge canvas that is slow to the point of
                // being unusable.
                lines.Add("screen mode id:i:2");
                lines.Add("use multimon:i:0");
                if (selectedMonitors.Count == 1)
                    lines.Add("selectedmonitors:s:" + selectedMonitors[0].Value);
            }

            // State the graphics and bandwidth settings explicitly rather than relying on each
            // client's defaults.
            // videoplaybackmode is set by each branch below, not here - writing it in both
            // places emitted the key twice with opposite values, and which one a client honours
            // is undefined.
            lines.Add("compression:i:1");
            lines.Add("bitmapcachepersistenable:i:1");

            if (downgradeCodecForTunnel)
            {
                // msrdc drives an advanced hardware-accelerated codec path that cannot establish
                // through a loopback tunnel: the graphics pipeline never negotiates and its
                // receive thread stalls for seconds at a time. Asking it to skip the video
                // playback path leaves less to fall back from.
                //
                // Detection stays ON here. Turning it off was tried twice and made tunnelled
                // sessions worse - the client's own connection details then showed round-trip
                // time and available bandwidth stuck at "Calculating..." with the frame rate at
                // zero, because its rate control never receives an estimate to work from.
                lines.Add("networkautodetect:i:1");
                lines.Add("bandwidthautodetect:i:1");
                lines.Add("connection type:i:7");
                lines.Add("videoplaybackmode:i:0");

                // msrdc prefers UDP for screen updates and throttles its frame rate when the
                // transport is unavailable - which it always is through a TCP-only tunnel. The
                // multi-transport property does not stop it (the client still initiates and
                // fails the negotiation), so also ask for the rate-control protocol itself to be
                // off. Unrecognised properties are ignored by the client, so this costs nothing
                // if the name is wrong.
                lines.Add("enableurcp:i:0");
                lines.Add("usemultitransport:i:0");
            }
            else
            {
                lines.Add("networkautodetect:i:1");
                lines.Add("bandwidthautodetect:i:1");
                lines.Add("connection type:i:7");
                lines.Add("videoplaybackmode:i:1");
            }

            lines.Add("redirectclipboard:i:" + (redirectClipboard ? 1 : 0));
            lines.Add("disableclipboardredirection:i:" + (redirectClipboard ? 0 : 1));
            lines.Add("redirectprinters:i:" + (redirectPrinters ? 1 : 0));
            lines.Add("redirectdrives:i:" + (redirectDrives ? 1 : 0));
            if (redirectDrives)
                lines.Add("drivestoredirect:s:*");
            lines.Add("redirectwebauthn:i:" + (redirectWebAuthn ? 1 : 0));
            lines.Add("redirectsmartcards:i:0");

            File.WriteAllLines(rdpFile, lines, System.Text.Encoding.ASCII);
        }

        // True when the session goes through a port forward on this machine - a jump client or
        // gateway listening on loopback - rather than straight to the host.
        private static bool IsTunnelledAddress(string server)
        {
            if (string.IsNullOrWhiteSpace(server))
                return false;

            // Strip any port, and the brackets IPv6 literals are written with.
            var host = server.Trim();
            if (host.StartsWith("["))
            {
                var close = host.IndexOf(']');
                if (close > 0)
                    host = host.Substring(1, close - 1);
            }
            else
            {
                var colon = host.IndexOf(':');
                if (colon > 0)
                    host = host.Substring(0, colon);
            }

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "::1")
                return true;

            System.Net.IPAddress address;
            return System.Net.IPAddress.TryParse(host, out address) &&
                   System.Net.IPAddress.IsLoopback(address);
        }

        private void ButtonDetect_Click(object sender, EventArgs e)
        {
            var saved = GetSelectedMonitorString();
            LoadMonitors(saved);
        }

        private void ButtonAll_Click(object sender, EventArgs e)
        {
            var allValues = monitorData.Select(m => m.Value).ToList();

            if (IsConnectedSet(allValues))
            {
                SetSelectedMonitorValues(allValues);
                statusLabel.Text = DefaultStatusText();
            }
            else
            {
                SetSelectedMonitorValues(LargestConnectedGroup());
                statusLabel.Text = "Monitors aren't all adjacent; selected the largest connected group instead.";
            }

            UpdateResolutionOptions();
            previewPanel.Invalidate();
        }

        private void ButtonClear_Click(object sender, EventArgs e)
        {
            SetSelectedMonitorValues(new string[0]);
            statusLabel.Text = DefaultStatusText();
            UpdateResolutionOptions();
            previewPanel.Invalidate();
        }

        private void ButtonLaunch_Click(object sender, EventArgs e)
        {
            var server = comboServer.Text.Trim();
            var username = textUser.Text.Trim();
            var password = textPassword.Text;
            var selectedMonitors = GetSelectedMonitorString();

            if (string.IsNullOrWhiteSpace(server) && string.IsNullOrWhiteSpace(username))
            {
                if (File.Exists(rdpFile))
                {
                    // Reusing the existing .rdp as written - no spanning decision to make here.
                    LaunchRdp(null, SelectClient(comboServer.Text.Trim(), false));
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

            var customResolution = GetSelectedCustomResolution();

            if (customResolution == null && string.IsNullOrWhiteSpace(selectedMonitors))
            {
                MessageBox.Show("Please select at least one monitor.", "Validation");
                return;
            }

            bool wantAll = checkAutoClickAll.Checked;
            bool redirectDrives = wantAll || checkAutoClickDrives.Checked;
            bool redirectPrinters = wantAll || checkAutoClickPrinters.Checked;
            bool redirectWebAuthn = wantAll || checkAutoClickWebAuthn.Checked;
            bool redirectClipboard = wantAll || checkAutoClickClipboard.Checked;

            // Choose the client before writing the file: mstsc needs the spanning workaround,
            // msrdc does not, so the .rdp has to be written to suit whichever will run.
            bool multiMonitorSpan = selectedMonitorValues.Count > 1 && customResolution == null;
            var clientSelection = SelectClient(server, multiMonitorSpan);

            // Opt-in only. It fixes mstsc placing a spanned session on the wrong monitor, but the
            // result is a fixed-size window: it carries a title bar and cannot be made full
            // screen, so it is not a straight improvement and must not be applied unasked.
            bool spanAsPositionedWindow =
                multiMonitorSpan && checkWindowedSpan.Checked && !clientSelection.IsMsrdc;

            // Only msrdc needs this, and only through a tunnel: mstsc's legacy path performs
            // fine there, so leave its settings alone.
            bool downgradeCodecForTunnel = clientSelection.IsMsrdc && IsTunnelledAddress(server);

            bool hasPassword = !string.IsNullOrEmpty(password);
            string stagedCredentialServer = null;
            if (hasPassword && CredentialStagingService.Stage(server, username, password))
                stagedCredentialServer = server;

            SettingsService.Save(
                settingsFile,
                selectedMonitors,
                checkAutoConnect.Checked,
                checkAutoClickAll.Checked,
                checkAutoClickWebAuthn.Checked,
                checkAutoClickDrives.Checked,
                checkAutoClickClipboard.Checked,
                checkAutoClickPrinters.Checked,
                SelectedClientSetting(),
                checkAdminSession.Checked,
                checkWindowedSpan.Checked);

            WriteRdpFile(
                server,
                username,
                GetSelectedMonitors(),
                customResolution,
                redirectClipboard,
                redirectDrives,
                redirectPrinters,
                redirectWebAuthn,
                !hasPassword,
                checkAdminSession.Checked,
                spanAsPositionedWindow,
                downgradeCodecForTunnel);

            ServerHistoryService.Add(historyFile, server);
            RefreshServerHistoryItems();
            comboServer.Text = server;

            LaunchRdp(stagedCredentialServer, clientSelection);
        }

        private void CollectDiagnosticsInBackground(string launchedClient, string reason)
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    // Wait for the connection to actually happen before collecting. The events
                    // that matter most - graphics protocol version, whether AVC is available and
                    // whether the frame buffer is in hardware or software memory - are only
                    // written once the session negotiates, so collecting at launch would miss
                    // precisely the evidence needed to explain a slow session.
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(45));

                    DiagnosticsService.Collect(appDataDir, rdpFile, settingsFile, launchedClient, reason);
                }
                catch
                {
                    // Diagnostics are best effort - never interfere with the session
                }
            });
        }

        private void ButtonDiagnostics_Click(object sender, EventArgs e)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                var path = DiagnosticsService.Collect(
                    appDataDir, rdpFile, settingsFile, "(not launched - collected on demand)", "manual");

                statusLabel.Text = "Diagnostics written to " + Path.GetFileName(path);
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not write diagnostics:\n\n" + ex.Message,
                    "Diagnostics failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private string SelectedClientSetting()
        {
            var choice = comboClient.SelectedItem as string;
            if (choice == ClientMstsc) return "mstsc";
            if (choice == ClientMsrdc) return "msrdc";
            return "auto";
        }

        private class ClientSelection
        {
            public string Path = "mstsc.exe";
            public bool IsMsrdc;
            public string Reason;
        }

        // Decided before the .rdp is written, because the file has to suit the client: mstsc
        // needs the spanning workaround below, msrdc does not.
        private ClientSelection SelectClient(string targetAddress, bool multiMonitorSpan)
        {
            var selection = new ClientSelection();
            var clientChoice = SelectedClientSetting();
            bool tunnelled = IsTunnelledAddress(targetAddress);

            if (clientChoice == "mstsc")
            {
                selection.Reason = "pinned by the client selector";
            }
            else if (clientChoice == "auto" && tunnelled)
            {
                // Sessions through a local port forward - a jump client or gateway on loopback -
                // carry no UDP, so RDP's multi-transport negotiation always fails. msrdc handles
                // that badly: its receive thread stalls for seconds at a time, the modern
                // graphics pipeline never negotiates, and the session falls back to legacy
                // bitmap encoding that repaints progressively. mstsc tolerates the same failure.
                selection.Reason = "automatic: tunnelled target (loopback), which msrdc handles poorly";
            }
            else if (clientChoice == "msrdc")
            {
                var pinned = RdpClientLocator.FindMsrdc();
                if (pinned != null)
                {
                    selection.Path = pinned;
                    selection.IsMsrdc = true;
                    selection.Reason = "pinned by the client selector";
                }
                else
                {
                    selection.Reason = "pinned to msrdc, which was not found - fell back to mstsc";
                    MessageBox.Show(
                        "The Remote Desktop client (msrdc) was selected but could not be found.\n\n" +
                        "Falling back to the built-in client (mstsc).",
                        "Remote Desktop client not found",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            else if (multiMonitorSpan && DisplayScalingService.HasMixedScaling())
            {
                var msrdcPath = RdpClientLocator.FindMsrdc();
                if (msrdcPath != null)
                {
                    selection.Path = msrdcPath;
                    selection.IsMsrdc = true;
                    selection.Reason = "automatic: mixed display scaling with a spanned session";
                }
                else
                {
                    selection.Reason = "automatic: mixed scaling, msrdc unavailable - mstsc with the spanning workaround";
                }
            }
            else
            {
                selection.Reason = "automatic: no mixed-scaling span detected";
            }

            return selection;
        }

        private void LaunchRdp(string stagedCredentialServer, ClientSelection selection)
        {
            var clientPath = selection.Path;
            bool usingMsrdc = selection.IsMsrdc;

            var checkboxNames = new List<string> { "Don't ask me again for connections to this computer" };
            bool toggleAll = checkAutoConnect.Checked && checkAutoClickAll.Checked;

            if (!toggleAll)
            {
                if (checkAutoClickWebAuthn.Checked) checkboxNames.Add("WebAuthn");
                if (checkAutoClickDrives.Checked) checkboxNames.Add("Drives");
                if (checkAutoClickClipboard.Checked) checkboxNames.Add("Clipboard");
                if (checkAutoClickPrinters.Checked) checkboxNames.Add("Printers");
            }

            try
            {
                var process = RdpAutoConnectService.Connect(
                    clientPath, rdpFile, checkAutoConnect.Checked, checkboxNames.ToArray(), toggleAll);

                if (stagedCredentialServer != null)
                {
                    if (usingMsrdc)
                    {
                        // msrdc's launcher process can hand the session to another process and
                        // exit, so process exit is not a reliable "session ended" signal here.
                        CredentialStagingService.RemoveAfterDelay(stagedCredentialServer, TimeSpan.FromMinutes(2));
                    }
                    else if (process != null)
                    {
                        process.EnableRaisingEvents = true;
                        process.Exited += (s, e) => CredentialStagingService.Remove(stagedCredentialServer);
                    }
                }

                // Always report which client ran and where it came from - "which client is this?"
                // is otherwise guesswork, and the answer decides where to look when a session
                // misbehaves.
                statusLabel.Text = usingMsrdc
                    ? "Launched with Remote Desktop client: " + clientPath
                    : "Launched with the built-in client (mstsc.exe).";

                // Record the state that produced this session, so a session that turns out to be
                // slow or wrong can be diagnosed after the fact. Collection touches WMI and the
                // event log, so keep it off the UI thread and never let it disturb the launch.
                CollectDiagnosticsInBackground(clientPath, selection.Reason);
            }
            catch (Exception ex)
            {
                if (stagedCredentialServer != null)
                    CredentialStagingService.Remove(stagedCredentialServer);

                MessageBox.Show(
                    "Could not start the Remote Desktop connection:\n\n" + ex.Message,
                    "Launch failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
