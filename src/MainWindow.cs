using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Shapes;
using Avalonia.Input.Platform;

namespace DandyPicker
{
    // "Seed Field": a headline, a plant per profile (soil, circuit-trace roots, a stem with a
    // leaf per place in the list, and the dandelion), and a strip at the bottom when a
    // sign-in link drifts onto the clipboard. Everything grows in when the window opens.
    class MainWindow : Window
    {
        List<Profile> profiles;
        readonly Settings settings = Settings.Load();
        HashSet<string> running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool refreshing, statusKnown;
        string clipLink;          // a claude:// link on the clipboard, if any
        string dismissedLink;     // ...that the user said to ignore

        // The plants live as long as their profile, so a refresh doesn't make them grow again.
        readonly Dictionary<Profile, Button> plants = new Dictionary<Profile, Button>();
        readonly Dictionary<Profile, UsageInfo> usage = new Dictionary<Profile, UsageInfo>();
        DateTime usageAt = DateTime.MinValue;
        bool fetchingUsage;
        double shownAt = double.NaN;     // when the window first showed (PlantView.Clock)

        readonly TextBlock caption;
        readonly Button usageBtn;
        readonly WrapPanel field;
        readonly StackPanel bottom;
        readonly DispatcherTimer timer;

        public MainWindow()
        {
            Title = Program.Title;
            Width = 980; Height = 880; MinWidth = 640; MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Ui.UiFont;
            FontSize = 14;
            this.SetResourceReference(BackgroundProperty, "Bg");
            this.SetResourceReference(ForegroundProperty, "Text");
            UseLayoutRounding = true;
            try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://DandyPicker/dandelion.ico"))); } catch { }

            profiles = Profiles.Load();

            var root = new DockPanel { Margin = new Thickness(56, 32, 56, 24) };

            // headline
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            usageBtn = Ui.IconBtn("gauge", "", ToggleUsage, "BtnPillOutline", "Text");
            var plant = Ui.IconBtn("plus", "Plant a profile", AddProfile, "BtnPillOutline", "Text");
            var buttons = Ui.Row(8, usageBtn, plant);
            buttons.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(buttons, Dock.Right);
            head.Children.Add(buttons);
            caption = Ui.Text("", 12.5, "Muted");
            caption.LetterSpacing = 2;
            var title = new TextBlock { FontSize = 42, FontWeight = FontWeights.Light, LetterSpacing = -0.6 };
            title.Inlines.Add(new Avalonia.Controls.Documents.Run("Which Claude "));
            var today = new Avalonia.Controls.Documents.Run("today?") { FontWeight = FontWeights.SemiBold };
            today.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, this.GetResourceObservable("Accent"));
            title.Inlines.Add(today);
            head.Children.Add(Ui.Stack(6, caption, title));
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            bottom = new StackPanel { Spacing = 12, Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            field = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            root.Children.Add(new ScrollViewer { Content = field, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalContentAlignment = VerticalAlignment.Center });
            Content = root;

            // The header rises in first; the plants grow once we know which are running.
            Opened += delegate
            {
                shownAt = PlantView.Now;
                Ui.Rise(caption, 0); Ui.Rise(title, 120); Ui.Rise(buttons, 220);
                RefreshStatus(); CheckClipboard(); RefreshUsage(true);
                timer.Start();
            };
            ShowUsageButton();
            Render();

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += delegate { RefreshStatus(); CheckClipboard(); RefreshUsage(false); };
            Activated += delegate { CheckClipboard(); RefreshUsage(false); };
            Closed += delegate { timer.Stop(); };
        }

        // Milliseconds left until 'ms' after the window opened; -1 once that's past (no animation).
        double Later(double ms)
        {
            if (double.IsNaN(shownAt)) return ms;
            double d = shownAt + ms - PlantView.Now;
            return d > 0 ? d : -1;
        }

        // ---------- page ----------

