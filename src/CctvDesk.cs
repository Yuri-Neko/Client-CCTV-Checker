using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace CctvReportPortable
{
    internal sealed class CsvDocument
    {
        public string Path;
        public char Delimiter;
        public List<string> Headers;
        public List<List<string>> Rows;

        public static CsvDocument Load(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF');
            string first = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None).FirstOrDefault() ?? "";
            char delimiter = new[] { ',', ';', '\t' }.OrderByDescending(c => first.Count(x => x == c)).First();
            List<List<string>> records = Parse(text, delimiter);
            if (records.Count == 0 || records[0].Count == 0) throw new InvalidDataException("CSV tidak memiliki header.");
            return new CsvDocument { Path = path, Delimiter = delimiter, Headers = records[0], Rows = records.Skip(1).Where(r => r.Any(v => v.Length > 0)).ToList() };
        }

        static List<List<string>> Parse(string text, char delimiter)
        {
            var all = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < text.Length; i++) {
                char c = text[i];
                if (quoted) {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else if (c == '"') quoted = false; else field.Append(c);
                } else if (c == '"' && field.Length == 0) quoted = true;
                else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\r' || c == '\n') { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; row.Add(field.ToString()); field.Clear(); all.Add(row); row = new List<string>(); }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); all.Add(row); }
            return all;
        }

        public string BackupAndSave(DataGridView grid)
        {
            var sb = new StringBuilder();
            for (int c = 0; c < grid.Columns.Count; c++) { if (c > 0) sb.Append(Delimiter); sb.Append(Escape(grid.Columns[c].HeaderText, Delimiter)); }
            sb.AppendLine();
            foreach (DataGridViewRow r in grid.Rows) {
                if (r.IsNewRow) continue;
                for (int c = 0; c < grid.Columns.Count; c++) { if (c > 0) sb.Append(Delimiter); sb.Append(Escape(Convert.ToString(r.Cells[c].Value, CultureInfo.InvariantCulture) ?? "", Delimiter)); }
                sb.AppendLine();
            }
            string backup = Path + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            File.Copy(Path, backup, false);
            string temp = Path + ".tmp";
            File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(true));
            File.Replace(temp, Path, null);
            return backup;
        }

        static string Escape(string value, char delimiter)
        {
            if (value.IndexOfAny(new[] { delimiter, '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }

    internal sealed class MainForm : Form
    {
        static readonly Color Ink = Color.FromArgb(29, 45, 51);
        static readonly Color Muted = Color.FromArgb(90, 108, 112);
        static readonly Color Canvas = Color.FromArgb(241, 244, 242);
        static readonly Color Paper = Color.FromArgb(255, 255, 255);
        static readonly Color Rail = Color.FromArgb(231, 237, 234);
        static readonly Color Border = Color.FromArgb(211, 221, 217);
        static readonly Color Teal = Color.FromArgb(19, 107, 92);
        static readonly Color TealPale = Color.FromArgb(224, 240, 235);
        static readonly Color Amber = Color.FromArgb(143, 95, 14);
        static readonly Color Navy = Color.FromArgb(25, 50, 58);
        readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        readonly ConcurrentQueue<string> output = new ConcurrentQueue<string>();
        readonly RichTextBox logBox = new RichTextBox();
        readonly ProgressBar progress = new ProgressBar();
        readonly Label statusText = new Label();
        readonly Label dataSummary = new Label();
        readonly Button runNav = new Button(), dataNav = new Button();
        readonly Button runButton = new Button(), stopButton = new Button();
        readonly Button camerasNav = new Button(), nvrNav = new Button();
        readonly Button saveButton = new Button(), reloadButton = new Button();
        readonly Button browseFolderButton = new Button();
        readonly Button licenseButton = new Button();
        readonly ComboBox reportType = new ComboBox();
        readonly TextBox nvrIp = new TextBox(), officer = new TextBox(), reportFolder = new TextBox();
        readonly ToolTip pathTip = new ToolTip();
        readonly CheckBox fast = new CheckBox(), fill = new CheckBox();
        readonly CheckBox showPasswords = new CheckBox();
        readonly DataGridView camerasGrid = new DataGridView(), nvrGrid = new DataGridView();
        readonly Panel runPage = new Panel(), dataPage = new Panel();
        CsvDocument camerasDoc, nvrDoc;
        bool showCameras = true;
        Process child;
        System.Windows.Forms.Timer poll;
        string logPath;
        string outputPath;
        DateTime runStartedUtc;

        public MainForm()
        {
            Text = "CCTV Report Desk"; StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 680); Size = new Size(1160, 780);
            BackColor = Canvas; ForeColor = Ink; Font = new Font("Segoe UI", 9.5F);
            BuildInterface(); LoadCsvs();
            poll = new System.Windows.Forms.Timer(); poll.Interval = 140; poll.Tick += DrainOutput; poll.Start();
            FormClosing += (s, e) => { try { if (child != null && !child.HasExited) child.Kill(); } catch { } };
        }

        void BuildInterface()
        {
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Canvas };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 82)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(shell);
            var header = new Panel { Dock = DockStyle.Fill, BackColor = Navy };
            header.Controls.Add(new Label { Text = "CCTV Report Desk", Font = new Font("Segoe UI Semibold", 19F, FontStyle.Bold), ForeColor = Color.White, Location = new Point(26, 13), AutoSize = true });
            header.Controls.Add(new Label { Text = "Hikvision · laporan dan data perangkat", Font = new Font("Segoe UI", 9.5F), ForeColor = Color.FromArgb(201, 218, 215), Location = new Point(28, 49), AutoSize = true });
            shell.Controls.Add(header, 0, 0); shell.SetColumnSpan(header, 2);

            var rail = new Panel { Dock = DockStyle.Fill, BackColor = Rail, Padding = new Padding(14, 23, 14, 14) };
            rail.Controls.Add(new Label { Text = "RUANG KERJA", Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold), ForeColor = Muted, Location = new Point(16, 16), AutoSize = true });
            runNav.Text = "Laporan"; dataNav.Text = "Data kamera & NVR";
            SetupNav(runNav); SetupNav(dataNav); runNav.Location = new Point(8, 43); runNav.Size = new Size(174, 42); dataNav.Location = new Point(8, 91); dataNav.Size = new Size(174, 42);
            runNav.Click += (s,e) => ShowPage(false); dataNav.Click += (s,e) => ShowPage(true);
            rail.Controls.Add(runNav); rail.Controls.Add(dataNav);
            rail.Controls.Add(new Label { Text = "DATA LOKAL", Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold), ForeColor = Muted, Location = new Point(16, 163), AutoSize = true });
            rail.Controls.Add(new Label { Text = "CSV dan hasil laporan\ntersimpan di folder ini.", Font = new Font("Segoe UI", 9F), ForeColor = Muted, Location = new Point(16, 187), AutoSize = true });
            licenseButton.Text = "Ganti license key"; licenseButton.Size = new Size(174, 36); licenseButton.Location = new Point(8, 244); StyleButton(licenseButton, Paper, Ink, false); licenseButton.Click += (s,e) => ChangeLicenseKey(); rail.Controls.Add(licenseButton);
            shell.Controls.Add(rail, 0, 1);

            var contentHost = new Panel { Dock = DockStyle.Fill, BackColor = Canvas, Padding = new Padding(25, 21, 25, 22) };
            runPage.Dock = DockStyle.Fill; dataPage.Dock = DockStyle.Fill; contentHost.Controls.Add(runPage); contentHost.Controls.Add(dataPage); shell.Controls.Add(contentHost, 1, 1);
            BuildRunPage(); BuildDataPage(); ShowPage(false);
        }

        void BuildRunPage()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Canvas };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 218));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); runPage.Controls.Add(layout);
            var title = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
            title.Controls.Add(new Label { Text = "Jalankan pemeriksaan", Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold), ForeColor = Ink, Location = new Point(0, 0), AutoSize = true });
            title.Controls.Add(new Label { Text = "Hasil asli tetap di aplikasi; file baru juga disalin ke folder pilihan.", Font = new Font("Segoe UI", 9.5F), ForeColor = Muted, Location = new Point(2, 33), AutoSize = true }); layout.Controls.Add(title, 0, 0);

            var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Padding = new Padding(14, 8, 14, 4), BackColor = Paper, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); form.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); form.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); layout.Controls.Add(form, 0, 1);
            AddFieldLabel(form, "Jenis laporan", 0, 0); reportType.DropDownStyle = ComboBoxStyle.DropDownList;
            reportType.Items.AddRange(new object[] { "Laporan rekaman (Excel)", "Daily check (Word)", "Cek data kamera" }); reportType.SelectedIndex = 0; StyleInput(reportType); form.Controls.Add(reportType, 1, 0);
            AddFieldLabel(form, "IP NVR", 2, 0); nvrIp.MaxLength = 64; StyleInput(nvrIp); form.Controls.Add(nvrIp, 3, 0);
            AddFieldLabel(form, "Petugas", 0, 1); StyleInput(officer); form.Controls.Add(officer, 1, 1);
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Paper, WrapContents = false, Padding = new Padding(0, 15, 0, 0) };
            fast.Text = "Mode cepat"; fill.Text = "Isi data kosong"; StyleCheck(fast); StyleCheck(fill); options.Controls.Add(fast); options.Controls.Add(fill); form.Controls.Add(options, 2, 1); form.SetColumnSpan(options, 2);
            AddFieldLabel(form, "Salin hasil ke", 0, 2); reportFolder.ReadOnly = true; StyleInput(reportFolder); form.Controls.Add(reportFolder, 1, 2); form.SetColumnSpan(reportFolder, 2);
            browseFolderButton.Text = "Pilih folder…"; browseFolderButton.Size = new Size(116, 36); browseFolderButton.Anchor = AnchorStyles.Left; browseFolderButton.Margin = new Padding(2, 10, 4, 8); StyleButton(browseFolderButton, Paper, Ink, false); browseFolderButton.Click += BrowseReportFolder; form.Controls.Add(browseFolderButton, 3, 2);
            string savedFolder = Path.Combine(root, "output-folder.txt");
            outputPath = File.Exists(savedFolder) ? File.ReadAllText(savedFolder, Encoding.UTF8).Trim() : Path.Combine(root, "Report CCTV");
            ShowOutputPath();

            var actionRow = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
            runButton.Text = "Jalankan laporan"; runButton.Size = new Size(158, 38); runButton.Location = new Point(0, 9); StyleButton(runButton, Teal, Color.White, true); runButton.Click += StartRun; actionRow.Controls.Add(runButton);
            stopButton.Text = "Hentikan"; stopButton.Size = new Size(100, 38); stopButton.Location = new Point(168, 9); StyleButton(stopButton, Paper, Ink, false); stopButton.Enabled = false; stopButton.Click += StopRun; actionRow.Controls.Add(stopButton);
            var open = new Button { Text = "Buka hasil" , Size = new Size(108, 38), Location = new Point(278, 9) }; StyleButton(open, Paper, Ink, false); open.Click += (s,e) => { string p = Path.Combine(root, "Report CCTV"); Directory.CreateDirectory(p); Process.Start("explorer.exe", p); }; actionRow.Controls.Add(open);
            statusText.Text = "Siap"; statusText.ForeColor = Teal; statusText.AutoSize = false; statusText.TextAlign = ContentAlignment.MiddleRight; statusText.Dock = DockStyle.Right; statusText.Width = 240; actionRow.Controls.Add(statusText);
            progress.Visible = false; progress.Width = 190; progress.Height = 16; progress.Anchor = AnchorStyles.Right | AnchorStyles.Top; progress.Location = new Point(450, 20); actionRow.Controls.Add(progress); layout.Controls.Add(actionRow, 0, 2);

            var logs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Paper, Padding = new Padding(14, 10, 14, 12) };
            logs.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); logs.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.Controls.Add(logs, 0, 3);
            var logHeader = new Panel { Dock = DockStyle.Fill, BackColor = Paper };
            logHeader.Controls.Add(new Label { Text = "Log proses", Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold), ForeColor = Ink, Dock = DockStyle.Left, Width = 140, TextAlign = ContentAlignment.MiddleLeft });
            var clear = new LinkLabel { Text = "Bersihkan log", LinkColor = Teal, ActiveLinkColor = Teal, Dock = DockStyle.Right, Width = 110, TextAlign = ContentAlignment.MiddleRight }; clear.Click += (s,e) => logBox.Clear(); logHeader.Controls.Add(clear); logs.Controls.Add(logHeader, 0, 0);
            logBox.Dock = DockStyle.Fill; logBox.BackColor = Color.FromArgb(248, 250, 249); logBox.ForeColor = Ink; logBox.BorderStyle = BorderStyle.FixedSingle; logBox.Font = new Font("Consolas", 9F); logBox.ReadOnly = true;
            logBox.Text = "Belum ada proses. Log akan muncul di sini saat laporan berjalan."; logBox.ForeColor = Muted; logs.Controls.Add(logBox, 0, 1);
            reportType.SelectedIndexChanged += (s,e) => UpdateReportOptions(); UpdateReportOptions();
        }

        void BuildDataPage()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Canvas };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); dataPage.Controls.Add(layout);
            var title = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
            title.Controls.Add(new Label { Text = "Data kamera & NVR", Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold), ForeColor = Ink, Location = new Point(0, 0), AutoSize = true });
            title.Controls.Add(new Label { Text = "Edit file CSV yang ikut di folder portable aplikasi.", Font = new Font("Segoe UI", 9.5F), ForeColor = Muted, Location = new Point(2, 34), AutoSize = true }); layout.Controls.Add(title, 0, 0);

            var toolbar = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
            camerasNav.Text = "Kamera"; nvrNav.Text = "NVR"; camerasNav.Size = new Size(88, 34); nvrNav.Size = new Size(88, 34); camerasNav.Location = new Point(0, 4); nvrNav.Location = new Point(92, 4);
            camerasNav.Click += (s,e) => SelectCsv(true); nvrNav.Click += (s,e) => SelectCsv(false); toolbar.Controls.Add(camerasNav); toolbar.Controls.Add(nvrNav);
            showPasswords.Text = "Lihat password"; showPasswords.Size = new Size(136, 32); showPasswords.Location = new Point(190, 5); showPasswords.Visible = false; StyleCheck(showPasswords); showPasswords.CheckedChanged += (s,e) => nvrGrid.Refresh(); toolbar.Controls.Add(showPasswords);
            saveButton.Text = "Simpan"; saveButton.Size = new Size(100, 34); saveButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; saveButton.Location = new Point(Math.Max(0, toolbar.Width - 216), 4); StyleButton(saveButton, Teal, Color.White, true); saveButton.Click += (s,e) => SaveCsv();
            reloadButton.Text = "Muat ulang"; reloadButton.Size = new Size(104, 34); reloadButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; reloadButton.Location = new Point(Math.Max(0, toolbar.Width - 108), 4); StyleButton(reloadButton, Paper, Ink, false); reloadButton.Click += (s,e) => LoadCsvs();
            toolbar.Resize += (s,e) => { saveButton.Left = toolbar.ClientSize.Width - 216; reloadButton.Left = toolbar.ClientSize.Width - 108; };
            toolbar.Controls.Add(saveButton); toolbar.Controls.Add(reloadButton); layout.Controls.Add(toolbar, 0, 1);

            StyleGrid(camerasGrid); StyleGrid(nvrGrid); camerasGrid.Visible = true; nvrGrid.Visible = false;
            camerasGrid.Dock = DockStyle.Fill; nvrGrid.Dock = DockStyle.Fill; layout.Controls.Add(camerasGrid, 0, 2); layout.Controls.Add(nvrGrid, 0, 2);
            dataSummary.Text = ""; dataSummary.ForeColor = Muted; dataSummary.Dock = DockStyle.Fill; dataSummary.TextAlign = ContentAlignment.MiddleLeft; layout.Controls.Add(dataSummary, 0, 3);
            SelectCsv(true);
        }

        void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Paper; grid.BorderStyle = BorderStyle.FixedSingle; grid.GridColor = Border;
            grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersDefaultCellStyle.BackColor = Rail; grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold); grid.ColumnHeadersHeight = 34;
            grid.DefaultCellStyle.BackColor = Paper; grid.DefaultCellStyle.ForeColor = Ink; grid.DefaultCellStyle.SelectionBackColor = TealPale; grid.DefaultCellStyle.SelectionForeColor = Ink;
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 9F); grid.DefaultCellStyle.Padding = new Padding(4, 1, 4, 1);
            grid.RowHeadersVisible = false; grid.RowTemplate.Height = 27; grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 249);
            grid.AllowUserToAddRows = true; grid.AllowUserToDeleteRows = true; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2; grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.CellFormatting += (s,e) => {
                if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name.Equals("Password", StringComparison.OrdinalIgnoreCase) && !showPasswords.Checked && e.Value != null && Convert.ToString(e.Value).Length > 0) {
                    e.Value = "••••••••"; e.FormattingApplied = true;
                }
            };
            grid.DataBindingComplete += (s,e) => SetColumnWeights(grid);
        }

        void SetColumnWeights(DataGridView grid)
        {
            foreach (DataGridViewColumn c in grid.Columns) {
                string name = c.Name.ToLowerInvariant();
                float weight = name == "name" ? 38F : name == "ip" ? 27F : name == "password" ? 35F : 16F;
                c.FillWeight = weight; c.MinimumWidth = name == "password" ? 120 : name == "name" ? 150 : 96;
            }
        }

        void LoadCsvs()
        {
            LoadGrid(Path.Combine(root, "cameras.csv"), camerasGrid, true);
            LoadGrid(Path.Combine(root, "nvr_list.csv"), nvrGrid, false);
            UpdateDataSummary();
        }
        void LoadGrid(string path, DataGridView grid, bool camera)
        {
            try {
                if (!File.Exists(path)) throw new FileNotFoundException("Simpan file konfigurasi di folder aplikasi.", path);
                CsvDocument doc = CsvDocument.Load(path); var table = new DataTable();
                foreach (string h in doc.Headers) table.Columns.Add(h);
                foreach (var row in doc.Rows) { var values = row.Take(doc.Headers.Count).ToList(); while (values.Count < doc.Headers.Count) values.Add(""); table.Rows.Add(values.Cast<object>().ToArray()); }
                grid.DataSource = table; if (camera) camerasDoc = doc; else nvrDoc = doc;
            } catch (Exception ex) {
                grid.DataSource = null;
                MessageBox.Show("File CSV tidak bisa dibuka.\n" + ex.Message, "Data belum tersedia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        void SelectCsv(bool cameras)
        {
            showCameras = cameras; camerasGrid.Visible = cameras; nvrGrid.Visible = !cameras;
            showPasswords.Visible = !cameras; if (cameras) showPasswords.Checked = false;
            camerasNav.BackColor = cameras ? TealPale : Paper; camerasNav.ForeColor = cameras ? Teal : Ink;
            nvrNav.BackColor = cameras ? Paper : TealPale; nvrNav.ForeColor = cameras ? Ink : Teal;
            UpdateDataSummary();
            DataGridView active = cameras ? camerasGrid : nvrGrid;
            if (active.IsHandleCreated) active.BeginInvoke(new Action(delegate {
                if (active.Visible && active.DisplayedRowCount(false) > 0 && active.Rows.Count > 0) active.FirstDisplayedScrollingRowIndex = 0;
            }));
        }
        void UpdateDataSummary()
        {
            CsvDocument doc = showCameras ? camerasDoc : nvrDoc;
            string label = showCameras ? "cameras.csv" : "nvr_list.csv";
            dataSummary.Text = doc == null ? label + " · belum dimuat" : label + " · " + doc.Rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " baris · " + root;
        }
        void SaveCsv()
        {
            try {
                DataGridView grid = showCameras ? camerasGrid : nvrGrid; CsvDocument doc = showCameras ? camerasDoc : nvrDoc;
                if (doc == null) throw new InvalidOperationException("File CSV belum dimuat.");
                grid.EndEdit(); string backup = doc.BackupAndSave(grid); UpdateDataSummary(); dataSummary.Text += " · tersimpan; cadangan " + Path.GetFileName(backup);
            } catch (Exception ex) { MessageBox.Show("Perubahan belum tersimpan.\n" + ex.Message, "Tidak bisa menyimpan", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void BrowseReportFolder(object sender, EventArgs e)
        {
            using (var picker = new FolderBrowserDialog()) {
                picker.Description = "Pilih folder untuk menerima salinan laporan yang baru dibuat.";
                picker.SelectedPath = Directory.Exists(outputPath) ? outputPath : root;
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                outputPath = picker.SelectedPath; ShowOutputPath();
                try { File.WriteAllText(Path.Combine(root, "output-folder.txt"), outputPath, new UTF8Encoding(true)); }
                catch (Exception ex) { AddLog("Folder dipilih, tetapi pengaturan tidak bisa disimpan: " + ex.Message); }
            }
        }

        void ChangeLicenseKey()
        {
            using (var dialog = new LicenseDialog(Path.Combine(root, "license.key"))) {
                if (dialog.ShowDialog(this) == DialogResult.OK) AddLog("License key disimpan. Validasi dilakukan saat laporan dijalankan.");
            }
        }
        void ShowOutputPath()
        {
            string path = outputPath ?? "";
            reportFolder.Text = path.Length > 66 ? "…" + path.Substring(path.Length - 65) : path;
            pathTip.SetToolTip(reportFolder, path);
        }

        void ShowPage(bool data)
        {
            dataPage.Visible = data; runPage.Visible = !data; dataPage.BringToFront(); if (data) SelectCsv(showCameras);
            runNav.BackColor = data ? Rail : Paper; runNav.ForeColor = data ? Muted : Teal;
            dataNav.BackColor = data ? Paper : Rail; dataNav.ForeColor = data ? Teal : Muted;
        }
        void UpdateReportOptions()
        {
            bool daily = reportType.SelectedIndex == 1;
            fast.Enabled = daily; officer.Enabled = daily; fill.Enabled = reportType.SelectedIndex == 2;
        }
        void StartRun(object sender, EventArgs e)
        {
            string loader = Path.Combine(root, "hikvision_report_all.ps1");
            if (!File.Exists(loader) || !File.Exists(Path.Combine(root, "license.key"))) {
                AddLog("File program atau lisensi tidak ditemukan. Pastikan semua file tetap satu folder dengan aplikasi."); return;
            }
            var args = new List<string>(); int type = reportType.SelectedIndex;
            if (type == 0) args.Add("-Report");
            if (type == 1) { args.Add("-Daily"); if (!String.IsNullOrWhiteSpace(officer.Text)) { args.Add("-Petugas"); args.Add(officer.Text.Trim()); } if (fast.Checked) args.Add("-Fast"); }
            if (type == 2) { args.Add("-Check"); if (fill.Checked) args.Add("-Fill"); }
            if (!String.IsNullOrWhiteSpace(nvrIp.Text)) { args.Add("-NvrIp"); args.Add(nvrIp.Text.Trim()); }
            string joined = String.Join(" ", args.Select(Quote));
            var info = new ProcessStartInfo { FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe"),
                Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(loader) + (joined.Length > 0 ? " " + joined : ""),
                WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            try {
                Directory.CreateDirectory(Path.Combine(root, "logs")); logPath = Path.Combine(root, "logs", "run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".log");
                runStartedUtc = DateTime.UtcNow;
                Process launched = new Process { StartInfo = info, EnableRaisingEvents = true };
                launched.OutputDataReceived += (s,ev) => { if (ev.Data != null) output.Enqueue(ev.Data); };
                launched.ErrorDataReceived += (s,ev) => { if (ev.Data != null) output.Enqueue("[ERROR] " + ev.Data); };
                launched.Exited += (s,ev) => output.Enqueue("##EXIT:" + launched.ExitCode.ToString(CultureInfo.InvariantCulture));
                child = launched; launched.Start(); launched.BeginOutputReadLine(); launched.BeginErrorReadLine();
                SetRunning(true); logBox.Clear(); logBox.ForeColor = Ink; AddLog("Mulai: " + reportType.Text + (nvrIp.Text.Length > 0 ? " · NVR " + nvrIp.Text.Trim() : " · semua NVR"));
            } catch (Exception ex) { SetRunning(false); AddLog("Gagal memulai proses: " + ex.Message); }
        }
        static string Quote(string value)
        {
            var quoted = new StringBuilder(); quoted.Append('"'); int slashes = 0;
            foreach (char c in value) {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { quoted.Append('\\', slashes * 2 + 1); quoted.Append('"'); slashes = 0; continue; }
                if (slashes > 0) { quoted.Append('\\', slashes); slashes = 0; }
                quoted.Append(c);
            }
            if (slashes > 0) quoted.Append('\\', slashes * 2);
            quoted.Append('"'); return quoted.ToString();
        }
        void StopRun(object sender, EventArgs e) { try { if (child != null && !child.HasExited) { child.Kill(); AddLog("Permintaan menghentikan proses dikirim."); } } catch (Exception ex) { AddLog("Proses tidak bisa dihentikan: " + ex.Message); } }
        void DrainOutput(object sender, EventArgs e)
        {
            string line;
            while (output.TryDequeue(out line)) {
                if (line.StartsWith("##EXIT:")) {
                    int code = 1; Int32.TryParse(line.Substring(7), out code);
                    AddLog(code == 0 ? "Laporan selesai." : "Proses selesai dengan kode " + code + "."); SetRunning(false); child = null;
                    if (code == 0) CopyNewReports();
                }
                else AddLog(line);
            }
        }

        void CopyNewReports()
        {
            string source = Path.Combine(root, "Report CCTV");
            string destination = (outputPath ?? "").Trim();
            if (!Directory.Exists(source) || String.IsNullOrWhiteSpace(destination)) return;
            try {
                Directory.CreateDirectory(destination);
                if (String.Equals(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return;
                int copied = 0;
                foreach (string file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly)) {
                    if (File.GetLastWriteTimeUtc(file) < runStartedUtc.AddSeconds(-1)) continue;
                    File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true); copied++;
                }
                AddLog(copied == 0 ? "Tidak ada file laporan baru untuk disalin." : copied.ToString(CultureInfo.InvariantCulture) + " file laporan disalin ke " + destination);
            } catch (Exception ex) { AddLog("Laporan sudah dibuat, tetapi salinan ke folder pilihan gagal: " + ex.Message); }
        }
        void AddLog(string line)
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + line;
            if (logBox.Text.StartsWith("Belum ada proses.")) { logBox.Clear(); logBox.ForeColor = Ink; }
            logBox.AppendText(stamp + Environment.NewLine); logBox.SelectionStart = logBox.TextLength; logBox.ScrollToCaret();
            if (!String.IsNullOrEmpty(logPath)) try { File.AppendAllText(logPath, stamp + Environment.NewLine, new UTF8Encoding(true)); } catch { }
        }
        void SetRunning(bool running)
        {
            runButton.Enabled = !running; stopButton.Enabled = running; reportType.Enabled = !running; nvrIp.Enabled = !running;
            progress.Style = running ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks; progress.MarqueeAnimationSpeed = running ? 24 : 0; progress.Visible = running;
            statusText.Text = running ? "Sedang berjalan" : "Siap"; statusText.ForeColor = running ? Amber : Teal;
        }
        static void AddFieldLabel(TableLayoutPanel p, string text, int c, int r)
        { p.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft }, c, r); }
        static void SetupNav(Button b)
        { b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.TextAlign = ContentAlignment.MiddleLeft; b.Padding = new Padding(11, 0, 0, 0); b.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold); b.Cursor = Cursors.Hand; }
        static void StyleInput(Control c)
        { c.Dock = DockStyle.Fill; c.Margin = new Padding(2, 10, 14, 9); c.BackColor = Paper; c.ForeColor = Ink; c.Font = new Font("Segoe UI", 9.5F); }
        static void StyleCheck(CheckBox c) { c.ForeColor = Ink; c.AutoSize = true; c.Padding = new Padding(0, 2, 12, 0); }
        static void StyleButton(Button b, Color back, Color fore, bool primary)
        { b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = primary ? Teal : Border; b.FlatAppearance.BorderSize = 1; b.BackColor = back; b.ForeColor = fore; b.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold); b.Cursor = Cursors.Hand; }
    }

    internal sealed class LicenseDialog : Form
    {
        readonly string keyPath;
        readonly TextBox keyInput = new TextBox();
        readonly CheckBox reveal = new CheckBox();
        readonly Label error = new Label();

        public LicenseDialog(string path)
        {
            keyPath = path; Text = "Ganti license key"; StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false; ClientSize = new Size(500, 228); BackColor = Color.FromArgb(241, 244, 242); Font = new Font("Segoe UI", 9.5F);
            Controls.Add(new Label { Text = "Masukkan license key baru", Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold), ForeColor = Color.FromArgb(29, 45, 51), Location = new Point(24, 20), AutoSize = true });
            Controls.Add(new Label { Text = "Key disimpan di folder portable. Server memeriksa key saat laporan berikutnya dijalankan.", ForeColor = Color.FromArgb(90, 108, 112), Location = new Point(26, 55), Size = new Size(448, 38) });
            keyInput.Location = new Point(26, 103); keyInput.Size = new Size(448, 30); keyInput.UseSystemPasswordChar = true; keyInput.Text = File.Exists(path) ? File.ReadAllText(path, Encoding.ASCII).Trim() : ""; Controls.Add(keyInput);
            reveal.Text = "Tampilkan key"; reveal.Location = new Point(26, 139); reveal.AutoSize = true; reveal.CheckedChanged += (s,e) => keyInput.UseSystemPasswordChar = !reveal.Checked; Controls.Add(reveal);
            error.ForeColor = Color.FromArgb(154, 52, 43); error.Location = new Point(26, 167); error.Size = new Size(285, 22); Controls.Add(error);
            var cancel = new Button { Text = "Batal", Location = new Point(300, 172), Size = new Size(82, 34), DialogResult = DialogResult.Cancel };
            var save = new Button { Text = "Simpan key", Location = new Point(389, 172), Size = new Size(85, 34) };
            StyleDialogButton(cancel, Color.White, Color.FromArgb(29, 45, 51)); StyleDialogButton(save, Color.FromArgb(19, 107, 92), Color.White);
            save.Click += SaveKey; Controls.Add(cancel); Controls.Add(save); AcceptButton = save; CancelButton = cancel;
        }

        void SaveKey(object sender, EventArgs e)
        {
            string value = (keyInput.Text ?? "").Trim();
            if (value.Length == 0) { error.Text = "Masukkan license key terlebih dahulu."; keyInput.Focus(); return; }
            try {
                if (File.Exists(keyPath)) File.Copy(keyPath, keyPath + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture), false);
                File.WriteAllText(keyPath, value, Encoding.ASCII); DialogResult = DialogResult.OK; Close();
            } catch (Exception ex) { error.Text = "Key belum tersimpan: " + ex.Message; }
        }

        static void StyleDialogButton(Button b, Color back, Color fore)
        { b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(211, 221, 217); b.BackColor = back; b.ForeColor = fore; b.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold); }
    }

    static class Program
    {
        [STAThread] static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm()); }
    }
}
