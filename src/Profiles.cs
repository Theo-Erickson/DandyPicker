using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DandyPicker
{
    public class Profile
    {
        public string name { get; set; }
        public string dataDir { get; set; }   // "" = Claude's default folder
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string color { get; set; }     // null = the color its seed words pick
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? wave { get; set; }     // the dandelion's look (Dandelion.cs); null = default
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? bend { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string tips { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string windowTitle { get; set; }   // null = "Claude (name)"
    }

    // Everything that touches Claude or the disk: the profile list, which profiles are
    // running, and launching, folders and shortcuts.
    static class Profiles
    {
        static readonly string Here = AppContext.BaseDirectory;
        static readonly string ProfilesFile = Path.Combine(Here, "profiles.json");
        static readonly string ClaudeIconFile = Path.Combine(Here, "claude.ico");
        public static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static readonly string ClaudeAlias = Path.Combine(LocalAppData, @"Microsoft\WindowsApps\claude-desktop.exe");
        public static readonly string DefaultDir = Path.Combine(AppData, "Claude");

        public static bool ClaudeFound { get { return File.Exists(ClaudeAlias); } }

        public static List<Profile> Load()
        {
            try
            {
                if (File.Exists(ProfilesFile))
                {
                    var loaded = JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(ProfilesFile));
                    if (loaded != null && loaded.Count > 0) return loaded;
                }
            }
            catch { }
            var fresh = new List<Profile>
            {
                new Profile { name = "Main (default)", dataDir = "" },
                new Profile { name = "Personal", dataDir = Path.Combine(AppData, "Claude-Personal") },
            };
            Save(fresh);
            return fresh;
        }

        public static void Save(List<Profile> profiles)
        {
            File.WriteAllText(ProfilesFile, JsonSerializer.Serialize(profiles), new UTF8Encoding(false));
        }

        public static string DataDirOf(Profile p) { return string.IsNullOrEmpty(p.dataDir) ? DefaultDir : p.dataDir; }

        public static bool IsDefault(Profile p) { return string.IsNullOrEmpty(p.dataDir); }

        // A data folder for a new profile called 'name'.
        public static string NewDataDir(string name, int count)
        {
            string folder = "Claude-" + Regex.Replace(name, "[^A-Za-z0-9_-]", "");
            if (folder == "Claude-") folder = "Claude-Profile" + (count + 1);
            return Path.Combine(AppData, folder);
        }

        // ---------- running-instance detection ----------

        // Data dirs whose Claude Desktop main process is alive.
        public static HashSet<string> RunningDirs()
        {
            return new HashSet<string>(RunningProcesses().Values, StringComparer.OrdinalIgnoreCase);
        }

        // Each live Claude Desktop main process's id and data dir.
        public static Dictionary<int, string> RunningProcesses()
        {
            var set = new Dictionary<int, string>();
            try
            {
                using (var q = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine, ExecutablePath FROM Win32_Process WHERE Name='Claude.exe' OR Name='claude-desktop.exe'"))
                {
                    foreach (ManagementObject pr in q.Get())
                    {
                        string cmd = pr["CommandLine"] as string;
                        string exe = pr["ExecutablePath"] as string;
                        if (string.IsNullOrEmpty(cmd) || cmd.Contains("--type=")) continue;     // Chromium child process
                        if (exe == null || exe.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) < 0) continue; // Claude Code CLI etc.
                        var m = Regex.Match(cmd, "--user-data-dir=(?:\"([^\"]+)\"|(\\S+))");
                        string dir = !m.Success ? DefaultDir : (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
                        set[(int)(uint)pr["ProcessId"]] = dir.TrimEnd('\\');
                    }
                }
            }
            catch { }
            return set;
        }

        public static bool IsRunning(HashSet<string> running, Profile p) { return running.Contains(DataDirOf(p).TrimEnd('\\')); }

        // ---------- actions ----------

        // Starts the profile (or brings it forward if it's running, handing it 'link' if any).
        public static void Launch(Profile p, string link)
        {
            var args = new List<string>();
            if (!string.IsNullOrEmpty(p.dataDir))
            {
                Directory.CreateDirectory(p.dataDir);
                args.Add("--user-data-dir=\"" + p.dataDir + "\"");
            }
            if (link != null) args.Add("\"" + link + "\"");
            Process.Start(new ProcessStartInfo(ClaudeAlias, string.Join(" ", args))
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            });
        }

        // The folder Claude really uses for the profile, or null if it doesn't exist yet.
        public static string RealFolder(Profile p)
        {
            string dir = DataDirOf(p);
            // Claude is a packaged (MSIX) app, so Windows may keep the profile's files in the
            // package's private copy of AppData instead of the real folder.
            string pkgCopy = Path.Combine(LocalAppData, @"Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming", Path.GetFileName(dir.TrimEnd('\\')));
            if (Directory.Exists(pkgCopy) && dir != DefaultDir) dir = pkgCopy;
            return Directory.Exists(dir) ? dir : null;
        }

        public static void OpenFolder(string dir)
        {
            Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        // Makes "Claude (name)" on the desktop and returns its name.
        public static string CreateShortcut(Profile p)
        {
            string safe = Regex.Replace(p.name, "[\\\\/:*?\"<>|]", "").Trim();
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Claude (" + safe + ").lnk");
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic lnk = shell.CreateShortcut(path);
            lnk.TargetPath = ClaudeAlias;
            if (!string.IsNullOrEmpty(p.dataDir)) lnk.Arguments = "--user-data-dir=\"" + p.dataDir + "\"";
            lnk.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (File.Exists(ClaudeIconFile)) lnk.IconLocation = ClaudeIconFile;
            lnk.Save();
            return "Claude (" + safe + ")";
        }
    }
}
