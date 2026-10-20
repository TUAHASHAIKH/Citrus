using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Citrus
{
    static class Program
    {
        public static EventWaitHandle ShowRequested;
        public static EventWaitHandle ShutdownRequested;
        static string ShowEventName;
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--native-worker") { Environment.Exit(EngineBridge.RunNative(args[1])); return; }
            if (args.Length > 0 && args[0] == "--self-test")
            {
                Environment.Exit(SelfTest.Run(args.Length > 1 ? args[1] : "."));
                return;
            }
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }
            bool background = Array.IndexOf(args, "--background") >= 0;
            if (Array.IndexOf(args, "--enable-startup") >= 0) Startup.SetEnabled(true);
            bool created;
            // Different Windows desktops cannot display one another's windows.
            // Keep the single-instance guard and activation signal local to this desktop.
            string desktop = Native.DesktopName();
            ShowEventName = "Local.Citrus.ShowWindow." + desktop;
            string shutdownEvent;
            using (var hash = System.Security.Cryptography.SHA256.Create()) {
                string installation = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
                shutdownEvent = "Local.Citrus.Shutdown." + desktop + "." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(installation))).Replace("-", "");
            }
            string mutexName = "Local.Citrus.JpegPrototype." + desktop;
            if (Array.IndexOf(args, "--shutdown") >= 0) {
                try {
                    using (var signal = EventWaitHandle.OpenExisting(shutdownEvent)) signal.Set();
                    using (var existing = Mutex.OpenExisting(mutexName)) {
                        bool acquired = false;
                        try { acquired = existing.WaitOne(30000); }
                        catch (AbandonedMutexException) { acquired = true; }
                        if (acquired) existing.ReleaseMutex(); else Environment.ExitCode = 2;
                    }
                }
                catch (WaitHandleCannotBeOpenedException) { }
                return;
            }
            using (var mutex = new Mutex(true, mutexName, out created))
            {
                if (!created)
                {
                    try { if (!background) using (var signal = EventWaitHandle.OpenExisting(ShowEventName)) signal.Set(); }
                    catch (WaitHandleCannotBeOpenedException) { }
                    return;
                }
                ShowRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                ShutdownRequested = new EventWaitHandle(false, EventResetMode.AutoReset, shutdownEvent);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                int availableProfiles = FormatCatalog.All.Count;
                Application.Run(new MainWindow(background));
                ShowRequested.Dispose();
                ShutdownRequested.Dispose();
            }
        }
    }

    static class Startup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "CitrusJpegConverter";
        public static bool Enabled
        {
            get { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && key.GetValue(Name) != null; }
        }
        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (enabled) key.SetValue(Name, "\"" + Application.ExecutablePath + "\" --background", RegistryValueKind.String);
                else key.DeleteValue(Name, false);
            }
        }
    }

    class MainWindow : GlassWindow
    {
        readonly Label status = new Label();
        readonly Button choose = new GlassButton();
        readonly Button cancel = new GlassButton();
        readonly Button convert = new GlassButton();
        readonly ComboBox output = new GlassComboBox { Width = 200 };
        readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Height = 6, Margin = new Padding(0, 18, 0, 0) };
        readonly Queue<PendingBatch> pending = new Queue<PendingBatch>();
        bool startingNext;
        int queueEpoch;
        class PendingBatch { public string[] Files; public FormatRoute Target; }
        string lastOutput;
        readonly Label hint;
        readonly ToolStripMenuItem gestureMenu;
        readonly NotifyIcon tray;
        readonly Icon brandIcon;
        readonly DropWheel wheel;
        readonly System.Windows.Forms.Timer observer;
        CancellationTokenSource cancellation;
        bool busy, closeAfterCancellation, exitRequested, hideAtStartup;
        readonly DragGesture gesture = new DragGesture();
        DateTime releaseCleanupAt = DateTime.MaxValue;
        DateTime candidateSince;
        AppSettings settings;
        bool updatingMenu;
        string[] selectedFiles = new string[0];
        public bool IsConverting { get { return busy || startingNext; } }
        public AppSettings Preferences { get { return settings; } }
        public Palette Colors { get; private set; }

        public MainWindow() : this(false) { }

        public MainWindow(bool background) : this(background, AppSettings.Load(AppSettings.DefaultPath)) { }

        public MainWindow(bool background, AppSettings preferences)
        {
            settings = preferences;
            hideAtStartup = background;
            brandIcon = Brand.LoadIcon();
            Icon = brandIcon;
            Text = "Citrus · Offline converter";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(23, 25, 30);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, RowCount = 6, Padding = new Padding(28), Dock = DockStyle.Fill };
            var title = new Label { Text = "A fresh format.\nNo extra steps.", Font = new Font("Segoe UI", 26, FontStyle.Bold),
                AutoSize = true, Margin = new Padding(0, 0, 0, 18) };
            hint = new Label {
                AutoSize = true, Margin = new Padding(0, 0, 0, 22), ForeColor = Color.Silver };
            choose.Text = "Choose files…";
            choose.AutoSize = true;
            choose.Padding = new Padding(12, 9, 12, 9);
            choose.Margin = new Padding(0, 0, 12, 0);
            choose.BackColor = Color.FromArgb(255, 158, 80);
            choose.ForeColor = Color.FromArgb(30, 25, 20);
            choose.FlatStyle = FlatStyle.Flat;
            choose.Click += delegate {
                using (var picker = new OpenFileDialog { Filter = "Files to convert|*.*", Multiselect = true })
                    if (picker.ShowDialog(this) == DialogResult.OK) SetSelection(picker.FileNames);
            };
            convert.Text = "Convert"; convert.AutoSize = true; convert.Padding = new Padding(12, 9, 12, 9);
            convert.Name = "ConvertImages"; convert.AccessibleName = "Convert selected images";
            convert.Enabled = false;
            convert.Click += delegate { if (output.SelectedItem != null) StartConversion(selectedFiles, (FormatRoute)output.SelectedItem); };
            output.DropDownStyle = ComboBoxStyle.DropDownList; output.FormattingEnabled = true;
            output.DrawMode = DrawMode.OwnerDrawFixed; output.ItemHeight = Font.Height + 8; output.FlatStyle = FlatStyle.Flat;
            output.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                var palette = Colors ?? Palette.For(settings);
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var brush = new SolidBrush(selected ? palette.Accent : palette.Segment)) e.Graphics.FillRectangle(brush, e.Bounds);
                string label = e.Index >= 0 ? output.Items[e.Index].ToString() : output.SelectedItem == null ? "Choose files first" : output.SelectedItem.ToString();
                TextRenderer.DrawText(e.Graphics, label, output.Font, Rectangle.Inflate(e.Bounds, -5, 0), selected ? palette.AccentText : palette.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            output.Enabled = false; output.Name = "OutputFormat"; output.AccessibleName = "Output image format";
            output.SelectedIndexChanged += delegate {
                var route = output.SelectedItem as FormatRoute;
                if (route != null) status.Text = selectedFiles.Length + " file(s) selected.\n" + route.Warning;
            };
            cancel.Text = "Cancel";
            cancel.AutoSize = true;
            cancel.Padding = new Padding(12, 9, 12, 9);
            cancel.FlatStyle = FlatStyle.Standard;
            cancel.UseVisualStyleBackColor = true;
            cancel.Enabled = false;
            cancel.Click += delegate { CancelAll(); };
            status.AutoSize = true;
            status.MaximumSize = new Size(460, 0);
            status.Margin = new Padding(0, 22, 0, 0);
            status.Text = "Ready · Offline · Originals preserved";
            status.ForeColor = Color.FromArgb(190, 200, 190);
            var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false, Margin = Padding.Empty };
            var settingsButton = new GlassButton { Text = "Settings", AutoSize = true, Padding = new Padding(12, 9, 12, 9) };
            settingsButton.Click += delegate { ShowSettings(); };
            buttons.Controls.AddRange(new Control[] { choose, convert, cancel, settingsButton });
            var outputRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(0, 44), Margin = new Padding(0, 0, 0, 16), WrapContents = false };
            outputRow.Controls.Add(new Label { Text = "Format / action", AutoSize = true, Margin = new Padding(0, 5, 12, 0) });
            outputRow.Controls.Add(output);
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(hint, 0, 1);
            layout.Controls.Add(outputRow, 0, 2);
            layout.Controls.Add(buttons, 0, 3);
            layout.Controls.Add(progress, 0, 4);
            layout.Controls.Add(status, 0, 5);
            Controls.Add(layout);
            AllowDrop = true;
            DragEnter += delegate(object sender, DragEventArgs e) { e.Effect = Accept(e); };
            DragDrop += delegate(object sender, DragEventArgs e) {
                if (Accept(e) == DragDropEffects.Copy) { SetSelection((string[])e.Data.GetData(DataFormats.FileDrop)); OpenWindow(); }
            };
            wheel = new DropWheel(this);
            tray = new NotifyIcon { Icon = brandIcon, Text = "Citrus · Image converter", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open converter", null, delegate { OpenWindow(); });
            gestureMenu = new ToolStripMenuItem("Drag-to-convert enabled") { Checked = settings.Enabled, CheckOnClick = true };
            gestureMenu.CheckedChanged += delegate {
                if (updatingMenu) return;
                var next = settings.Copy(); next.Enabled = gestureMenu.Checked;
                try { next.Save(AppSettings.DefaultPath); settings = next; ApplySettings(); }
                catch (Exception ex) {
                    updatingMenu = true; gestureMenu.Checked = settings.Enabled; updatingMenu = false;
                    MessageBox.Show("Could not save settings: " + ex.Message, "Citrus");
                }
                wheel.Hide();
            };
            menu.Items.Add(gestureMenu);
            menu.Items.Add("Settings…", null, delegate { ShowSettings(); });
            menu.Items.Add("Cancel all conversions", null, delegate { CancelAll(); });
            menu.Items.Add("Open output folder", null, delegate {
                if (lastOutput != null) FileActions.Execute(new string[] { lastOutput }, new FormatRoute { Id = "action.2" });
            });
            menu.Items.Add("Engine status", null, delegate {
                MessageBox.Show(FormatCatalog.EngineStatus + "\n\n" + string.Join("\n", FormatCatalog.All.GroupBy(route => route.Id.Split('.')[0]).Select(group => group.Key + ": " + group.Count() + " profiles")), "Citrus engines");
            });
            menu.Items.Add("Licenses and source…", null, delegate { ShowLicenseInformation(); });
            var startup = new ToolStripMenuItem("Start with Windows") { Checked = Startup.Enabled, CheckOnClick = true };
            startup.CheckedChanged += delegate {
                try { Startup.SetEnabled(startup.Checked); }
                catch (Exception ex) {
                    MessageBox.Show("Could not update Windows startup: " + ex.Message, "Citrus");
                    startup.Checked = Startup.Enabled;
                }
            };
            menu.Items.Add(startup);
            menu.Items.Add("Exit Citrus", null, delegate { exitRequested = true; Close(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { OpenWindow(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            observer = new System.Windows.Forms.Timer { Interval = 25 };
            observer.Tick += Observe;
            observer.Start();
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (e.CloseReason == CloseReason.UserClosing && !exitRequested)
                {
                    e.Cancel = true;
                    Hide();
                    return;
                }
                if (busy && e.CloseReason == CloseReason.UserClosing) {
                    e.Cancel = true;
                    closeAfterCancellation = true;
                    pending.Clear();
                    cancellation.Cancel();
                    status.Text = "Finishing cancellation before closing…";
                }
            };
            // Create the hidden HWND without requiring the converter window to be visible.
            var handle = Handle;
            ApplySettings();
            SystemEvents.UserPreferenceChanged += SystemPreferencesChanged;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                SystemEvents.UserPreferenceChanged -= SystemPreferencesChanged;
                if (observer != null) observer.Dispose();
                if (wheel != null) wheel.Dispose();
                if (tray != null) tray.Dispose();
                if (brandIcon != null) brandIcon.Dispose();
                if (cancellation != null) cancellation.Cancel();
            }
            base.Dispose(disposing);
        }

        internal void SetSelection(string[] files)
        {
            var targets = FormatCatalog.Targets(files);
            if (targets.Length == 0 || files.Any(file => !File.Exists(file) && !Directory.Exists(file))) {
                selectedFiles = new string[0]; output.Items.Clear(); output.Enabled = convert.Enabled = false;
                status.Text = "No common conversion is available for these files with the installed engines.";
                return;
            }
            selectedFiles = (string[])files.Clone();
            output.Items.Clear();
            foreach (var target in targets) output.Items.Add(target);
            output.SelectedIndex = 0;
            output.Enabled = true; convert.Enabled = true;
            output.Parent.PerformLayout();
            PerformLayout();
            status.Text = files.Length + " file(s) selected. Choose an output format, then Convert.\n" + targets[0].Warning;
        }

        void ShowLicenseInformation()
        {
            using (var dialog = new Form { Text = "Citrus · Licenses and source", StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(580, 310), MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog })
            {
                var text = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
                    Location = new Point(20, 20), Size = new Size(540, 200), BackColor = dialog.BackColor,
                    Text = "Citrus " + Application.ProductVersion + "\r\nCopyright (c) Citrus contributors.\r\n\r\n" +
                        "Citrus is free software under the GNU Affero General Public License, version 3. " +
                        "You may modify and redistribute it under that license. There is no warranty.\r\n\r\n" +
                        "Conversion engines have separate licenses. Their notices and source information are included with the installation. " +
                        "Citrus's license does not replace those terms." };
                var notices = new Button { Text = "Third-party notices", Location = new Point(20, 240), Size = new Size(170, 36) };
                var license = new Button { Text = "Citrus license", Location = new Point(200, 240), Size = new Size(145, 36) };
                var source = new Button { Text = "Citrus source", Location = new Point(355, 240), Size = new Size(145, 36) };
                Action<string> open = delegate(string path) {
                    try {
                        if (File.Exists(path)) {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe"), "\"" + path + "\"") { UseShellExecute = false });
                        } else if (path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                        } else {
                            throw new FileNotFoundException("The license document is missing from this installation.", path);
                        }
                    }
                    catch (Exception ex) { MessageBox.Show(dialog, "Could not open the document: " + ex.Message, "Citrus"); }
                };
                notices.Click += delegate { open(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "THIRD_PARTY_NOTICES.md")); };
                license.Click += delegate { open(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSE")); };
                source.Click += delegate { open("https://github.com/TUAHASHAIKH/Citrus"); };
                dialog.Controls.AddRange(new Control[] { text, notices, license, source });
                dialog.ShowDialog();
            }
        }

        void ShowSettings()
        {
            wheel.Hide(); OpenWindow();
            using (var dialog = new SettingsWindow(settings)) {
                dialog.Icon = brandIcon;
                if (dialog.ShowDialog(this) == DialogResult.OK) { settings = dialog.Result; ApplySettings(); }
            }
        }

        void ApplySettings()
        {
            Colors = Palette.For(settings);
            GlassPalette = Colors;
            RefreshGlass();
            ApplyColors(this);
            hint.Text = "Drag a file and tap " + settings.Trigger + " to choose a format.\nImages · Media · Documents · Archives — offline.";
            hint.ForeColor = Colors.Muted; status.ForeColor = Colors.Muted;
            choose.BackColor = convert.BackColor = Colors.Accent;
            choose.ForeColor = convert.ForeColor = Colors.AccentText;
            updatingMenu = true; gestureMenu.Checked = settings.Enabled; updatingMenu = false;
            tray.Text = "Citrus · " + settings.Trigger + "-drag" + (settings.Enabled ? "" : " (paused)");
            wheel.Invalidate();
        }

        void SystemPreferencesChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) ApplySettings(); }); }
            catch (InvalidOperationException) { }
        }

        void ApplyColors(Control parent)
        {
            parent.BackColor = parent is Panel || parent is Label ? Color.Transparent : Colors.Background; parent.ForeColor = Colors.Text;
            var button = parent as Button;
            if (button != null) { button.FlatStyle = FlatStyle.Flat; button.BackColor = Colors.Segment; }
            foreach (Control child in parent.Controls) ApplyColors(child);
        }

        internal void CancelAll() { queueEpoch++; pending.Clear(); if (cancellation != null) cancellation.Cancel(); }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value && !hideAtStartup);
        }

        public void OpenWindow() { hideAtStartup = false; Show(); WindowState = FormWindowState.Normal; Activate(); }

        public DragDropEffects Accept(DragEventArgs e)
        {
            if (pending.Count >= 20 || (e.AllowedEffect & DragDropEffects.Copy) == 0 || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return DragDropEffects.None;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return DragDropEffects.None;
            if (FormatCatalog.Targets(files).Length == 0) return DragDropEffects.None;
            foreach (string file in files) if (!File.Exists(file) && !Directory.Exists(file)) return DragDropEffects.None;
            return DragDropEffects.Copy;
        }

        void Observe(object sender, EventArgs e)
        {
            if (Program.ShutdownRequested != null && Program.ShutdownRequested.WaitOne(0)) {
                exitRequested = true; pending.Clear(); queueEpoch++; startingNext = false; Close(); return;
            }
            if (Program.ShowRequested != null && Program.ShowRequested.WaitOne(0)) OpenWindow();
            bool left = Native.Down(1), shift = Native.Down(settings.TriggerKey);
            Point point = Cursor.Position;
            bool escape = Native.Down(0x1B);
            if (escape) wheel.Hide();
            if (gesture.Update(left, shift, escape, point, SystemInformation.DragSize, settings.Enabled && pending.Count < 20))
            {
                candidateSince = DateTime.UtcNow;
                wheel.OpenAt(point);
            }
            // Give OLE's Drop callback time to arrive after mouse-up before removing its target.
            if (left) releaseCleanupAt = DateTime.MaxValue;
            else if (wheel.Visible)
            {
                if (releaseCleanupAt == DateTime.MaxValue) releaseCleanupAt = DateTime.UtcNow.AddMilliseconds(200);
                if (DateTime.UtcNow >= releaseCleanupAt) wheel.Hide();
            }
            if (wheel.Visible && !wheel.HasPayload && (DateTime.UtcNow - candidateSince).TotalSeconds > 2)
                wheel.Hide();
        }

        public void StartConversion(string[] files) { StartConversion(files, ImageTarget.Png); }

        public void StartConversion(string[] files, ImageTarget target) { StartConversion(files, FormatCatalog.ForImage(target)); }

        public async void StartConversion(string[] files, FormatRoute target)
        {
            if (target.Id.StartsWith("action.")) {
                try { status.Text = FileActions.Execute(files, target); if (!Visible) tray.ShowBalloonTip(2000, "Citrus", status.Text, ToolTipIcon.Info); }
                catch (Exception ex) { status.Text = ex.Message; }
                wheel.Hide(); return;
            }
            if (!FormatCatalog.Targets(files).Any(route => route.Id == target.Id)) {
                status.Text = "That output format is not available for every selected image.";
                return;
            }
            if (busy || startingNext) {
                if (pending.Count >= 20) { status.Text = "Queue is full. Wait for a batch to finish."; return; }
                pending.Enqueue(new PendingBatch { Files = (string[])files.Clone(), Target = target });
                status.Text = pending.Count + " batch(es) queued.";
                wheel.Hide(); return;
            }
            files = (string[])files.Clone();
            int quality = settings.JpegQuality;
            wheel.Hide();
            busy = true; choose.Enabled = false; convert.Enabled = false; output.Enabled = false; cancel.Enabled = true;
            int jobsCount = target.Aggregate ? 1 : files.Length;
            progress.Maximum = Math.Max(1, jobsCount); progress.Value = 0;
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            int completed = 0;
            var errors = new List<string>();
            try
            {
                foreach (string file in target.Aggregate ? new string[] { files[0] } : files)
                {
                    if (token.IsCancellationRequested) break;
                    status.Text = "Converting " + (completed + errors.Count + 1) + " / " + jobsCount + " to " + target.Label + "\n" + Path.GetFileName(file);
                    tray.Text = "Citrus · " + (completed + errors.Count + 1) + "/" + jobsCount + " → " + target.Label;
                    try { lastOutput = await Task.Run(() => target.Aggregate ? EngineBridge.ConvertMany(files, target, quality, token) : EngineBridge.Convert(file, target, quality, token)); completed++; }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { errors.Add(Path.GetFileName(file) + ": " + ex.Message); }
                    progress.Value = Math.Min(progress.Maximum, completed + errors.Count);
                }
                if (IsDisposed) return;
                status.Text = (token.IsCancellationRequested ? "Cancelled. " : "") + completed + " " + target.Label + " saved beside original."
                    + (errors.Count > 0 ? "\n" + errors.Count + " failed. See details." : "");
                if (errors.Count > 0) {
                    OpenWindow();
                    MessageBox.Show(this, string.Join("\n\n", errors.Take(10).ToArray()) + (errors.Count > 10 ? "\n\n… and " + (errors.Count - 10) + " more failures." : ""), "Conversion details", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (!Visible) {
                    tray.ShowBalloonTip(3500, "Citrus", status.Text, ToolTipIcon.Info);
                }
            }
            finally
            {
                if (!IsDisposed) {
                    choose.Enabled = true; cancel.Enabled = false; convert.Enabled = output.Enabled = selectedFiles.Length > 0;
                    tray.Text = "Citrus · " + settings.Trigger + "-drag" + (settings.Enabled ? "" : " (paused)");
                }
                bool wasCancelled = token.IsCancellationRequested;
                cancellation.Dispose(); cancellation = null;
                busy = false;
                if (closeAfterCancellation && !IsDisposed) Close();
                else if (!wasCancelled && !IsDisposed && pending.Count > 0) {
                    var next = pending.Dequeue();
                    int epoch = queueEpoch; startingNext = true;
                    BeginInvoke((MethodInvoker)delegate { startingNext = false; if (!IsDisposed && epoch == queueEpoch) StartConversion(next.Files, next.Target); });
                }
            }
        }
    }

    // Supports Shift held before mouse-down, pressed before movement, or tapped mid-drag.
    // One activation per mouse gesture prevents Escape/timeouts from reopening the popup.
    class DragGesture
    {
        bool previousLeft, used;
        Point start;
        public bool Update(bool left, bool shift, bool escape, Point point, Size threshold, bool enabled)
        {
            if (left && !previousLeft) { start = point; used = false; }
            previousLeft = left;
            if (!left) { used = false; return false; }
            if (escape) { used = true; return false; }
            bool moved = Math.Abs(point.X - start.X) >= threshold.Width
                || Math.Abs(point.Y - start.Y) >= threshold.Height;
            if (!enabled || !shift || !moved || used) return false;
            used = true;
            return true;
        }
    }

    class DropWheel : GlassWindow
    {
        readonly MainWindow owner;
        readonly System.Windows.Forms.Timer animation;
        readonly System.Windows.Forms.Timer navigation;
        float reveal;
        float scale = 1;
        int hovering = -1;
        FormatRoute[] targets = new FormatRoute[0];
        int page;
        DateTime navigationSince;
        bool pageLatched;
        bool hasDragData;
        bool Paged { get { return targets.Length > 6; } }
        int SlotCount { get { return Paged ? 6 : targets.Length; } }
        FormatRoute RouteAt(int slot) {
            int index = page * 5 + slot;
            return slot < 0 || (Paged && slot == 5) || index >= targets.Length ? null : targets[index];
        }
        public bool HasPayload { get; private set; }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; return cp; }
        }
        public DropWheel(MainWindow owner)
        {
            this.owner = owner;
            Text = "Citrus conversion ring";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true;
            ClientSize = new Size(240, 240);
            BackColor = Color.FromArgb(28, 30, 35);
            DoubleBuffered = true; AllowDrop = true;
            using (var path = new GraphicsPath()) { path.AddEllipse(0, 0, 240, 240); Region = new Region(path); }
            DragEnter += delegate(object sender, DragEventArgs e) { hasDragData = false; OnDrag(sender, e); };
            DragOver += OnDrag;
            DragLeave += delegate { hovering = -1; pageLatched = false; hasDragData = false; Invalidate(); };
            DragDrop += delegate(object sender, DragEventArgs e) {
                int index = -1;
                string[] files = null;
                if (owner.Accept(e) == DragDropEffects.Copy)
                {
                    files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    targets = FormatCatalog.Targets(files);
                    index = HitTest(PointToClient(new Point(e.X, e.Y)));
                }
                if (index >= 0 && RouteAt(index) != null)
                {
                    var target = RouteAt(index);
                    e.Effect = DragDropEffects.Copy; Hide(); owner.StartConversion(files, target);
                }
                else { e.Effect = DragDropEffects.None; Hide(); }
            };
            animation = new System.Windows.Forms.Timer { Interval = 15 };
            animation.Tick += delegate {
                reveal = Math.Min(1, reveal + .12f); Invalidate();
                if (reveal >= 1) animation.Stop();
            };
            navigation = new System.Windows.Forms.Timer { Interval = 50 };
            navigation.Tick += delegate {
                if (Visible && Paged && hovering == 5 && !pageLatched && (DateTime.UtcNow - navigationSince).TotalMilliseconds >= 650) {
                    page = (page + 1) % ((targets.Length + 4) / 5); pageLatched = true; Invalidate();
                }
            };
        }
        public void OpenAt(Point point)
        {
            HasPayload = false; hovering = -1; targets = new FormatRoute[0]; reveal = 0; page = 0; pageLatched = false; hasDragData = false;
            scale = Native.DpiAt(point) / 96f;
            int diameter = (int)Math.Round(240 * scale);
            ClientSize = new Size(diameter, diameter);
            var oldRegion = Region;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(0, 0, diameter, diameter);
                Region = new Region(path);
            }
            if (oldRegion != null) oldRegion.Dispose();
            Rectangle area = Screen.FromPoint(point).WorkingArea;
            Location = PlaceNearCursor(point, area, diameter);
            BackColor = owner.Colors.Background;
            GlassPalette = owner.Colors;
            Show(); navigation.Start();
            if (owner.Preferences.ReducedMotion || !SystemInformation.IsMenuAnimationEnabled) { reveal = 1; Invalidate(); }
            else animation.Start();
        }
        public static Point PlaceNearCursor(Point cursor, Rectangle workArea, int diameter)
        {
            // The pointer overlaps the near edge of the ring so OLE enters our target.
            // Keep this location fixed throughout selection, rather than chasing the pointer.
            int offset = (int)Math.Round(diameter * .4);
            int centerX = cursor.X + offset;
            if (centerX + diameter / 2 > workArea.Right) centerX = cursor.X - offset;
            return new Point(
                Math.Max(workArea.Left, Math.Min(centerX - diameter / 2, workArea.Right - diameter)),
                Math.Max(workArea.Top, Math.Min(cursor.Y - diameter / 2, workArea.Bottom - diameter)));
        }
        int HitTest(Point p)
        {
            return RingGeometry.HitTest(new PointF(p.X / scale, p.Y / scale), SlotCount);
        }
        public void SetPreviewTargets(string[] files) { targets = FormatCatalog.Targets(files); HasPayload = targets.Length > 0; page = 0; Invalidate(); }
        void OnDrag(object sender, DragEventArgs e)
        {
            if (!hasDragData) {
                HasPayload = owner.Accept(e) == DragDropEffects.Copy;
                targets = HasPayload ? FormatCatalog.Targets((string[])e.Data.GetData(DataFormats.FileDrop)) : new FormatRoute[0];
                page = 0; hasDragData = true;
            }
            bool accepted = HasPayload && (e.AllowedEffect & DragDropEffects.Copy) != 0;
            int nextHover = accepted ? HitTest(PointToClient(new Point(e.X, e.Y))) : -1;
            if (nextHover != hovering) navigationSince = DateTime.UtcNow;
            hovering = nextHover;
            if (hovering != 5) pageLatched = false;
            e.Effect = hovering >= 0 && RouteAt(hovering) != null ? DragDropEffects.Copy : DragDropEffects.None;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(scale, scale);
            Palette palette = owner.Colors;
            int count = targets.Length == 0 ? 3 : SlotCount;
            using (var font = new Font("Segoe UI", count > 4 ? 13 : 17, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            for (int i = 0; i < count; i++)
            {
                bool selected = HasPayload && i == hovering;
                Color fill = selected ? palette.Accent : palette.Segment;
                fill = Color.FromArgb(
                    (int)(palette.Background.R + (fill.R - palette.Background.R) * reveal),
                    (int)(palette.Background.G + (fill.G - palette.Background.G) * reveal),
                    (int)(palette.Background.B + (fill.B - palette.Background.B) * reveal));
                using (var path = RingGeometry.Wedge(i, count)) {
                    using (var brush = new LinearGradientBrush(new Rectangle(0, 0, 240, 240), ControlPaint.Light(fill, .14f), fill, 100f)) g.FillPath(brush, path);
                    using (var edge = new Pen(Color.FromArgb(selected ? 120 : 48, Color.White), 1)) g.DrawPath(edge, path);
                }
                PointF label = RingGeometry.LabelPoint(i, count);
                using (var text = new SolidBrush(selected ? palette.AccentText : palette.Text))
                    g.DrawString(targets.Length == 0 ? "·" : Paged && i == 5 ? "More →" : RouteAt(i) == null ? "—" : RouteAt(i).Label, font, text,
                        new RectangleF(label.X - 33, label.Y - 18, 66, 36), format);
            }
            Brand.Draw(g, new RectangleF(103, 83, 34, 34));
            using (var font = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(palette.Text))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(Paged && hovering == 5 ? "Hold for\nnext page" : hovering >= 0 && RouteAt(hovering) != null ? "Drop for\n" + RouteAt(hovering).Label : HasPayload ? "Choose\nformat" : "Drag\nfiles", font, brush, new RectangleF(83, 121, 74, 32), format);
            base.OnPaint(e);
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { if (animation != null) animation.Stop(); if (navigation != null) navigation.Stop(); }
        }
        protected override void Dispose(bool disposing) { if (disposing) { animation.Dispose(); navigation.Dispose(); } base.Dispose(disposing); }
    }

    static class Native
    {
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        public static float DpiAt(Point point)
        {
            try
            {
                uint x, y;
                if (GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out x, out y) == 0 && x > 0) return x;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return 96;
        }
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint threadId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder buffer, int length, out int required);
        public static string DesktopName()
        {
            var buffer = new StringBuilder(512);
            int required;
            if (!GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, buffer, buffer.Capacity * 2, out required))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return buffer.ToString();
        }
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        public static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
    }

    static class SelfTest
    {
        public static int Run(string root)
        {
            Directory.CreateDirectory(root);
            string report = Path.Combine(root, "results.txt");
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Control.CheckForIllegalCrossThreadCalls = true;
                CheckGestures();
                CheckPlacement();
                PhaseTwoTests.Run(root);
                EngineTests.Run(root);
                string jpg = Path.Combine(root, "photo.jpg");
                using (var image = new Bitmap(64, 32)) {
                    using (var g = Graphics.FromImage(image)) g.Clear(Color.Coral);
                    image.Save(jpg, ImageFormat.Jpeg);
                }
                byte[] original = File.ReadAllBytes(jpg);
                string png = Converter.Convert(jpg, CancellationToken.None);
                using (var image = Image.FromFile(png)) {
                    Assert(image.Width == 64 && image.Height == 32, "dimensions");
                    Assert(image.RawFormat.Guid == ImageFormat.Png.Guid, "PNG signature");
                }
                string second = Converter.Convert(jpg, CancellationToken.None);
                Assert(second != png && File.Exists(png), "collision preserves previous output");
                Assert(System.Linq.Enumerable.SequenceEqual(original, File.ReadAllBytes(jpg)), "source unchanged");
                string corrupt = Path.Combine(root, "corrupt.jpg");
                File.WriteAllText(corrupt, "not an image");
                bool rejected = false;
                try { Converter.Convert(corrupt, CancellationToken.None); }
                catch (InvalidDataException ex) { rejected = ex.Message.Contains("valid image"); }
                Assert(rejected, "corrupt JPEG rejected");
                string fake = Path.Combine(root, "fake.jpg");
                File.Copy(png, fake, true);
                rejected = false;
                try { Converter.Convert(fake, CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "PNG disguised as JPEG rejected");
                var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                rejected = false;
                try { Converter.Convert(jpg, cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
                Assert(rejected, "cancellation");
                Assert(Directory.GetFiles(root, ".citrus-*.tmp").Length == 0, "temporary cleanup");
                using (var window = new MainWindow(false, new AppSettings()))
                {
                    window.Show();
                    Application.DoEvents();
                    window.SetSelection(new string[] { jpg });
                    Assert(((ComboBox)window.Controls.Find("OutputFormat", true).Single()).Text == "PNG", "output dropdown displays selected format");
                    CheckLayout(window);
                    using (var preview = new Bitmap(window.Width, window.Height))
                    {
                        window.DrawToBitmap(preview, new Rectangle(Point.Empty, window.Size));
                        preview.Save(Path.Combine(root, "ui-preview.png"), ImageFormat.Png);
                    }
                    int pickerCount = Directory.GetFiles(root, "photo*.png").Length;
                    var convertButton = (Button)window.Controls.Find("ConvertImages", true).Single();
                    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    convertButton.PerformClick();
                    var pickerDeadline = DateTime.UtcNow.AddSeconds(10);
                    while (window.IsConverting && DateTime.UtcNow < pickerDeadline) { Application.DoEvents(); Thread.Sleep(10); }
                    Assert(!window.IsConverting && Directory.GetFiles(root, "photo*.png").Length == pickerCount + 1, "file picker selection → Convert button publishes PNG");
                    window.SetSelection(new string[] { jpg, "unsupported.pdf" });
                    Assert(!convertButton.Enabled, "invalid new selection clears old conversion request");
                    window.Close();
                    Assert(!window.IsDisposed && !window.Visible, "closing window keeps app alive in tray");
                    window.OpenWindow();
                    Assert(window.Visible, "tray window can reopen");
                    using (var ring = new DropWheel(window))
                    {
                        ring.OpenAt(new Point(600, 400));
                        ring.SetPreviewTargets(new string[] { jpg });
                        var animationDeadline = DateTime.UtcNow.AddMilliseconds(250);
                        while (DateTime.UtcNow < animationDeadline) { Application.DoEvents(); Thread.Sleep(10); }
                        using (var preview = new Bitmap(ring.Width, ring.Height))
                        {
                            ring.DrawToBitmap(preview, new Rectangle(Point.Empty, ring.Size));
                            preview.Save(Path.Combine(root, "ring-preview.png"), ImageFormat.Png);
                        }
                        ring.Hide();
                    }
                }
                using (var settingsDialog = new SettingsWindow(new AppSettings()))
                {
                    settingsDialog.Show(); Application.DoEvents(); CheckLayout(settingsDialog);
                    using (var preview = new Bitmap(settingsDialog.Width, settingsDialog.Height)) {
                        settingsDialog.DrawToBitmap(preview, new Rectangle(Point.Empty, settingsDialog.Size));
                        preview.Save(Path.Combine(root, "settings-preview.png"), ImageFormat.Png);
                    }
                }
                using (var background = new MainWindow(true, new AppSettings()))
                {
                    background.Show();
                    Application.DoEvents();
                    Assert(!background.Visible && background.IsHandleCreated, "background start creates hidden live window");
                    int before = Directory.GetFiles(root, "photo*.png").Length;
                    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    background.StartConversion(new string[] { jpg });
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (background.IsConverting && DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    Assert(!background.IsConverting, "background conversion completes");
                    Assert(Directory.GetFiles(root, "photo*.png").Length == before + 1, "background conversion publishes PNG");
                    Assert(!background.Visible, "background conversion does not open converter UI");
                }
                File.WriteAllText(report, "PASS: native image routes, transparency, multi-page rejection, catalog intersection, six-wedge geometry, settings recovery, cursor/DPI placement, drag activation/cancellation, original preservation, collision safety, UI/settings layout, close-to-tray, hidden startup, background conversion; optional engine bridge, failure cleanup, job-object process-tree termination and queue checks run when engines are available.");
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(report, ex.ToString()); return 1; }
        }
        static void Assert(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); }
        static void CheckPlacement()
        {
            foreach (int diameter in new int[] { 240, 360, 480 })
            foreach (Rectangle area in new Rectangle[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1920, -200, 1920, 1080) })
            {
                foreach (Point cursor in new Point[] {
                    new Point(area.Left + area.Width / 2, area.Top + area.Height / 2),
                    new Point(area.Left + 2, area.Top + 2), new Point(area.Right - 2, area.Bottom - 2) })
                {
                    Point location = DropWheel.PlaceNearCursor(cursor, area, diameter);
                    Assert(area.Contains(new Rectangle(location, new Size(diameter, diameter))), "ring remains on monitor");
                    double distance = Math.Sqrt(Math.Pow(cursor.X - location.X - diameter / 2, 2)
                        + Math.Pow(cursor.Y - location.Y - diameter / 2, 2));
                    Assert(distance < diameter, "ring appears near cursor");
                }
            }
            Point right = DropWheel.PlaceNearCursor(new Point(900, 500), new Rectangle(0, 0, 1920, 1040), 240);
            Assert(right.X + 120 > 900, "ring opens beside cursor to right");
            Point left = DropWheel.PlaceNearCursor(new Point(1850, 500), new Rectangle(0, 0, 1920, 1040), 240);
            Assert(left.X + 120 < 1850, "ring flips to left near right edge");
        }
        static void CheckGestures()
        {
            var gesture = new DragGesture();
            var threshold = new Size(4, 4);
            var origin = new Point(100, 100);
            var moved = new Point(140, 100);
            Assert(!gesture.Update(false, true, false, origin, threshold, true), "Shift alone does not activate");
            Assert(!gesture.Update(true, true, false, origin, threshold, true), "mouse down does not activate");
            Assert(gesture.Update(true, true, false, moved, threshold, true), "Shift held before drag activates");
            Assert(!gesture.Update(true, true, false, moved, threshold, true), "no repeated activation");
            Assert(!gesture.Update(true, false, false, moved, threshold, true), "Shift release does not reactivate");
            gesture.Update(false, false, false, origin, threshold, true);
            gesture.Update(true, false, false, origin, threshold, true);
            Assert(!gesture.Update(true, false, false, moved, threshold, true), "normal dragging does not activate");
            Assert(gesture.Update(true, true, false, moved, threshold, true), "Shift tapped during drag activates");
            gesture.Update(false, false, false, origin, threshold, true);
            gesture.Update(true, false, false, origin, threshold, true);
            gesture.Update(true, true, true, moved, threshold, true);
            Assert(!gesture.Update(true, true, false, moved, threshold, true), "Escape stays dismissed until next drag");
            gesture.Update(false, false, false, origin, threshold, true);
            gesture.Update(true, true, false, origin, threshold, true);
            Assert(!gesture.Update(true, true, false, moved, threshold, false), "disabled gesture does not activate");
        }
        static void CheckLayout(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is Label)
                    Assert(control.Height >= control.PreferredSize.Height, "label fits: " + control.Text);
                Assert(control.Bottom <= parent.ClientSize.Height && control.Right <= parent.ClientSize.Width,
                    "control fits container: " + control.GetType().Name + " " + control.Bounds + " in " + parent.GetType().Name + " " + parent.ClientSize);
                CheckLayout(control);
            }
        }
    }
}