        void Render()
        {
            int on = profiles.Count(p => Profiles.IsRunning(running, p));
            caption.Text = (CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(DateTime.Now.DayOfWeek) + " · " + on + " of " + profiles.Count + " open").ToUpperInvariant();

            foreach (var gone in plants.Keys.Where(k => !profiles.Contains(k)).ToList()) plants.Remove(gone);
            field.Children.Clear();
            if (statusKnown)
            {
                for (int i = 0; i < profiles.Count; i++) field.Children.Add(PlantItem(profiles[i], i));
                field.Children.Add(NewItem(profiles.Count));
            }

            bottom.Children.Clear();
            if (!Profiles.ClaudeFound)
            {
                bottom.Children.Add(Ui.Banner("Err", Ui.Stack(2,
                    Ui.Text("Claude Desktop isn't installed", 14, "ErrText", FontWeights.SemiBold),
                    Ui.Text("DandyPicker looks for it at " + Profiles.ClaudeAlias + ". Install it from claude.ai/download, then reopen DandyPicker.", 12.5, "ErrText"))));
            }
            if (clipLink != null) bottom.Children.Add(LinkStrip());
            else
            {
                var how = Ui.Btn("How signing in a new profile works", ShowSignInHelp, "BtnLink");
                how.FontSize = 13;
                how.HorizontalAlignment = HorizontalAlignment.Right;
                bottom.Children.Add(how);
            }
        }

        Control PlantItem(Profile p, int index)
        {
            bool on = Profiles.IsRunning(running, p);
            double t0 = index * 260;

            Button holder;
            PlantView view;
            if (plants.TryGetValue(p, out holder) && (int)holder.Tag == index)
            {
                view = (PlantView)holder.Content;
                (holder.Parent as Panel)?.Children.Remove(holder);
                view.Running = on;
            }
            else
            {
                // Plants that appear after the window opened (a new profile) grow on their own.
                double appear = double.IsNaN(shownAt) || Later(t0) < 0 ? PlantView.Now - t0 : shownAt;
                if (holder != null) appear = PlantView.Now - 100000;   // its place changed: show it grown
                view = new PlantView(p, index, on, appear) { Width = 228, Height = 427 };
                holder = new Button { Theme = Ui.Theme("Bare"), Content = view, Tag = index, HorizontalAlignment = HorizontalAlignment.Center };
                holder.Tip("Customize " + p.name + "'s dandelion");
                Avalonia.Automation.AutomationProperties.SetName(holder, "Customize " + p.name);
                holder.Click += delegate { CustomizeProfile(p); };
                holder.DoubleTapped += delegate { Open(p); };
                plants[p] = holder;
            }
            UsageInfo info = null;
            bool showUsage = settings.usage || Program.DemoUsage;
            if (showUsage) usage.TryGetValue(p, out info);
            view.Usage = showUsage ? info : null;

            var name = Ui.Line(p.name, 22, "Text", FontWeights.Medium);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.MaxWidth = 230;

            Control status;
            if (on)
            {
                var dot = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(Shape.FillProperty, "DotOk");
                status = Ui.Row(6, dot, Ui.Text("In bloom · running", 13, "OkText"));
            }
            else status = Ui.Text(Profiles.IsDefault(p) ? "Closed · default folder" : "Closed", 13, "Muted");
            status.HorizontalAlignment = HorizontalAlignment.Center;
            status.Tip(Profiles.DataDirOf(p));

            var words = Ui.Text(Dandelion.SeedWords(Dandelion.PayloadOf(p)), 12, "Faint");
            words.FontFamily = Ui.Mono;
            words.HorizontalAlignment = HorizontalAlignment.Center;

            var labels = Ui.Stack(3, name, status, words);
            if (showUsage) labels.Children.Add(UsageLines(info));

            var open = Ui.Btn(on ? "Show window" : "Open", delegate { Open(p); }, on ? "BtnPillOutline" : "BtnPill");
            var more = Ui.IconBtn("more", null, null, "BtnRound");
            more.Tip("More");
            Avalonia.Automation.AutomationProperties.SetName(more, "More for " + p.name);
            more.Click += delegate { ShowMenu(more, p); };
            var actions = Ui.Row(4, open, more);
            actions.HorizontalAlignment = HorizontalAlignment.Center;
            actions.Margin = new Thickness(48, 2, 0, 0);   // centers the main button; ⋯ hangs off to its right

            Ui.Rise(labels, Later(t0 + 1500));
            Ui.Rise(actions, Later(t0 + 1680));
            return new StackPanel { Width = 250, Spacing = 10, Margin = new Thickness(6, 18, 6, 12), Children = { holder, labels, actions } };   // room above for the usage rings
        }

