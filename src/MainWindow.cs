using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Shapes;
using Avalonia.Input.Platform;

namespace DandyPicker
{
    // "Seed Field": a headline, a dandelion per profile (glowing while it runs), and a
    // strip at the bottom when a sign-in link drifts onto the clipboard.
    class MainWindow : Window
    {
        static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");

        List<Profile> profiles;
        HashSet<string> running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool refreshing;
        string clipLink;          // a claude:// link on the clipboard, if any
        string dismissedLink;     // ...that the user said to ignore

        readonly TextBlock caption;
        readonly WrapPanel field;
        readonly StackPanel bottom;
        readonly DispatcherTimer timer;

        public MainWindow()
        {
            Title = Program.Title;
            Width = 960; Height = 680; MinWidth = 620; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Ui.UiFont;
            FontSize = 14;
            this.SetResourceReference(BackgroundProperty, "Bg");
            this.SetResourceReference(ForegroundProperty, "Text");
            UseLayoutRounding = true;
            try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://DandyPicker/dandelion.ico"))); } catch { }

            profiles = Profiles.Load();

            var root = new DockPanel { Margin = new Thickness(56, 36, 56, 28) };

            // headline
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var plant = Ui.IconBtn("plus", "Plant a profile", AddProfile, "BtnPillOutline", "Text");
            plant.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(plant, Dock.Right);
            head.Children.Add(plant);
            caption = Ui.Text("", 12.5, "Muted");
            caption.LetterSpacing = 2;
            var title = new TextBlock { FontFamily = Display, FontSize = 44, FontWeight = FontWeights.Light, LetterSpacing = -0.8 };
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

            Render();

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += delegate { RefreshStatus(); CheckClipboard(); };
            Opened += delegate { RefreshStatus(); CheckClipboard(); timer.Start(); };
            Activated += delegate { CheckClipboard(); };
            Closed += delegate { timer.Stop(); };
        }

        // ---------- page ----------

        void Render()
        {
            int on = profiles.Count(p => Profiles.IsRunning(running, p));
            caption.Text = (CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(DateTime.Now.DayOfWeek) + " · " + on + " of " + profiles.Count + " open").ToUpperInvariant();

            field.Children.Clear();
            foreach (var p in profiles) field.Children.Add(PuffItem(p));
            field.Children.Add(NewItem());

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

        Control PuffItem(Profile p)
        {
            bool on = Profiles.IsRunning(running, p);

            var puff = new Button { Theme = Ui.Theme("Puff"), Content = Dandelion.Flower(p, 196, !on) };
            if (on)
            {
                puff.SetResourceReference(Button.BackgroundProperty, "RunBg");
                puff.SetResourceReference(Button.BorderBrushProperty, "OkBorder");
            }
            puff.Tip("Customize " + p.name + "'s dandelion");
            Avalonia.Automation.AutomationProperties.SetName(puff, "Customize " + p.name);
            puff.Click += delegate { CustomizeProfile(p); };
            puff.DoubleTapped += delegate { Open(p); };
            // A soft green glow while the profile is running.
            var frame = new Border { CornerRadius = new CornerRadius(98), Child = puff, HorizontalAlignment = HorizontalAlignment.Center };
            if (on)
                frame.BoxShadow = new BoxShadows(
                    new BoxShadow { Spread = 10, Color = Color.FromArgb(20, 76, 195, 138) },
                    new[] { new BoxShadow { Blur = 60, Color = Color.FromArgb(64, 76, 195, 138) } });

            var name = Ui.Line(p.name, 22, "Text", FontWeights.Medium);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.MaxWidth = 220;

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

            var words = Ui.Text(Dandelion.SeedWords(Dandelion.PayloadOf(p)), 11.5, "Faint");
            words.FontFamily = Ui.Mono;
            words.HorizontalAlignment = HorizontalAlignment.Center;

            var open = Ui.Btn(on ? "Show window" : "Open", delegate { Open(p); }, on ? "BtnPillOutline" : "BtnPill");
            var more = Ui.IconBtn("more", null, null, "BtnRound");
            more.Tip("More");
            Avalonia.Automation.AutomationProperties.SetName(more, "More for " + p.name);
            more.Click += delegate { ShowMenu(more, p); };
            var actions = Ui.Row(4, open, more);
            actions.HorizontalAlignment = HorizontalAlignment.Center;
            actions.Margin = new Thickness(48, 4, 0, 0);   // centers the main button; ⋯ hangs off to its right

            var labels = Ui.Stack(4, name, status, words);
            return new StackPanel { Width = 250, Spacing = 14, Margin = new Thickness(6, 12, 6, 12), Children = { frame, labels, actions } };
        }

        Control NewItem()
        {
            var ring = new Ellipse { Width = 196, Height = 196, StrokeThickness = 1.5, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 4 } };
            ring.SetResourceReference(Shape.StrokeProperty, "Dashed");
            var plus = Ui.Icon("plus", 40, "Faint", 1.5);
            var grid = new Grid { Children = { ring, plus } };
            var b = new Button { Theme = Ui.Theme("Puff"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Content = grid, HorizontalAlignment = HorizontalAlignment.Center };
            Avalonia.Automation.AutomationProperties.SetName(b, "Plant a new profile");
            b.Click += delegate { AddProfile(); };

            var name = Ui.Text("New", 22, "Sub", FontWeights.Medium);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            var sub = Ui.Text("Its own sign-in and chats", 13, "Muted");
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            return new StackPanel { Width = 250, Spacing = 14, Margin = new Thickness(6, 12, 6, 12), Children = { b, Ui.Stack(4, name, sub) } };
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
                    if (t.Result.SetEquals(running)) return;
                    running = t.Result;
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
            if (!Dialogs.Customize(this, copy, false, profiles.Count)) return;
            p.name = copy.name; p.color = copy.color; p.wave = copy.wave; p.bend = copy.bend; p.tips = copy.tips;
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
