using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Citrus
{
    static class FileActions
    {
        public static IEnumerable<FormatRoute> Routes() {
            return new string[] { "Copy path", "Copy files", "Show folder", "Terminal" }.Select((label, index) =>
                new FormatRoute { Id = "action." + index, Label = label, Extension = "", Sources = new string[] { "*" },
                    Warning = index >= 2 ? "Uses the first item in the selection." : "" });
        }
        public static string Execute(string[] paths, FormatRoute route)
        {
            paths = paths.Select(Path.GetFullPath).ToArray();
            if (route.Id == "action.0") { Clipboard.SetText(string.Join(Environment.NewLine, paths)); return "Paths copied to clipboard."; }
            if (route.Id == "action.1") {
                var collection = new StringCollection(); collection.AddRange(paths);
                Clipboard.SetFileDropList(collection); return "Files copied to clipboard. Paste them in Explorer.";
            }
            string folder = Directory.Exists(paths[0]) ? paths[0] : Path.GetDirectoryName(paths[0]);
            if (route.Id == "action.2") Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            else if (route.Id == "action.3") Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe") { WorkingDirectory = folder, UseShellExecute = true });
            else throw new InvalidOperationException("Unknown file action.");
            return route.Label + " opened.";
        }
    }
}