        Control UsageLines(UsageInfo info)
        {
            var sp = new StackPanel { Spacing = 3, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            if (info == null) { sp.Children.Add(Ui.Text("Checking usage...", 12.5, "Faint")); return sp; }
            if (info.Error != null)
            {
                var t = Ui.Text("Usage unavailable", 12.5, "Faint");
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.Tip(info.Error);
                sp.Children.Add(t);
                return sp;
            }
            // No reset time means no session has started yet.
            sp.Children.Add(UsageLine("Session", info.SessionReset == null ? "not started" : "resets in " + Until(info.SessionReset), info.Session, "#f2c14e"));
            sp.Children.Add(UsageLine("This week", "resets " + When(info.WeekReset), info.Week, "#8fb4ff"));
            if (info.StaleReason != null)
            {
                var asOf = Ui.Text("as of " + info.ReadAt.ToString("h:mm tt", CultureInfo.CurrentCulture), 12, "Faint");
                asOf.HorizontalAlignment = HorizontalAlignment.Center;
                asOf.Tip("Couldn't read newer numbers: " + info.StaleReason);
                sp.Children.Add(asOf);
            }
            return sp;
        }

        static Control UsageLine(string what, string reset, double percent, string color)
        {
            bool hot = percent >= 90;
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Ui.B(hot ? "#e8825f" : color), VerticalAlignment = VerticalAlignment.Center };
            string text = hot ? what + " almost out · " + reset : what + " " + Math.Round(percent) + "% · " + reset;
            var t = Ui.Text(text, 12.5, "Sub");
            if (hot) t.Foreground = Ui.B("#f0a37a");
            var row = Ui.Row(7, dot, t);
            row.HorizontalAlignment = HorizontalAlignment.Center;
            return row;
        }

        static string Until(DateTime? at)
        {
            if (at == null) return "soon";
            var d = at.Value - DateTime.Now;
            if (d.TotalMinutes < 1) return "a moment";
            return d.TotalHours >= 1 ? (int)d.TotalHours + "h " + d.Minutes.ToString("00") + "m" : d.Minutes + "m";
        }

        static string When(DateTime? at)
        {
            if (at == null) return "soon";
            return at.Value.ToString("ddd h:mm tt", CultureInfo.CurrentCulture);
        }

