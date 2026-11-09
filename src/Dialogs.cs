using System;

namespace DandyPicker
{
    // A dialog window that follows the theme (background, fonts, title bar).
    class ThemedWindow : Window
    {
        readonly Window owner;

        public ThemedWindow(Window owner, string title, double width)
        {
            this.owner = owner;
            Title = title;
            Width = width;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            FontFamily = Ui.UiFont;
            FontSize = 14;
            this.SetResourceReference(BackgroundProperty, "Bg");
            this.SetResourceReference(ForegroundProperty, "Text");
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        // Shows the dialog and returns once it's closed, so callers can read what was
        // picked on the next line.
        public void ShowModal()
        {
            var frame = new DispatcherFrame();
            Closed += delegate { frame.Continue = false; };
            if (owner != null && owner.IsVisible) ShowDialog(owner); else Show();
            Dispatcher.UIThread.PushFrame(frame);
        }

        // Standard layout: content, then a right-aligned button row.
        public void Layout(Control content, params Button[] buttons)
        {
            var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0), Spacing = 8 };
            foreach (var b in buttons) { b.MinWidth = 90; row.Children.Add(b); }
            DockPanel.SetDock(row, Dock.Bottom);
            root.Children.Add(row);
            root.Children.Add(content);
            Content = root;
        }
    }

    static class Dialogs
    {
        public static bool Ask(Window owner, string title, string message, string ok = "OK", string cancel = "Cancel", bool primary = true)
        {
            var w = new ThemedWindow(owner, title, 480);
            bool result = false;
            var okBtn = Ui.Btn(ok, delegate { result = true; w.Close(); }, primary ? "BtnPrimary" : "Btn");
            okBtn.IsDefault = true;
            var content = Ui.Stack(10, Ui.Text(title, 18, "Text", FontWeights.SemiBold), Ui.Text(message, 14, "Sub"));
            var buttons = cancel != null
                ? new[] { Ui.Btn(cancel, delegate { w.Close(); }), okBtn }
                : new[] { okBtn };
            w.Layout(content, buttons);
            w.ShowModal();
            return result;
        }

        public static void Info(Window owner, string title, string message)
        {
            Ask(owner, title, message, "OK", null, true);
        }

        // Asks for the claude:// link to send to 'p'. Returns it, or null.
        public static string AskForLink(Window owner, Profile p, string current)
        {
            var w = new ThemedWindow(owner, "Send sign-in link", 540);
            string result = null;
            var box = new TextBox { Text = current ?? "", PlaceholderText = "claude://...", FontFamily = Ui.Mono, FontSize = 12.5 };
            var err = Ui.Text("", 12.5, "DotErr");
            err.IsVisible = false;
            var send = Ui.Btn("Send to " + p.name, delegate
            {
                string url = (box.Text ?? "").Trim();
                if (!url.StartsWith("claude://", StringComparison.OrdinalIgnoreCase))
                {
                    err.Text = "That isn't a claude:// link.";
                    err.IsVisible = true;
                    return;
                }
                result = url;
                w.Close();
            }, "BtnPrimary");
            send.IsDefault = true;
            var content = Ui.Stack(10,
                Ui.Text("Send a sign-in link", 18, "Text", FontWeights.SemiBold),
                Ui.Text("Paste the claude:// link for " + p.name + ". In the browser, cancel the \"Open Claude?\" prompt, then right-click the page's Open Claude button and choose Copy link.", 13.5, "Sub"),
                box, err);
            w.Layout(content, Ui.Btn("Cancel", delegate { w.Close(); }), send);
            w.Opened += delegate { box.Focus(); box.SelectAll(); };
            w.ShowModal();
            return result;
        }

        // Add a profile, or customize one: its name and its dandelion (color, waviness,
        // stem and seed tips), with a live preview. Returns false if cancelled.
        public static bool Customize(Window owner, Profile p, bool adding, int index)
        {
            var w = new ThemedWindow(owner, adding ? "Plant a profile" : "Customize " + p.name, 720);
            bool saved = false;
            string color = p.color;
            double wave = p.wave ?? Dandelion.DefaultWave, bend = p.bend ?? Dandelion.DefaultBend;
            string tips = p.tips ?? Dandelion.DefaultTips;
            bool code = false;

            var name = new TextBox { Text = adding ? "" : p.name, PlaceholderText = "e.g. Work or Personal" };
            Func<string> dir = delegate { return adding ? Profiles.NewDataDir((name.Text ?? "").Trim(), index) : Profiles.DataDirOf(p); };
            Func<string> payload = delegate { return Dandelion.Payload(name.Text, dir()); };

            var preview = new Border { Width = 200, Height = 374, HorizontalAlignment = HorizontalAlignment.Center };
            var words = Ui.Text("", 15, "Text");
            words.HorizontalAlignment = HorizontalAlignment.Center;
            var reads = Ui.Text("", 11.5, "Faint");
            reads.FontFamily = Ui.Mono;
            reads.HorizontalAlignment = HorizontalAlignment.Center;
            reads.TextAlignment = TextAlignment.Center;
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
            for (int i = 0; i < 4; i++)
            {
                var sq = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = Ui.B(Dandelion.LevelColors[i]), VerticalAlignment = VerticalAlignment.Center };
                var t = Ui.Text(new[] { "00", "01", "10", "11" }[i], 11, "Muted");
                t.FontFamily = Ui.Mono;
                legend.Children.Add(Ui.Row(5, sq, t));
            }
            var codeBtn = Ui.Btn("", null, "BtnLink");
            codeBtn.HorizontalAlignment = HorizontalAlignment.Center;
            codeBtn.FontSize = 13;

            var swatches = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            var tipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var where = Ui.Line("", 11.5, "Faint");
            where.FontFamily = Ui.Mono;

            Action draw = null;
            draw = delegate
            {
                string pl = payload();
                string shown = string.IsNullOrEmpty(color) ? Dandelion.DefaultColor(pl) : color;
                // a grown, running plant, so the preview shows the whole look (hover it to see it sway)
                preview.Child = new PlantView(new Plant(pl, shown, wave, bend, tips, index), index, true, PlantView.Now - 100000) { Width = 200, Height = 374, Code = code };
                words.Text = Dandelion.SeedWords(pl);
                reads.Text = code ? "Reads: " + Dandelion.Decode(Dandelion.Levels(pl)) : "From the stem, clockwise: each seed's length is 2 bits.";
                legend.IsVisible = code;
                codeBtn.Content = code ? "Hide the code" : "Show the code";
                where.Text = dir();
                where.Tip(dir());

                swatches.Children.Clear();
                for (int i = 0; i < Dandelion.Colors.Length; i++)
                {
                    string c = Dandelion.Colors[i];
                    var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Ui.B(c) };
                    var b = new Button { Theme = Ui.Theme("Card"), Width = 36, Height = 36, Padding = new Thickness(8), Content = dot, CornerRadius = new CornerRadius(18) };
                    b.Tip(Dandelion.ColorNames[i]);
                    Avalonia.Automation.AutomationProperties.SetName(b, Dandelion.ColorNames[i]);
                    if (string.Equals(c, shown, StringComparison.OrdinalIgnoreCase)) { b.SetResourceReference(Button.BorderBrushProperty, "Accent"); b.BorderThickness = new Thickness(2); }
                    b.Click += delegate { color = c; draw(); };
                    swatches.Children.Add(b);
                }

                tipRow.Children.Clear();
                foreach (var t in Dandelion.Tips)
                {
                    string kind = t;
                    var b = Ui.Btn(char.ToUpper(kind[0]) + kind.Substring(1), delegate { tips = kind; draw(); }, kind == tips ? "BtnPrimary" : "Btn");
                    b.MinWidth = 72;
                    tipRow.Children.Add(b);
                }
            };
            // The title of the profile's Claude window; blank uses "Claude (name)".
            var titleBox = new TextBox { Text = p.windowTitle ?? "" };
            Action showTitleHint = delegate { titleBox.PlaceholderText = WindowTitles.TitleOf(new Profile { name = (name.Text ?? "").Trim() }); };
            showTitleHint();

            codeBtn.Click += delegate { code = !code; draw(); };
            name.TextChanged += delegate { draw(); showTitleHint(); };

            Func<string, double, Action<double>, Control> slider = (label, value, set) =>
            {
                var s = new Slider { Minimum = 0, Maximum = 1, Value = value, Margin = new Thickness(0, -6, 0, -6) };
                s.ValueChanged += delegate { set(s.Value); draw(); };
                return Ui.Stack(0, Ui.Text(label, 12, "Muted"), s);
            };

            var left = Ui.Stack(12, preview, words, reads, legend, codeBtn);
            left.Width = 260;
            left.Margin = new Thickness(0, 0, 28, 0);
            var right = Ui.Stack(14,
                Ui.Stack(6, Ui.Text("Name", 12, "Muted"), name, where),
                Ui.Stack(6, Ui.Text("Window title (leave blank for the default)", 12, "Muted"), titleBox),
                Ui.Stack(8, Ui.Text("Color", 12, "Muted"), swatches),
                slider("Waviness", wave, v => wave = v),
                slider("Stem bend", bend, v => bend = v),
                Ui.Stack(8, Ui.Text("Seed tips", 12, "Muted"), tipRow));
            var body = new DockPanel();
            DockPanel.SetDock(left, Dock.Left);
            body.Children.Add(left);
            body.Children.Add(right);
            draw();

            var save = Ui.Btn(adding ? "Plant it" : "Save", delegate
            {
                string n = (name.Text ?? "").Trim();
                if (n.Length == 0) { name.Focus(); return; }
                p.name = n;
                if (adding) p.dataDir = dir();
                p.color = color;
                p.wave = Math.Round(wave, 2); p.bend = Math.Round(bend, 2); p.tips = tips;
                string t = (titleBox.Text ?? "").Trim();
                p.windowTitle = t.Length == 0 ? null : t;
                saved = true;
                w.Close();
            }, "BtnPrimary");
            save.IsDefault = true;

            var content = Ui.Stack(6,
                Ui.Text(adding ? "Plant a profile" : "Customize " + p.name, 20, "Text", FontWeights.SemiBold),
                Gap(Ui.Text(adding
                    ? "A profile is one Claude account, with its own sign-in, chats and settings. Its seeds spell its name and folder."
                    : "The seeds spell this profile's name and folder. The rest is up to you.", 13, "Muted"), 14),
                body);
            w.Layout(content, Ui.Btn("Cancel", delegate { w.Close(); }), save);
            w.Opened += delegate { name.Focus(); name.SelectAll(); };
            w.ShowModal();
            return saved;
        }

        static Control Gap(Control c, double below)
        {
            c.Margin = new Thickness(0, 0, 0, below);
            return c;
        }
    }
}
