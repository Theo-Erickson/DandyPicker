using System;
using System.Globalization;
using System.Text;
using Avalonia.Controls.Shapes;

namespace DandyPicker
{
    // The dandelions. Each profile's flower is grown from its name and folder:
    //  - The seeds spell them out. Starting at the gold key seed and going clockwise,
    //    each seed is one of four lengths (2 bits), so four seeds make one byte of
    //    "name/folder". The picture can be read back like a QR code.
    //  - The same text hashes to three seed words (the flower's nickname) and a
    //    default color.
    //  - Color, waviness, stem bend and seed tips are the profile's own look; they
    //    never change what the seeds say.
    static class Dandelion
    {
        static readonly string[] Words =
        {
            "amber", "hollow", "kite", "fern", "lumen", "pip", "drift", "moss", "wren", "tansy", "quill", "sorrel", "ember", "loam", "brook", "thistle",
            "vale", "glim", "nettle", "sprig", "marrow", "dune", "hazel", "cobble", "flint", "mallow", "reed", "sedge", "bramble", "pollen", "tarn", "wisp",
        };
        public static readonly string[] Colors = { "#ffffff", "#f2c14e", "#e8825f", "#e06c9f", "#9b7cf0", "#5aa9f0", "#4cc3a0", "#b5e06a" };
        public static readonly string[] ColorNames = { "White", "Gold", "Coral", "Rose", "Violet", "Sky", "Mint", "Lime" };
        public static readonly string[] Tips = { "puff", "fan", "star" };
        public static readonly string[] LevelColors = { "#5aa9f0", "#4cc3a0", "#e9d98a", "#e8825f" };
        static readonly double[] Lens = { 66, 90, 114, 138 };
        const int MaxBytes = 48;

        public const double DefaultWave = 0.35, DefaultBend = 0.62;
        public const string DefaultTips = "fan";

        // ---------- what the flower says ----------

        public static string Payload(string name, string dataDir)
        {
            return (name ?? "").Trim() + "/" + System.IO.Path.GetFileName((dataDir ?? "").TrimEnd('\\', '/'));
        }

        public static string PayloadOf(Profile p) { return Payload(p.name, Profiles.DataDirOf(p)); }

        // FNV-1a over the UTF-16 units, as the design lab does, so both grow the same flower.
        public static uint Hash(string s)
        {
            uint h = 0x811c9dc5;
            foreach (char c in s) { h ^= c; h = unchecked(h * 0x01000193); }
            return h;
        }

        public static string SeedWords(string payload)
        {
            uint h = Hash(payload);
            return Words[h & 31] + " · " + Words[(h >> 5) & 31] + " · " + Words[(h >> 10) & 31];
        }

        public static string DefaultColor(string payload) { return Colors[(Hash(payload) >> 15) % Colors.Length]; }

        public static string ColorOf(Profile p) { return string.IsNullOrEmpty(p.color) ? DefaultColor(PayloadOf(p)) : p.color; }

        // The 2-bit seed lengths for the payload, most significant pair first.
        public static int[] Levels(string payload)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            int n = Math.Min(bytes.Length, MaxBytes);
            var levels = new int[n * 4];
            for (int i = 0; i < n; i++)
            {
                byte b = bytes[i];
                levels[i * 4] = (b >> 6) & 3; levels[i * 4 + 1] = (b >> 4) & 3; levels[i * 4 + 2] = (b >> 2) & 3; levels[i * 4 + 3] = b & 3;
            }
            return levels;
        }