        Control NewItem(int index)
        {
            var ring = new Ellipse { Width = 196, Height = 196, StrokeThickness = 1.5, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 4 } };
            ring.SetResourceReference(Shape.StrokeProperty, "Dashed");
            var plus = Ui.Icon("plus", 40, "Faint", 1.5);
            var grid = new Grid { Children = { ring, plus } };
            var b = new Button { Theme = Ui.Theme("Puff"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Content = grid, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
            Avalonia.Automation.AutomationProperties.SetName(b, "Plant a new profile");
            b.Click += delegate { AddProfile(); };

            var name = Ui.Text("New", 22, "Sub", FontWeights.Medium);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            var sub = Ui.Text("Its own sign-in and chats", 13, "Muted");
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            var item = new StackPanel { Width = 250, Spacing = 14, Margin = new Thickness(6, 4, 6, 12), Children = { b, Ui.Stack(4, name, sub) } };
            Ui.Rise(item, Later(index * 260 + 1200));
            return item;
        }

        void ShowMenu(Control anchor, Profile p)
        {
            Action close = null;
            var list = new StackPanel { MinWidth = 210 };
            Action<string, string, Action, string> item = (icon, text, act, brush) =>
            {
                var r = Ui.Row(10, Ui.Icon(icon, 16, brush ?? "Muted"), Ui.Text(text, 13, brush ?? "Text"));
                var b = new Button { Theme = Ui.Theme("Row"), Content = r };
                b.Click += delegate { close(); act(); };
                list.Children.Add(b);
            };
            item("edit", "Customize...", delegate { CustomizeProfile(p); }, null);
            if (!Profiles.IsDefault(p)) item("link", "Send sign-in link...", delegate { SendLink(p, null); }, null);
            item("folder", "Open data folder", delegate { OpenFolder(p); }, null);
            item("shortcut", "Desktop shortcut", delegate { CreateShortcut(p); }, null);
            if (!Profiles.IsDefault(p))
            {
                var sep = new Border { Height = 1, Margin = new Thickness(6, 4, 6, 4) };
                sep.SetResourceReference(Border.BackgroundProperty, "Border");
                list.Children.Add(sep);
                item("trash", "Remove from list", delegate { RemoveProfile(p); }, "DotErr");
            }
            var card = Ui.Panel(list, 6, "Panel", "Line");
            card.Margin = new Thickness(12, 4, 12, 12);
            card.BoxShadow = Ui.Shadow();
            close = Ui.ShowPopup(anchor, card);
        }

        // ---------- usage (opt-in) ----------

        void ShowUsageButton()
        {
            var sp = (StackPanel)usageBtn.Content;
            ((TextBlock)sp.Children[1]).Text = settings.usage ? "Usage on" : "Usage off";
            Avalonia.Automation.AutomationProperties.SetName(usageBtn, settings.usage ? "Usage on" : "Usage off");
            usageBtn.Tip(settings.usage ? "Showing each profile's session and weekly usage. Click to turn off." : "Show each profile's session and weekly usage");
        }

        void ToggleUsage()
        {
            if (!settings.usage)
            {
                if (!Dialogs.Ask(this, "Show plan usage?", Usage.Warning, "Turn on usage", "Keep it off", true)) return;
                settings.usage = true;
            }
            else
            {
                settings.usage = false;
                usage.Clear();
            }
            try { settings.Save(); } catch { }
            ShowUsageButton();
            Render();
            RefreshUsage(true);
        }

        // Every five minutes, or when the window comes back after a minute or more.
        async void RefreshUsage(bool force)
        {
            if (Program.DemoUsage)
            {
                // --demo-usage: made-up numbers, to see the rings without reading any sign-in
                if (usage.Count > 0) return;
                double[][] demo = { new double[] { 18, 41 }, new double[] { 72, 55 }, new double[] { 94, 91 } };
                for (int i = 0; i < profiles.Count; i++)
                    usage[profiles[i]] = new UsageInfo { Session = demo[i % 3][0], Week = demo[i % 3][1], SessionReset = DateTime.Now.AddMinutes(100 - 30 * (i % 3)), WeekReset = DateTime.Today.AddDays(3).AddHours(9) };
                Render();
                return;
            }
            if (!settings.usage || fetchingUsage) return;
            var age = DateTime.Now - usageAt;
            if (!force && age < TimeSpan.FromMinutes(5) && !(IsActive && age > TimeSpan.FromMinutes(1))) return;
            fetchingUsage = true;
            usageAt = DateTime.Now;
            try
            {
                foreach (var p in profiles.ToList())
                {
                    var info = await Usage.FetchAsync(p);
                    if (!settings.usage) return;
                    // Keep showing the last numbers that came back, marked with their time.
                    if (info.Error != null && usage.TryGetValue(p, out var old) && old.Error == null)
                        info = new UsageInfo { Session = old.Session, Week = old.Week, SessionReset = old.SessionReset, WeekReset = old.WeekReset, ReadAt = old.ReadAt, StaleReason = info.Error };
                    usage[p] = info;
                }
            }
            finally { fetchingUsage = false; }
            Render();
        }

        // ---------- signing in ----------

        void ShowSignInHelp()
        {
            Dialogs.Info(this, "Signing in a new profile",
                "Windows sends every claude:// link to the default Claude window, so a new profile never hears back from the browser. DandyPicker passes the link along instead.\n\n" +
                "1.  Open the new profile and start signing in from its window.\n" +
                "2.  When your browser asks to open Claude, choose Cancel.\n" +
                "3.  Right-click the page's Open Claude button and choose Copy link.\n" +
                "4.  Come back here: DandyPicker spots the link and offers to send it.");
        }

        Control LinkStrip()
        {
            var targets = profiles.Where(p => !Profiles.IsDefault(p) && Profiles.IsRunning(running, p)).ToList();
            var dock = new DockPanel();

            var seed = Ui.Icon("seed", 26, "Text", 1.6);
            seed.Margin = new Thickness(0, 0, 16, 0);
            seed.RenderTransform = new RotateTransform(-30);
            DockPanel.SetDock(seed, Dock.Left);
            dock.Children.Add(seed);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            foreach (var t in targets)
            {
                var p = t;
                var send = Ui.Btn("Send to " + p.name, delegate { SendLink(p, clipLink); }, "BtnPrimary");
                send.Height = 44; send.CornerRadius = new CornerRadius(12); send.FontSize = 14; send.Padding = new Thickness(18, 0);
                actions.Children.Add(send);
            }
            var dismiss = Ui.IconBtn("close", null, delegate { dismissedLink = clipLink; clipLink = null; Render(); }, "BtnRound");
            dismiss.CornerRadius = new CornerRadius(12);
            dismiss.Tip("Ignore this link");
            actions.Children.Add(dismiss);
            DockPanel.SetDock(actions, Dock.Right);
            dock.Children.Add(actions);

            var link = Ui.Line(clipLink, 12, "Muted");
            link.FontFamily = Ui.Mono;
            var msg = Ui.Stack(3,
                Ui.Text(targets.Count > 0 ? "A sign-in link drifted in" : "A sign-in link drifted in. Open the profile you're signing in to send it there.", 15, "Text", FontWeights.Medium),
                link);
            msg.VerticalAlignment = VerticalAlignment.Center;
            dock.Children.Add(msg);

            var strip = Ui.Panel(dock, 14, "InfoBg", "InfoBorder", 16);
            strip.Padding = new Thickness(20, 14, 14, 14);
            return strip;
        }

        async void CheckClipboard()
        {
            if (Clipboard == null) return;
            string text = null;
            try { text = await Clipboard.TryGetTextAsync(); } catch { }
            text = (text ?? "").Trim();
            string link = text.StartsWith("claude://", StringComparison.OrdinalIgnoreCase) && text != dismissedLink ? text : null;
            if (link != clipLink) { clipLink = link; Render(); }
        }

        // Windows always routes claude:// links (e.g. the browser's sign-in hand-off) to
        // the default instance. Re-launching with the profile's --user-data-dir plus the
        // link forwards it to that profile's running instance instead.
        void SendLink(Profile p, string link)
        {
            if (!Profiles.IsRunning(running, p))
            {
                Dialogs.Info(this, "Open " + p.name + " first", "Start signing in from " + p.name + "'s own window first: the link only works in the window that asked for it.");
                return;
            }
            if (link == null) link = Dialogs.AskForLink(this, p, clipLink);
            if (link == null || !Ready()) return;
            Profiles.Launch(p, link);
            dismissedLink = link;
            clipLink = null;
            try { Clipboard?.ClearAsync(); } catch { }
            Render();
        }

        // ---------- status ----------

        void RefreshStatus()
        {
            if (refreshing) return;
            refreshing = true;
            Task.Run(() => Profiles.RunningDirs()).ContinueWith(t =>
            {
                Dispatcher.UIThread.Post(delegate
                {
                    refreshing = false;
                    if (statusKnown && t.Result.SetEquals(running)) return;
                    running = t.Result;
                    statusKnown = true;
                    Render();
                });
            });
        }

        void RefreshSoon()
        {
            DispatcherTimer.RunOnce(RefreshStatus, TimeSpan.FromSeconds(1.5));
        }

        // ---------- actions ----------

        bool Ready()
        {
            if (Profiles.ClaudeFound) return true;
            Dialogs.Info(this, "Claude Desktop isn't installed", "DandyPicker looks for it at\n" + Profiles.ClaudeAlias);
            return false;
        }

        void Open(Profile p)
        {
            if (!Ready()) return;
            // Launching a profile that's already running just brings its window forward.
            Profiles.Launch(p, null);
            RefreshSoon();
        }

        void Save()
        {
            Profiles.Save(profiles);
            Render();
        }

        void AddProfile()
        {
            var p = new Profile { dataDir = "" };
            if (!Dialogs.Customize(this, p, true, profiles.Count)) return;
            profiles.Add(p);
            Save();
        }

        void CustomizeProfile(Profile p)
        {
            var copy = new Profile { name = p.name, dataDir = p.dataDir, color = p.color, wave = p.wave, bend = p.bend, tips = p.tips };
            if (!Dialogs.Customize(this, copy, false, profiles.IndexOf(p))) return;
            p.name = copy.name; p.color = copy.color; p.wave = copy.wave; p.bend = copy.bend; p.tips = copy.tips;
            Button holder;
            if (plants.TryGetValue(p, out holder)) ((PlantView)holder.Content).SetPlant(new Plant(p, profiles.IndexOf(p)));
            Save();
        }

        void RemoveProfile(Profile p)
        {
            if (!Dialogs.Ask(this, "Remove " + p.name + "?", "It's only taken off this list. Its data folder is kept:\n" + p.dataDir, "Remove", "Cancel", false)) return;
            profiles.Remove(p);
            Save();
        }

        void OpenFolder(Profile p)
        {
            string dir = Profiles.RealFolder(p);
            if (dir != null) Profiles.OpenFolder(dir);
            else Dialogs.Info(this, "No folder yet", "Open " + p.name + " once first and Claude will create it.");
        }

        void CreateShortcut(Profile p)
        {
            Dialogs.Info(this, "Shortcut created", "It's on your desktop as " + Profiles.CreateShortcut(p) + ".");
        }
    }
}
