using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace Citrus
{
    [DataContract]
    class FormatRoute
    {
        [DataMember] public string Id;
        [DataMember] public string Label;
        [DataMember] public string Extension;
        [DataMember] public string[] Sources;
        [DataMember] public string Warning;
        [DataMember] public int MinimumFiles = 0;
        [DataMember] public bool Aggregate = false;
        public override string ToString() { return Label; }
    }
    [DataContract] class EngineRuntime {
        [DataMember] public string Python = "";
        [DataMember] public string Libraries = "";
        [DataMember] public string Office = "";
        [DataMember] public string FFmpeg = "";
    }
    [DataContract] class WorkerRequest {
        [DataMember] public string Source;
        [DataMember] public string[] Sources;
        [DataMember] public string Route;
        [DataMember] public string Output;
        [DataMember] public int Quality;
    }
    [DataContract] class WorkerResult {
        [DataMember] public string Output;
        [DataMember] public string Error;
    }

    static class FormatCatalog
    {
        static readonly Lazy<List<FormatRoute>> catalog = new Lazy<List<FormatRoute>>(Load);
        public static string EngineStatus = "Built-in images";
        public static string NativeId(ImageTarget target) { return "image" + ImageRoutes.Extension(target); }
        public static FormatRoute ForImage(ImageTarget target) { return catalog.Value.First(route => route.Id == NativeId(target)); }
        public static FormatRoute[] Targets(string[] files)
        {
            if (files == null || files.Length == 0) return new FormatRoute[0];
            return catalog.Value.Where(route => files.Length >= route.MinimumFiles && files.All(file => !string.IsNullOrWhiteSpace(file)
                && (route.Sources.Contains("*") || (!Directory.Exists(file) && route.Sources.Contains(Path.GetExtension(file).ToLowerInvariant()))))).ToArray();
        }
        public static List<FormatRoute> All { get { return catalog.Value; } }
        static List<FormatRoute> Load()
        {
            var result = new List<FormatRoute>();
            foreach (ImageTarget target in ImageRoutes.All)
                result.Add(new FormatRoute { Id = NativeId(target), Label = ImageRoutes.Label(target), Extension = ImageRoutes.Extension(target),
                    Sources = new string[] { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" }.Where(ext => {
                        ImageTarget source; return ImageRoutes.TrySource("file" + ext, out source) && source != target;
                    }).ToArray(), Warning = target == ImageTarget.Jpeg || target == ImageTarget.Bmp ? "Transparent areas become white." : "" });
            try {
                var extra = EngineBridge.Capabilities();
                foreach (var route in extra) {
                    if (string.IsNullOrEmpty(route.Id) || route.Sources == null) continue;
                    var existing = result.FirstOrDefault(item => item.Id == route.Id);
                    if (existing != null) existing.Sources = existing.Sources.Union(route.Sources).ToArray();
                    else result.Add(route);
                }
                EngineStatus = "Local engines ready · " + result.Count + " output profiles";
            }
            catch (Exception ex) { EngineStatus = "Built-in images only · " + ex.Message; }
            result.AddRange(FileActions.Routes());
            return result;
        }
    }

    static class EngineBridge
    {
        public static T Read<T>(string path) {
            using (var stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        public static void Write<T>(string path, T value) {
            using (var stream = File.Create(path)) new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        }
        static readonly string engineDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine");
        static ProcessStartInfo Python(string arguments) {
            var runtime = Read<EngineRuntime>(Path.Combine(engineDirectory, "runtime.json"));
            string python = ResolveRuntimePath(runtime.Python);
            if (!File.Exists(python)) throw new IOException("Python engine is not configured.");
            var info = Info(python, Quote(Path.Combine(engineDirectory, "worker.py")) + " " + arguments);
            info.EnvironmentVariables["CITRUS_LIBRARIES"] = ResolveRuntimePath(runtime.Libraries);
            if (!string.IsNullOrEmpty(runtime.Office)) info.EnvironmentVariables["CITRUS_OFFICE"] = ResolveRuntimePath(runtime.Office);
            if (!string.IsNullOrEmpty(runtime.FFmpeg)) info.EnvironmentVariables["CITRUS_FFMPEG"] = ResolveRuntimePath(runtime.FFmpeg);
            info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            return info;
        }
        static string ResolveRuntimePath(string path) {
            return string.IsNullOrEmpty(path) ? "" : Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(engineDirectory, path));
        }
        static ProcessStartInfo Info(string file, string args) {
            return new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        }
        // Windows command-line quoting: quote the whole argument and double terminal backslashes.
        public static string Quote(string value) {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value) {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); slashes = 0; result.Append(c);
            }
            result.Append('\\', slashes * 2); return result.Append('"').ToString();
        }
        public static List<FormatRoute> Capabilities() {
            string json = Execute(Python("--capabilities"), CancellationToken.None, 30000);
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (List<FormatRoute>)new DataContractJsonSerializer(typeof(List<FormatRoute>)).ReadObject(stream);
        }
        public static string Convert(string source, FormatRoute route, int quality, CancellationToken token)
        {
            return ConvertMany(new string[] { source }, route, quality, token);
        }
        public static string ConvertMany(string[] sources, FormatRoute route, int quality, CancellationToken token)
        {
            sources = sources.Select(Path.GetFullPath).ToArray();
            string source = sources[0];
            string root = Path.Combine(Path.GetDirectoryName(source), ".citrus-work-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                File.SetAttributes(root, File.GetAttributes(root) | FileAttributes.Hidden);
                string requestPath = Path.Combine(root, "request.json");
                var request = new WorkerRequest { Source = source, Sources = sources, Route = route.Id, Output = Path.Combine(root, "result" + route.Extension), Quality = quality };
                Write(requestPath, request);
                ImageTarget sourceImage;
                var native = ImageRoutes.All.Where(target => FormatCatalog.NativeId(target) == route.Id).ToArray();
                ProcessStartInfo info = native.Length == 1 && ImageRoutes.TrySource(source, out sourceImage)
                    ? Info(System.Windows.Forms.Application.ExecutablePath, "--native-worker " + Quote(requestPath))
                    : Python(Quote(requestPath));
                string json = Execute(info, token, 30 * 60 * 1000);
                WorkerResult result;
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    result = (WorkerResult)new DataContractJsonSerializer(typeof(WorkerResult)).ReadObject(stream);
                if (!string.IsNullOrEmpty(result.Error)) throw new IOException(result.Error);
                string staged = Path.GetFullPath(result.Output ?? "");
                if (!staged.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid worker output path.");
                bool directory = Directory.Exists(staged);
                if (!directory && (!File.Exists(staged) || new FileInfo(staged).Length == 0)) throw new IOException("Worker produced no output.");
                string stem = Path.GetFileNameWithoutExtension(source);
                if (route.Aggregate) stem += "-combined";
                if (source.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) stem = Path.GetFileName(source).Substring(0, Path.GetFileName(source).Length - 7);
                for (int index = 0; index < 10000; index++) {
                    token.ThrowIfCancellationRequested();
                    string path = Path.Combine(Path.GetDirectoryName(source), stem + (directory ? route.Id.StartsWith("pdf.") ? "-pages" : "-extracted" : "")
                        + (index == 0 ? "" : " (" + index + ")") + (directory ? "" : route.Extension));
                    try {
                        if (directory) Directory.Move(staged, path); else File.Move(staged, path);
                        return path;
                    }
                    catch (IOException) { if (!File.Exists(path) && !Directory.Exists(path)) throw; }
                }
                throw new IOException("Too many files with the same name.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        public static int RunNative(string path)
        {
            try {
                var request = Read<WorkerRequest>(path);
                ImageTarget target = ImageRoutes.All.First(item => FormatCatalog.NativeId(item) == request.Route);
                string output = Converter.Convert(request.Source, target, request.Quality, CancellationToken.None, Path.GetDirectoryName(request.Output));
                using (var stream = Console.OpenStandardOutput())
                    new DataContractJsonSerializer(typeof(WorkerResult)).WriteObject(stream, new WorkerResult { Output = output });
                return 0;
            }
            catch (Exception ex) {
                using (var stream = Console.OpenStandardOutput())
                    new DataContractJsonSerializer(typeof(WorkerResult)).WriteObject(stream, new WorkerResult { Error = ex.Message });
                return 1;
            }
        }
        static string Execute(ProcessStartInfo info, CancellationToken token, int timeout)
        {
            token.ThrowIfCancellationRequested();
            using (var job = new ProcessJob())
            using (var process = new Process { StartInfo = info }) {
                var stdout = new StringBuilder(); var stderr = new StringBuilder();
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock(stdout) { if (stdout.Length < 1024 * 1024) stdout.AppendLine(e.Data); } };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock(stderr) { if (stderr.Length < 8192) stderr.AppendLine(e.Data); } };
                process.Start();
                try {
                    job.Assign(process); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    var watch = Stopwatch.StartNew();
                    while (!process.WaitForExit(50)) {
                        token.ThrowIfCancellationRequested();
                        if (watch.ElapsedMilliseconds > timeout) throw new TimeoutException("Conversion timed out.");
                    }
                    process.WaitForExit(); token.ThrowIfCancellationRequested();
                    string output = stdout.ToString().Trim();
                    if (process.ExitCode != 0) {
                        try {
                            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(output))) {
                                var result = (WorkerResult)new DataContractJsonSerializer(typeof(WorkerResult)).ReadObject(stream);
                                if (!string.IsNullOrEmpty(result.Error)) throw new IOException(result.Error);
                            }
                        }
                        catch (SerializationException) { }
                        throw new IOException(string.IsNullOrWhiteSpace(stderr.ToString()) ? "Conversion engine failed." : stderr.ToString());
                    }
                    return output;
                }
                finally {
                    job.Dispose();
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
                }
            }
        }
    }

    sealed class ProcessJob : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct Basic { public long Time1, Time2; public uint Flags; public UIntPtr Min, Max; public uint Processes; public UIntPtr Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] struct Io { public ulong A, B, C, D, E, F; }
        [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic Basic; public Io Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr security, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int kind, ref Extended info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        IntPtr handle;
        public ProcessJob() {
            handle = CreateJobObject(IntPtr.Zero, null);
            var info = new Extended(); info.Basic.Flags = 0x2300;
            info.ProcessMemory = new UIntPtr(IntPtr.Size == 8 ? 2UL * 1024 * 1024 * 1024 : 1024UL * 1024 * 1024);
            info.JobMemory = new UIntPtr(IntPtr.Size == 8 ? 4UL * 1024 * 1024 * 1024 : 1536UL * 1024 * 1024);
            if (handle == IntPtr.Zero || !SetInformationJobObject(handle, 9, ref info, (uint)Marshal.SizeOf(info))) {
                int error = Marshal.GetLastWin32Error(); Dispose(); throw new System.ComponentModel.Win32Exception(error);
            }
        }
        public void Assign(Process process) {
            if (!AssignProcessToJobObject(handle, process.Handle)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    }
}