        // Reads the text back out of the seed lengths.
        public static string Decode(int[] levels)
        {
            var bytes = new byte[levels.Length / 4];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)((levels[i * 4] << 6) | (levels[i * 4 + 1] << 4) | (levels[i * 4 + 2] << 2) | levels[i * 4 + 3]);
            return Encoding.UTF8.GetString(bytes);
        }

        // ---------- drawing ----------

        static string F(double v) { return v.ToString("0.#", CultureInfo.InvariantCulture); }

        static Path Stroke(string d, IBrush brush, double width)
        {
            return new Path { Data = Geometry.Parse(d), Stroke = brush, StrokeThickness = width, StrokeLineCap = PenLineCap.Round };
        }

        public static Control Flower(Profile p, double size, bool dim = false, bool code = false)
        {
            return Flower(PayloadOf(p), ColorOf(p), p.wave ?? DefaultWave, p.bend ?? DefaultBend, p.tips ?? DefaultTips, size, dim, code);
        }

        // A square picture of the flower, its head a little above center and the stem
        // running off the bottom (so it fills a round frame).
        public static Control Flower(string payload, string color, double wave, double bend, string tips, double size, bool dim = false, bool code = false)
        {
            const double cx = 190, cy = 180, r0 = 16;
            uint h = Hash(payload);
            int[] levels = Levels(payload);
            int n = levels.Length + 1;

            var groups = new[] { new StringBuilder(), new StringBuilder(), new StringBuilder(), new StringBuilder() };
            var lines = new StringBuilder();
            var dots = new StringBuilder();
            string keyRay = "", keyTip = "";
            for (int k = 0; k < n; k++)
            {
                double a = -Math.PI / 2 + 2 * Math.PI * k / n;
                double ux = Math.Cos(a), uy = Math.Sin(a), px = -uy, py = ux;
                double len = k == 0 ? 156 : Lens[levels[k - 1]];
                double amp = wave * 18 * (k % 2 == 1 ? 1 : -1) * (0.6 + 0.4 * Math.Sin(k * 1.3 + h % 7));
                double x0 = cx + ux * r0, y0 = cy + uy * r0, x1 = cx + ux * len, y1 = cy + uy * len;
                double mx = cx + ux * (r0 + len) / 2 + px * amp, my = cy + uy * (r0 + len) / 2 + py * amp;
                string d = "M" + F(x0) + " " + F(y0) + " Q" + F(mx) + " " + F(my) + " " + F(x1) + " " + F(y1) + " ";
                double ta = Math.Atan2(y1 - my, x1 - mx);
                if (k == 0)
                {
                    keyRay = d;
                    keyTip = "M" + F(x1 - 6) + " " + F(y1) + " a6 6 0 1 0 12 0 a6 6 0 1 0 -12 0";
                    continue;
                }
                groups[levels[k - 1]].Append(d);
                if (tips == "puff")
                    dots.Append("M" + F(x1 - 4) + " " + F(y1) + " a4 4 0 1 0 8 0 a4 4 0 1 0 -8 0 ");
                else if (tips == "star")
                    for (int s = 0; s < 6; s++)
                    {
                        double sa = ta + s * Math.PI / 3;
                        lines.Append("M" + F(x1) + " " + F(y1) + " L" + F(x1 + Math.Cos(sa) * 6.5) + " " + F(y1 + Math.Sin(sa) * 6.5) + " ");
                    }
                else
                    for (int j = -2; j <= 2; j++)
                    {
                        double fa = ta + j * 0.42;
                        lines.Append("M" + F(x1) + " " + F(y1) + " L" + F(x1 + Math.Cos(fa) * 11) + " " + F(y1 + Math.Sin(fa) * 11) + " ");
                    }
            }

            var c = new Canvas { Width = 380, Height = 380, ClipToBounds = true };
            var halo = new Ellipse { Width = 340, Height = 340, Fill = Ui.B("#ffffff", 10) };
            Canvas.SetLeft(halo, cx - 170); Canvas.SetTop(halo, cy - 170);
            c.Children.Add(halo);
            double b = (bend - 0.5) * 2;
            c.Children.Add(Stroke("M" + F(cx) + " " + F(cy + 12) + " C" + F(cx + b * 40) + " 300 " + F(cx - b * 60) + " 400 " + F(cx + b * 30) + " 510", Ui.B("#7ec470"), 5));
            var ink = Ui.B(color);
            for (int l = 0; l < 4; l++)
                if (groups[l].Length > 0) c.Children.Add(Stroke(groups[l].ToString(), code ? Ui.B(LevelColors[l]) : ink, 1.4));
            var tipInk = code ? Ui.B("#6d7697") : ink;
            if (lines.Length > 0) c.Children.Add(Stroke(lines.ToString(), tipInk, 1.2));
            if (dots.Length > 0) c.Children.Add(new Path { Data = Geometry.Parse(dots.ToString()), Fill = tipInk });
            c.Children.Add(Stroke(keyRay, Ui.B("#f2c14e"), 2.4));
            c.Children.Add(new Path { Data = Geometry.Parse(keyTip), Fill = Ui.B("#f2c14e") });
            var core = new Ellipse { Width = 24, Height = 24, Fill = Ui.B("#c4aa5a") };
            Canvas.SetLeft(core, cx - 12); Canvas.SetTop(core, cy - 12);
            c.Children.Add(core);
            if (dim) c.Opacity = 0.55;
            return new Viewbox { Width = size, Height = size, Child = c };
        }

        // DandyPicker's own badge: a dusk-blue circle with a white seed head on a green stem.
        public static Control Badge(double size)
        {
            var c = new Canvas { Width = 256, Height = 256 };
            var bg = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#3a6ea5"), 0), new GradientStop(Color.Parse("#204870"), 1) }
            };
            var disc = new Ellipse { Width = 248, Height = 248, Fill = bg };
            Canvas.SetLeft(disc, 4); Canvas.SetTop(disc, 4);
            c.Children.Add(disc);
            c.Children.Add(Stroke("M124 104 C130 164 110 204 128 236", Ui.B("#7ec470"), 9));
            var rays = new StringBuilder();
            var tips = new StringBuilder();
            for (int i = 0; i < 40; i++)
            {
                double a = 2 * Math.PI * i / 40 + 0.06 * Math.Sin(i * 1.7);
                double len = 74 * (0.9 + 0.1 * Math.Cos(i * 2.3));
                double x = 124 + Math.Cos(a) * len, y = 104 + Math.Sin(a) * len;
                rays.Append("M" + F(124 + Math.Cos(a) * 10) + " " + F(104 + Math.Sin(a) * 10) + " L" + F(x) + " " + F(y) + " ");
                tips.Append("M" + F(x - 7) + " " + F(y) + " a7 7 0 1 0 14 0 a7 7 0 1 0 -14 0 ");
            }
            c.Children.Add(Stroke(rays.ToString(), Ui.B("#ffffff", 230), 2.6));
            c.Children.Add(new Path { Data = Geometry.Parse(tips.ToString()), Fill = Ui.B("#ffffff", 235) });
            var core = new Ellipse { Width = 26, Height = 26, Fill = Ui.B("#c4aa5a") };
            Canvas.SetLeft(core, 111); Canvas.SetTop(core, 91);
            c.Children.Add(core);
            return new Viewbox { Width = size, Height = size, Child = c };
        }
    }
}
