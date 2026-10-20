using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Citrus
{
    static class EngineTests
    {
        static void Assert(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); }
        public static void Run(string root)
        {
            string folder = Path.Combine(root, "engine bridge Ω"); Directory.CreateDirectory(folder);
            string text = Path.Combine(folder, "notes.txt"); File.WriteAllText(text, "Citrus engine bridge test");
            var pdf = FormatCatalog.Targets(new string[] { text }).FirstOrDefault(route => route.Id == "text.pdf");
            if (pdf == null) { File.WriteAllText(Path.Combine(folder, "optional-engines.txt"), FormatCatalog.EngineStatus); return; }
            string output = EngineBridge.Convert(text, pdf, 90, CancellationToken.None);
            Assert(File.ReadAllBytes(output).Take(4).SequenceEqual(new byte[] { 37, 80, 68, 70 }), "Python worker publishes actual PDF");
            var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool rejected = false;
            try { EngineBridge.Convert(text, pdf, 90, cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
            Assert(rejected && Directory.GetDirectories(folder, ".citrus-work-*").Length == 0, "cancelled worker staging cleaned");
            string invalid = Path.Combine(folder, "invalid.pdf"); File.WriteAllText(invalid, "invalid"); rejected = false;
            var toText = FormatCatalog.Targets(new string[] { invalid }).First(route => route.Id == "pdf.txt");
            try { EngineBridge.Convert(invalid, toText, 90, CancellationToken.None); } catch (IOException) { rejected = true; }
            Assert(rejected && Directory.GetDirectories(folder, ".citrus-work-*").Length == 0, "worker failures clean staging and return errors");
            Assert(FormatCatalog.Targets(new string[] { text }).Any(route => route.Id == "action.0"), "universal file actions exposed");
            CheckProcessTree(); CheckQueue(folder);
        }
        static void CheckProcessTree()
        {
            var runtime = EngineBridge.Read<EngineRuntime>(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", "runtime.json"));
            string script = "import subprocess,sys,time;p=subprocess.Popen([sys.executable,'-c','import time;time.sleep(60)']);print(p.pid,flush=True);time.sleep(60)";
            using (var job = new ProcessJob())
            using (var process = Process.Start(new ProcessStartInfo(runtime.Python, "-c " + EngineBridge.Quote(script)) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })) {
                job.Assign(process);
                var line = process.StandardOutput.ReadLineAsync(); Assert(line.Wait(5000), "child process starts");
                int childId = int.Parse(line.Result);
                using (var child = Process.GetProcessById(childId)) {
                    job.Dispose();
                    Assert(process.WaitForExit(5000) && child.WaitForExit(5000), "job cancellation kills parent and child processes");
                }
            }
        }
        static void CheckQueue(string folder)
        {
            string source = Path.Combine(folder, "queue.jpg");
            using (var image = new Bitmap(32, 32)) image.Save(source, ImageFormat.Jpeg);
            using (var window = new MainWindow(true, new AppSettings())) {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                window.StartConversion(new string[] { source }, ImageTarget.Png);
                window.StartConversion(new string[] { source }, ImageTarget.Bmp);
                var end = DateTime.UtcNow.AddSeconds(15);
                while (window.IsConverting && DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(10); }
                Assert(!window.IsConverting && File.Exists(Path.ChangeExtension(source, ".png")) && File.Exists(Path.ChangeExtension(source, ".bmp")), "queued batches drain in background");
                Assert(!window.Visible, "queue does not show main window");
            }
        }
    }
}
