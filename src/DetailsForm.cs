using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTemps
{
    internal sealed class DetailsForm : Form
    {
        private readonly Label[] _summary = new Label[6];
        private readonly ListView _list;
        private readonly Label _status;

        private SensorSnapshot _snapshot;
        private string _signature = string.Empty;
        private DateTime _copiedUntil = DateTime.MinValue;

        public DetailsForm(Icon icon)
        {
            Text = "TaskbarTemps · sensores";
            if (icon != null) Icon = icon;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = false;
            TopMost = true;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;
            ClientSize = new Size(700, 566);
            MinimumSize = new Size(580, 420);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 204f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));

            TableLayoutPanel summary = new TableLayoutPanel();
            summary.Dock = DockStyle.Fill;
            summary.Padding = new Padding(14, 8, 14, 6);
            summary.ColumnCount = 2;
            summary.RowCount = 6;
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 5f));
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 6; i++) summary.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 6f));

            Color[] accents = new Color[]
            {
                Color.FromArgb(37, 99, 235),
                Color.FromArgb(124, 58, 237),
                Color.FromArgb(5, 150, 105),
                Color.FromArgb(217, 119, 6),
                Color.FromArgb(8, 145, 178),
                Color.FromArgb(71, 85, 105)
            };

            for (int i = 0; i < 6; i++)
            {
                Panel accent = new Panel();
                accent.Dock = DockStyle.Fill;
                accent.Margin = new Padding(0, 4, 10, 4);
                accent.BackColor = accents[i];

                Label label = new Label();
                label.Dock = DockStyle.Fill;
                label.Margin = new Padding(0);
                label.Padding = new Padding(2, 0, 0, 0);
                label.TextAlign = ContentAlignment.MiddleLeft;
                label.AutoEllipsis = true;
                label.AutoSize = false;
                label.Font = new Font("Segoe UI", 9f);
                label.ForeColor = Color.FromArgb(32, 36, 44);

                summary.Controls.Add(accent, 0, i);
                summary.Controls.Add(label, 1, i);
                _summary[i] = label;
            }

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(14, 0, 14, 0);
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = false;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.Columns.Add("Componente", 190);
            _list.Columns.Add("Tipo", 100);
            _list.Columns.Add("Sensor", 240);
            _list.Columns.Add("Valor", 130);

            TableLayoutPanel bottom = new TableLayoutPanel();
            bottom.Dock = DockStyle.Fill;
            bottom.ColumnCount = 3;
            bottom.RowCount = 1;
            bottom.Padding = new Padding(14, 8, 14, 8);
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _status = new Label();
            _status.Dock = DockStyle.Fill;
            _status.Margin = new Padding(0);
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.AutoEllipsis = true;
            _status.ForeColor = Color.FromArgb(96, 102, 112);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.WrapContents = false;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Margin = new Padding(0);

            Button close = new Button();
            close.Text = "Fechar";
            close.Width = 92;
            close.Height = 30;
            close.Margin = new Padding(8, 0, 0, 0);
            close.FlatStyle = FlatStyle.System;
            close.Click += delegate { Close(); };

            Button copy = new Button();
            copy.Text = "Copiar";
            copy.Width = 92;
            copy.Height = 30;
            copy.Margin = new Padding(0);
            copy.FlatStyle = FlatStyle.System;
            copy.Click += delegate { CopyReport(); };

            buttons.Controls.Add(close);
            buttons.Controls.Add(copy);

            FlowLayoutPanel credit = new FlowLayoutPanel();
            credit.AutoSize = true;
            credit.WrapContents = false;
            credit.FlowDirection = FlowDirection.LeftToRight;
            credit.Anchor = AnchorStyles.Left;
            credit.Margin = new Padding(0, 0, 10, 0);

            Label creditLabel = new Label();
            creditLabel.Text = "Desenvolvido por";
            creditLabel.AutoSize = true;
            creditLabel.ForeColor = Color.FromArgb(140, 146, 156);
            creditLabel.Margin = new Padding(0, 4, 4, 0);

            LinkLabel creditLink = new LinkLabel();
            creditLink.Text = "mamfo.net";
            creditLink.AutoSize = true;
            creditLink.LinkColor = Color.FromArgb(37, 99, 235);
            creditLink.ActiveLinkColor = Color.FromArgb(29, 78, 216);
            creditLink.Margin = new Padding(0, 4, 0, 0);
            creditLink.LinkClicked += delegate { OpenWebsite("https://www.mamfo.net"); };

            credit.Controls.Add(creditLabel);
            credit.Controls.Add(creditLink);

            bottom.Controls.Add(_status, 0, 0);
            bottom.Controls.Add(credit, 1, 0);
            bottom.Controls.Add(buttons, 2, 0);

            root.Controls.Add(summary, 0, 0);
            root.Controls.Add(_list, 0, 1);
            root.Controls.Add(bottom, 0, 2);
            Controls.Add(root);

            KeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };
        }

        public void SetSnapshot(SensorSnapshot snapshot)
        {
            if (IsDisposed || snapshot == null) return;
            _snapshot = snapshot;

            _summary[0].Text = DisplayFormat.DetailSummaryCpu(snapshot);
            _summary[1].Text = DisplayFormat.DetailSummaryRam(snapshot);
            _summary[2].Text = DisplayFormat.DetailSummaryGpu(snapshot);
            _summary[3].Text = DisplayFormat.DetailSummaryDisk(snapshot);
            _summary[4].Text = DisplayFormat.DetailSummaryNetwork(snapshot);
            _summary[5].Text = DisplayFormat.DetailSummaryIp(snapshot);

            UpdateRows(snapshot);
            UpdateStatus(snapshot);
        }

        private void UpdateRows(SensorSnapshot snapshot)
        {
            StringBuilder signature = new StringBuilder();
            foreach (SensorRow row in snapshot.Rows)
            {
                signature.Append(row.Hardware).Append('\u001f')
                    .Append(row.Type).Append('\u001f')
                    .Append(row.Name).Append('\u001e');
            }
            string sig = signature.ToString();

            if (sig != _signature)
            {
                _list.BeginUpdate();
                try
                {
                    _list.Items.Clear();
                    foreach (SensorRow row in snapshot.Rows)
                    {
                        ListViewItem item = new ListViewItem(row.Hardware);
                        item.SubItems.Add(row.Type);
                        item.SubItems.Add(row.Name);
                        item.SubItems.Add(row.Value);
                        _list.Items.Add(item);
                    }
                }
                finally
                {
                    _list.EndUpdate();
                }
                _signature = sig;
            }
            else if (_list.Items.Count == snapshot.Rows.Count)
            {
                for (int i = 0; i < snapshot.Rows.Count; i++)
                {
                    if (_list.Items[i].SubItems.Count >= 4)
                    {
                        _list.Items[i].SubItems[3].Text = snapshot.Rows[i].Value;
                    }
                }
            }
        }

        private void UpdateStatus(SensorSnapshot snapshot)
        {
            if (DateTime.Now < _copiedUntil)
            {
                _status.Text = "Relatório copiado para a área de transferência.";
                _status.ForeColor = Color.FromArgb(5, 150, 105);
                return;
            }

            string when = snapshot.Updated == DateTime.MinValue
                ? "—"
                : snapshot.Updated.ToString("HH:mm:ss");

            string admin;
            if (!snapshot.IsAdmin)
            {
                admin = "sem elevação — temperatura do CPU indisponível";
            }
            else if (!snapshot.CpuTemp.HasValue)
            {
                admin = "administrador — temperatura do CPU indisponível (é necessário o PawnIO)";
            }
            else
            {
                admin = "administrador";
            }

            string error = string.IsNullOrEmpty(snapshot.Error) ? string.Empty : "Aviso: " + snapshot.Error + "   ·   ";
            _status.Text = error + admin + "   ·   " + snapshot.Rows.Count + " sensores   ·   " + when;
            _status.ForeColor = snapshot.IsAdmin && snapshot.CpuTemp.HasValue
                ? Color.FromArgb(96, 102, 112)
                : Color.FromArgb(180, 83, 9);
        }

        private static void OpenWebsite(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Não foi possível abrir o site:\n" + ex.Message, "TaskbarTemps",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CopyReport()
        {
            if (_snapshot == null) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("TaskbarTemps — relatório de sensores");
                sb.AppendLine("Data: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Administrador: " + (_snapshot.IsAdmin ? "sim" : "não"));
                if (!string.IsNullOrEmpty(_snapshot.Error)) sb.AppendLine("Aviso: " + _snapshot.Error);
                sb.AppendLine();
                sb.AppendLine(_summary[0].Text.Replace(Environment.NewLine, " | "));
                sb.AppendLine(_summary[1].Text.Replace(Environment.NewLine, " | "));
                sb.AppendLine(_summary[2].Text.Replace(Environment.NewLine, " | "));
                sb.AppendLine(_summary[3].Text.Replace(Environment.NewLine, " | "));
                sb.AppendLine(_summary[4].Text.Replace(Environment.NewLine, " | "));
                sb.AppendLine(_summary[5].Text.Replace(Environment.NewLine, " | "));
                sb.AppendLine();
                foreach (SensorRow row in _snapshot.Rows)
                {
                    sb.AppendLine(row.Hardware + "\t" + row.Type + "\t" + row.Name + "\t" + row.Value);
                }
                Clipboard.SetText(sb.ToString());
                _copiedUntil = DateTime.Now.AddSeconds(2);
                UpdateStatus(_snapshot);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível copiar:\n" + ex.Message, "TaskbarTemps",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void ShowNear(IntPtr barHandle)
        {
            Native.RECT bar;
            if (Native.GetWindowRect(barHandle, out bar))
            {
                Rectangle area = Screen.FromHandle(barHandle).WorkingArea;
                int x = Math.Min(Math.Max(area.Left + 8, bar.Right - Width), area.Right - Width - 8);
                int y = bar.Top - Height - 10;
                if (y < area.Top + 8) y = bar.Bottom + 10;
                Location = new Point(x, y);
            }

            if (!Visible) Show();
            else Activate();
        }
    }
}
