using System;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Citrus
{
    [DataContract]
    class AppSettings
    {
        [DataMember] public int Schema = 1;
        [DataMember] public string Trigger = "Shift";
        [DataMember] public bool Enabled = true;
        [DataMember] public bool ReducedMotion;
        [DataMember] public int JpegQuality = 90;
        [DataMember] public string Theme = "Dark";

        [OnDeserializing]
        void Defaults(StreamingContext context)
        {
            Schema = 1; Trigger = "Shift"; Enabled = true; JpegQuality = 90; Theme = "Dark";
        }
        public static string DefaultPath {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Citrus", "settings.json"); }
        }
        public int TriggerKey { get { return Trigger == "Alt" ? 0x12 : Trigger == "Control" ? 0x11 : 0x10; } }
        public AppSettings Copy() { return (AppSettings)MemberwiseClone(); }
        public void Validate()
        {
            if (Schema != 1) throw new SerializationException("Unsupported settings schema.");
            if (Trigger != "Shift" && Trigger != "Alt" && Trigger != "Control") Trigger = "Shift";
            if (JpegQuality < 1 || JpegQuality > 100) JpegQuality = 90;
            if (Theme != "Dark" && Theme != "Light" && Theme != "System") Theme = "Dark";
        }
        public static AppSettings Load(string path)
        {
            if (!File.Exists(path)) return new AppSettings();
            try {
                using (var stream = File.OpenRead(path)) {
                    if (stream.Length > 1024 * 1024) throw new SerializationException("Settings file too large.");
                    var result = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream);
                    if (result == null) throw new SerializationException("Empty settings.");
                    result.Validate();
                    return result;
                }
            }
            catch (Exception ex) {
                if (!(ex is SerializationException) && !(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                // Preserve unreadable settings for diagnosis; recover to safe defaults.
                if (ex is SerializationException)
                    try { File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N")); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return new AppSettings();
            }
        }
        public void Save(string path)
        {
            Validate();
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(stream, this);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    class Palette
    {
        public Color Background, Text, Muted, Accent, AccentText, Segment;
        public static Palette For(AppSettings settings)
        {
            if (SystemInformation.HighContrast) return new Palette { Background = SystemColors.Window, Text = SystemColors.WindowText,
                Muted = SystemColors.WindowText, Accent = SystemColors.Highlight, AccentText = SystemColors.HighlightText, Segment = SystemColors.Control };
            bool light = settings.Theme == "Light";
            if (settings.Theme == "System") {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    light = key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 0)) != 0;
            }
            return new Palette {
                Background = light ? Color.FromArgb(250, 248, 244) : Color.FromArgb(23, 25, 30),
                Text = light ? Color.FromArgb(39, 37, 34) : Color.FromArgb(247, 244, 238),
                Muted = light ? Color.FromArgb(90, 86, 80) : Color.FromArgb(185, 190, 199),
                Accent = Color.FromArgb(255, 163, 83), AccentText = Color.FromArgb(40, 29, 19),
                Segment = light ? Color.FromArgb(235, 227, 216) : Color.FromArgb(52, 55, 63)
            };
        }
    }

    class SettingsWindow : GlassWindow
    {
        readonly ComboBox trigger = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ComboBox theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly NumericUpDown quality = new NumericUpDown { Minimum = 1, Maximum = 100, Width = 80 };
        readonly CheckBox enabled = new CheckBox { AutoSize = true, Text = "Enable drag-to-convert" };
        readonly CheckBox motion = new CheckBox { AutoSize = true, Text = "Reduce wheel animation" };
        public AppSettings Result { get; private set; }
        public SettingsWindow(AppSettings current)
        {
            Text = "Citrus settings";
            GlassPalette = Palette.For(current);
            Font = new Font("Segoe UI", 10);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            var panel = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(24), Dock = DockStyle.Fill };
            trigger.Items.AddRange(new object[] { "Shift", "Alt", "Control" }); trigger.SelectedItem = current.Trigger;
            theme.Items.AddRange(new object[] { "Dark", "Light", "System" }); theme.SelectedItem = current.Theme;
            quality.Value = current.JpegQuality; enabled.Checked = current.Enabled; motion.Checked = current.ReducedMotion;
            panel.Controls.Add(enabled, 0, 0); panel.SetColumnSpan(enabled, 2);
            AddRow(panel, "Drag trigger", trigger, 1);
            AddRow(panel, "Appearance", theme, 2);
            AddRow(panel, "JPEG quality (1–100)", quality, 3);
            panel.Controls.Add(motion, 0, 4); panel.SetColumnSpan(motion, 2);
            var note = new Label { AutoSize = true, MaximumSize = new Size(390, 0),
                Text = "Transparent pixels become white in JPG and BMP. Outputs stay beside the originals.\n\nPress the trigger while dragging, then release it before dropping.",
                Margin = new Padding(0, 16, 0, 16) };
            panel.Controls.Add(note, 0, 5); panel.SetColumnSpan(note, 2);
            var save = new GlassButton { Text = "Save", AutoSize = true, Padding = new Padding(12, 5, 12, 5) };
            var cancel = new GlassButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(12, 5, 12, 5) };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            buttons.Controls.AddRange(new Control[] { cancel, save });
            panel.Controls.Add(buttons, 0, 6); panel.SetColumnSpan(buttons, 2);
            save.Click += delegate {
                Result = current.Copy(); Result.Trigger = (string)trigger.SelectedItem; Result.Theme = (string)theme.SelectedItem;
                Result.Enabled = enabled.Checked; Result.ReducedMotion = motion.Checked; Result.JpegQuality = (int)quality.Value;
                try { Result.Save(AppSettings.DefaultPath); DialogResult = DialogResult.OK; }
                catch (Exception ex) { MessageBox.Show(this, "Could not save settings: " + ex.Message, "Citrus", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            AcceptButton = save; CancelButton = cancel; Controls.Add(panel);
            Tint(this, GlassPalette);
            save.BackColor = GlassPalette.Accent; save.ForeColor = GlassPalette.AccentText;
        }
        static void AddRow(TableLayoutPanel panel, string text, Control control, int row)
        {
            var label = new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 12, 20, 12) };
            control.Anchor = AnchorStyles.Left; control.AccessibleName = text;
            panel.Controls.Add(label, 0, row); panel.Controls.Add(control, 1, row);
        }
    }
}
