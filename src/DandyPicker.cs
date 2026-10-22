// DandyPicker
// Runs several Claude Desktop instances side by side, each with its own data
// folder (and therefore its own signed-in account), via Electron's
// --user-data-dir switch. Profiles live in profiles.json next to the .exe.
// Removing a profile here never deletes its data folder.
//
// Build with build.cmd (uses the C# compiler that ships with Windows).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.VisualBasic;

[assembly: System.Reflection.AssemblyTitle("DandyPicker")]
[assembly: System.Reflection.AssemblyProduct("DandyPicker")]
[assembly: System.Reflection.AssemblyDescription("Pick which Claude account to open - run several side by side")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace DandyPicker
{
    public class Profile
    {
        public string name { get; set; }
        public string dataDir { get; set; }   // "" = Claude's default folder
    }

    static class Program
    {
        public const string Title = "DandyPicker";

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);

        [STAThread]
        static void Main()
        {
            SetProcessDPIAware();
            bool first;
            using (var mutex = new Mutex(true, "DandyPicker.SingleInstance", out first))
            {
                if (!first)
                {
                    // Already open: bring that window forward instead of opening a second one.
                    IntPtr h = FindWindow(null, Title);
                    if (h != IntPtr.Zero) { ShowWindow(h, 9); SetForegroundWindow(h); }
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }

    class MainForm : Form
    {
        static readonly string Here = AppDomain.CurrentDomain.BaseDirectory;
        static readonly string ProfilesFile = Path.Combine(Here, "profiles.json");
        static readonly string ClaudeIconFile = Path.Combine(Here, "claude.ico");
        static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static readonly string ClaudeAlias = Path.Combine(LocalAppData, @"Microsoft\WindowsApps\claude-desktop.exe");
        static readonly string DefaultDir = Path.Combine(AppData, "Claude");

        List<Profile> profiles;
        HashSet<string> running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool refreshing;
        readonly float scale;

        ListView list;
        Label hint;
        System.Windows.Forms.Timer timer;

        int S(int px) { return (int)Math.Round(px * scale); }

        public MainForm()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;

            Text = Program.Title;
            Font = new Font("Segoe UI", 9.5f);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(680), S(380));
            MinimumSize = new Size(S(560), S(400));

            profiles = LoadProfiles();
            if (!File.Exists(ProfilesFile)) SaveProfiles();

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(S(10)) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            list = new ListView
            {
                View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, S(10), 0)
            };
            list.Columns.Add("Profile", S(150));
            list.Columns.Add("Status", S(80));
            list.Columns.Add("Data folder", S(200));
            list.DoubleClick += delegate { OpenSelected(); };
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) OpenSelected(); };
            list.Resize += delegate { FitLastColumn(); };
            layout.Controls.Add(list, 0, 0);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            layout.Controls.Add(buttons, 1, 0);

            AddButton(buttons, "Open", OpenSelected, true);
            AddButton(buttons, "Send sign-in link", SendSignInLink, false);
            buttons.Controls.Add(new Panel { Height = S(8), Width = 1, Margin = Padding.Empty });
            AddButton(buttons, "Add profile...", AddProfile, false);
            AddButton(buttons, "Rename...", RenameProfile, false);
            AddButton(buttons, "Remove", RemoveProfile, false);
            AddButton(buttons, "Open folder", OpenFolder, false);
            AddButton(buttons, "Desktop shortcut", CreateShortcut, false);

            hint = new Label
            {
                AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, S(8), 0, 0),
                Text = "Double-click a profile to open it. Signing in from a new profile? Use Send sign-in link."
            };
            layout.Controls.Add(hint, 0, 1);
            layout.SetColumnSpan(hint, 2);

            RebuildList(0);

            timer = new System.Windows.Forms.Timer { Interval = 3000 };
            timer.Tick += delegate { RefreshStatus(); };
            Shown += delegate { RefreshStatus(); timer.Start(); };
            FormClosed += delegate { timer.Stop(); };
        }

        void AddButton(FlowLayoutPanel panel, string text, Action onClick, bool primary)
        {
            var b = new Button { Text = text, Width = S(150), Height = S(primary ? 42 : 32), Margin = new Padding(0, 0, 0, S(6)) };
            if (primary) { b.Font = new Font("Segoe UI", 10f, FontStyle.Bold); AcceptButton = b; }
            b.Click += delegate { onClick(); };
            panel.Controls.Add(b);
        }

        // ---------- profiles ----------

        static List<Profile> LoadProfiles()
        {
            try
            {
                if (File.Exists(ProfilesFile))
                {
                    var loaded = new JavaScriptSerializer().Deserialize<List<Profile>>(File.ReadAllText(ProfilesFile));
                    if (loaded != null && loaded.Count > 0) return loaded;
                }
            }
            catch { }
            return new List<Profile>
            {
                new Profile { name = "Main (default)", dataDir = "" },
                new Profile { name = "Personal", dataDir = Path.Combine(AppData, "Claude-Personal") },
            };
        }

        void SaveProfiles()
        {
            File.WriteAllText(ProfilesFile, new JavaScriptSerializer().Serialize(profiles), new UTF8Encoding(false));
        }

        static string DataDirOf(Profile p) { return string.IsNullOrEmpty(p.dataDir) ? DefaultDir : p.dataDir; }

        Profile Selected
        {
            get { return list.SelectedIndices.Count > 0 ? profiles[list.SelectedIndices[0]] : null; }
        }

        void RebuildList(int select)
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var p in profiles)
            {
                var item = new ListViewItem(p.name);
                item.SubItems.Add("");
                item.SubItems.Add(DataDirOf(p));
                list.Items.Add(item);
            }
            list.EndUpdate();
            ApplyStatus();
            if (list.Items.Count > 0)
            {
                var item = list.Items[Math.Max(0, Math.Min(select, list.Items.Count - 1))];
                item.Selected = true; item.Focused = true;
            }
            FitLastColumn();
        }

        void FitLastColumn()
        {
            if (list.Columns.Count < 3) return;
            int w = list.ClientSize.Width - list.Columns[0].Width - list.Columns[1].Width;
            if (w > S(80)) list.Columns[2].Width = w;
        }

        // ---------- running-instance detection ----------

        // Data dirs whose Claude Desktop main process is alive.
        static HashSet<string> GetRunningDirs()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var q = new ManagementObjectSearcher(
                    "SELECT CommandLine, ExecutablePath FROM Win32_Process WHERE Name='Claude.exe' OR Name='claude-desktop.exe'"))
                {
                    foreach (ManagementObject pr in q.Get())
                    {
                        string cmd = pr["CommandLine"] as string;
                        string exe = pr["ExecutablePath"] as string;
                        if (string.IsNullOrEmpty(cmd) || cmd.Contains("--type=")) continue;     // Chromium child process
                        if (exe == null || exe.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) < 0) continue; // Claude Code CLI etc.
                        var m = Regex.Match(cmd, "--user-data-dir=(?:\"([^\"]+)\"|(\\S+))");
                        string dir = !m.Success ? DefaultDir : (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
                        set.Add(dir.TrimEnd('\\'));
                    }
                }
            }
            catch { }
            return set;
        }

        void RefreshStatus()
        {
            if (refreshing) return;
            refreshing = true;
            Task.Factory.StartNew<HashSet<string>>(GetRunningDirs).ContinueWith(t =>
            {
                refreshing = false;
                if (IsDisposed) return;
                running = t.Result;
                ApplyStatus();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        bool IsRunning(Profile p) { return running.Contains(DataDirOf(p).TrimEnd('\\')); }

        void ApplyStatus()
        {
            for (int i = 0; i < list.Items.Count && i < profiles.Count; i++)
            {
                bool on = IsRunning(profiles[i]);
                var item = list.Items[i];
                string text = on ? "Running" : "";
                if (item.SubItems[1].Text != text) item.SubItems[1].Text = text;
                item.ForeColor = on ? Color.FromArgb(30, 120, 60) : SystemColors.WindowText;
            }
        }

        // ---------- actions ----------

        void Launch(Profile p, string link)
        {
            if (!File.Exists(ClaudeAlias))
            {
                MessageBox.Show(this, "Claude Desktop not found at\n" + ClaudeAlias, Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var args = new List<string>();
            if (!string.IsNullOrEmpty(p.dataDir))
            {
                Directory.CreateDirectory(p.dataDir);
                args.Add("--user-data-dir=\"" + p.dataDir + "\"");
            }
            if (link != null) args.Add("\"" + link + "\"");
            // Launching a profile that's already running just brings its window forward
            // (and hands it the link, if any).
            Process.Start(new ProcessStartInfo(ClaudeAlias, string.Join(" ", args))
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            });
        }

        void OpenSelected()
        {
            var p = Selected; if (p == null) return;
            Launch(p, null);
            RefreshStatusSoon();
        }

        void RefreshStatusSoon()
        {
            var once = new System.Windows.Forms.Timer { Interval = 1500 };
            once.Tick += delegate { once.Stop(); once.Dispose(); RefreshStatus(); };
            once.Start();
        }

        // Windows always routes claude:// links (e.g. the browser's sign-in hand-off) to
        // the default instance. Re-launching with the profile's --user-data-dir plus the
        // link forwards it to that profile's running instance instead.
        void SendSignInLink()
        {
            var p = Selected; if (p == null) return;
            if (!IsRunning(p))
            {
                MessageBox.Show(this, "Open '" + p.name + "' and start signing in from its window first - the link only works in the window that asked for it.", Program.Title);
                return;
            }
            string url = "";
            try { url = (Clipboard.GetText() ?? "").Trim(); } catch { }
            if (!url.StartsWith("claude://", StringComparison.OrdinalIgnoreCase))
            {
                url = Interaction.InputBox(
                    "Paste the claude:// sign-in link for '" + p.name + "'.\n\nIn the browser, cancel the 'Open Claude?' prompt, then right-click the page's Open Claude button and choose Copy link.",
                    "Send sign-in link", "").Trim();
            }
            if (url.Length == 0) return;
            if (!url.StartsWith("claude://", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "That isn't a claude:// link.", Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Launch(p, url);
            try { Clipboard.Clear(); } catch { }
        }

        void AddProfile()
        {
            string name = Interaction.InputBox("Name for the new profile (e.g. Work):", "Add profile", "Work").Trim();
            if (name.Length == 0) return;
            string folder = "Claude-" + Regex.Replace(name, "[^A-Za-z0-9_-]", "");
            if (folder == "Claude-") folder = "Claude-Profile" + (profiles.Count + 1);
            profiles.Add(new Profile { name = name, dataDir = Path.Combine(AppData, folder) });
            SaveProfiles();
            RebuildList(profiles.Count - 1);
        }

        void RenameProfile()
        {
            var p = Selected; if (p == null) return;
            string name = Interaction.InputBox("New name:", "Rename profile", p.name).Trim();
            if (name.Length == 0) return;
            p.name = name;
            SaveProfiles();
            RebuildList(list.SelectedIndices[0]);
        }

        void RemoveProfile()
        {
            var p = Selected; if (p == null) return;
            if (string.IsNullOrEmpty(p.dataDir))
            {
                MessageBox.Show(this, "The default profile can't be removed.", Program.Title);
                return;
            }
            var ok = MessageBox.Show(this, "Remove '" + p.name + "' from this list?\n\nIts data folder is kept:\n" + p.dataDir,
                "Remove profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ok != DialogResult.Yes) return;
            int at = list.SelectedIndices[0];
            profiles.Remove(p);
            SaveProfiles();
            RebuildList(at);
        }

        void OpenFolder()
        {
            var p = Selected; if (p == null) return;
            string dir = DataDirOf(p);
            // Claude is a packaged (MSIX) app, so Windows may keep the profile's files in the
            // package's private copy of AppData instead of the real folder.
            string pkgCopy = Path.Combine(LocalAppData, @"Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming", Path.GetFileName(dir.TrimEnd('\\')));
            if (Directory.Exists(pkgCopy) && dir != DefaultDir) dir = pkgCopy;
            if (Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
            else MessageBox.Show(this, "Not created yet - open the profile once first.", Program.Title);
        }

        void CreateShortcut()
        {
            var p = Selected; if (p == null) return;
            string safe = Regex.Replace(p.name, "[\\\\/:*?\"<>|]", "").Trim();
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Claude (" + safe + ").lnk");
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic lnk = shell.CreateShortcut(path);
            lnk.TargetPath = ClaudeAlias;
            if (!string.IsNullOrEmpty(p.dataDir)) lnk.Arguments = "--user-data-dir=\"" + p.dataDir + "\"";
            lnk.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (File.Exists(ClaudeIconFile)) lnk.IconLocation = ClaudeIconFile;
            lnk.Save();
            MessageBox.Show(this, "Shortcut created on your desktop:\nClaude (" + safe + ")", Program.Title);
        }
    }
}
