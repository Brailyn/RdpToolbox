using RdpToolbox.Services;
using System.Drawing;
using System.Windows.Forms;

namespace RdpToolbox
{
    public class ServerHistoryForm : Form
    {
        private readonly string historyFile;
        private FlowLayoutPanel listPanel;

        public string SelectedServer { get; private set; }

        public ServerHistoryForm(string historyFile)
        {
            this.historyFile = historyFile;
            InitializeComponent();
            LoadEntries();
        }

        private void InitializeComponent()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Server History";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(460, 420);
            MinimumSize = new Size(360, 260);
            MaximizeBox = false;

            listPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(10)
            };
            Controls.Add(listPanel);

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            var buttonClose = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                Size = new Size(90, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(350, 8)
            };
            bottomPanel.Controls.Add(buttonClose);
            Controls.Add(bottomPanel);

            CancelButton = buttonClose;
        }

        private void LoadEntries()
        {
            var entries = ServerHistoryService.Load(historyFile);
            listPanel.Controls.Clear();

            if (entries.Count == 0)
            {
                listPanel.Controls.Add(new Label
                {
                    Text = "No saved servers yet.",
                    AutoSize = true,
                    Padding = new Padding(4)
                });
                return;
            }

            foreach (var server in entries)
            {
                listPanel.Controls.Add(BuildRow(server));
            }
        }

        private Control BuildRow(string server)
        {
            var row = new Panel { Size = new Size(410, 32), Margin = new Padding(0, 0, 0, 4) };

            var label = new Label
            {
                Text = server,
                AutoEllipsis = true,
                Size = new Size(230, 24),
                Location = new Point(0, 4),
                TextAlign = ContentAlignment.MiddleLeft
            };
            row.Controls.Add(label);

            var buttonUse = new Button { Text = "Use", Location = new Point(235, 2), Size = new Size(70, 26) };
            buttonUse.Click += (s, e) =>
            {
                SelectedServer = server;
                DialogResult = DialogResult.OK;
                Close();
            };
            row.Controls.Add(buttonUse);

            var buttonDelete = new Button { Text = "Delete", Location = new Point(310, 2), Size = new Size(70, 26) };
            buttonDelete.Click += (s, e) =>
            {
                ServerHistoryService.Remove(historyFile, server);
                LoadEntries();
            };
            row.Controls.Add(buttonDelete);

            return row;
        }
    }
}
