using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Media.Immutable;

namespace DandyPicker
{
    // Draws one profile's plant and animates it, all from the time since it appeared:
    //  - growing in: soil, roots, stem, leaves, then the seeds in reading order, and the
    //    frame popping out last;
    //  - hover: the plant sways from the soil and each seed flutters a little out of step;
    //  - opening: the head unfolds with a little bounce, a green pulse, and data starts to
    //    run down the roots and leave as packets;
    //  - closing: seeds wilt toward the ground, leaves fold up, roots go quiet;
    //  - usage rings round the frame when usage is on.
    class PlantView : Control
    {
        public static readonly Stopwatch Clock = Stopwatch.StartNew();
        public static double Now { get { return Clock.Elapsed.TotalMilliseconds; } }

        // Drawn in a 380 x 700 space, scaled to fit, with Top spare above it: the week ring
        // reaches above the space (to y = -10.5 with its stroke), so everything is drawn Top
        // lower; otherwise it would be cut off at the control's top edge.
        const double W = 380, Top = 12, H = 700 + Top;
        const double PacketCycle = 3200;

        static readonly Poly SessionRing = Poly.RoundedRect(14, 4, 352, 352, 94);
        static readonly Poly WeekRing = Poly.RoundedRect(2, -8, 376, 376, 106);
        static readonly Geometry LeafShape = Geometry.Parse("M0 0 Q22 -17 52 -3 Q25 11 0 0Z");
        static readonly Geometry LeafVeins = Geometry.Parse("M2 -0.5 Q26 -5 50 -3 M14 -2.6 L21 -9.5 M26 -3.8 L34 -10 M37 -3.6 L43 -7.5 M17 -1.2 L24 4.5 M29 -2.4 L37 3");
        static readonly Typeface PacketFont = new Typeface(Ui.Mono, FontStyle.Normal, FontWeight.SemiBold);

        Plant plant;
        readonly int index;
        readonly double appear;            // Clock time it started growing
        bool running;
        double changedAt = double.NegativeInfinity;   // last Running change (none yet: no transition)
        double hover, lastFrame;
        bool frameQueued;
        UsageInfo usage;
        bool code;
        FormattedText[] packetText;

        public PlantView(Profile p, int index, bool running, double appear)
        {
            this.index = index;
            this.running = running;
            this.appear = appear;
            SetPlant(new Plant(p, index));
            ClipToBounds = false;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        // A view of a plant that isn't (yet) a saved profile, e.g. the Customize preview.
        public PlantView(Plant plant, int index, bool running, double appear)
        {
            this.index = index;
            this.running = running;
            this.appear = appear;
            SetPlant(plant);
            ClipToBounds = false;
        }

        double T0 { get { return index * 260; } }

        public void SetPlant(Plant p)
        {
            plant = p;
            packetText = new FormattedText[p.Packets.Count];
            var ink = new ImmutableSolidColorBrush(Color.Parse("#7fe0ad"));
            for (int i = 0; i < packetText.Length; i++)
                packetText[i] = new FormattedText(p.Packets[i].Bits, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, PacketFont, 12, ink);
            Kick();
        }

        public bool Running
        {
            get { return running; }
            set { if (value == running) return; running = value; changedAt = Now; Kick(); }
        }

        public UsageInfo Usage { get { return usage; } set { usage = value; Kick(); } }

        // Color the seeds by the two bits each one holds.
        public bool Code { get { return code; } set { code = value; Kick(); } }

        protected override Size MeasureOverride(Size available) { return new Size(Width, Height); }

        protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Kick(); }
        protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Kick(); }

        // Asks for frames until nothing is moving.
        void Kick()
        {
            if (frameQueued) return;
            var top = TopLevel.GetTopLevel(this);
            if (top == null) { InvalidateVisual(); return; }
            frameQueued = true;
            top.RequestAnimationFrame(delegate { frameQueued = false; InvalidateVisual(); });
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Kick(); }

        // ---------- easing ----------

        static double Clamp01(double x) { return x < 0 ? 0 : x > 1 ? 1 : x; }
        static double Out3(double x) { x = Clamp01(x); return 1 - (1 - x) * (1 - x) * (1 - x); }
        static double OutBack(double x) { x = Clamp01(x); const double c1 = 1.70158, c3 = c1 + 1; return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2); }
        static double InOut(double x) { x = Clamp01(x); return x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2; }

        // 0 = open, 1 = wilted; 'delay' staggers seeds and leaves.
        double Wilt(double now, double delay)
        {
            if (double.IsNegativeInfinity(changedAt)) return running ? 0 : 1;
            double x = (now - changedAt - delay) / (running ? 800 : 1100);
            if (x <= 0) return running ? 1 : 0;
            if (x >= 1) return running ? 0 : 1;
            return running ? 1 - OutBack(x) : InOut(x);
        }

        // The frame scales out, overshoots and settles.
        static void FramePop(double x, out double scale, out double alpha)
        {
            x = Clamp01(x);
            if (x < 0.55) { double q = Out3(x / 0.55); scale = 0.35 + (1.09 - 0.35) * q; alpha = q; }
            else if (x < 0.78) { double q = InOut((x - 0.55) / 0.23); scale = 1.09 + (0.97 - 1.09) * q; alpha = 1; }
            else { double q = InOut((x - 0.78) / 0.22); scale = 0.97 + (1 - 0.97) * q; alpha = 1; }
        }

        static Color Mix(Color a, Color b, double t)
        {
            t = Clamp01(t);
            return Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        static Color C(string hex, double alpha = 1)
        {
            var c = Color.Parse(hex);
            return Color.FromArgb((byte)(255 * Clamp01(alpha)), c.R, c.G, c.B);
        }

        static IBrush Br(Color c) { return new ImmutableSolidColorBrush(c); }

        static Pen P(Color c, double w) { return new Pen(Br(c), w, null, PenLineCap.Round, PenLineJoin.Round); }

        static Matrix About(Point c, Matrix m)
        {
            return Matrix.CreateTranslation(-c.X, -c.Y) * m * Matrix.CreateTranslation(c.X, c.Y);
        }

        // ---------- drawing ----------

        public override void Render(DrawingContext ctx)
        {
            double now = Now, t = now - appear, t0 = T0;
            double dt = lastFrame > 0 ? Math.Min(100, now - lastFrame) : 16;
            lastFrame = now;
            hover += ((IsPointerOver ? 1 : 0) - hover) * Math.Min(1, dt / 160);
            if (hover < 0.002) hover = 0;
            bool moving = t < t0 + 3400 || hover > 0 || running || now - changedAt < 2400;

            double scale = Math.Min(Bounds.Width / W, Bounds.Height / H);
            using (ctx.PushTransform(Matrix.CreateTranslation(0, Top) * Matrix.CreateScale(scale, scale)))
            {
                double headWilt = Wilt(now, 0);
                double open = 1 - Clamp01(headWilt);
                var center = new Point(Plant.Cx, Plant.Cy);

                // frame, halo and usage rings pop in near the end of the bloom
                double fs, fa;
                FramePop((t - t0 - 1650) / 1000, out fs, out fa);
                if (fa > 0)
                {
                    using (ctx.PushTransform(About(center, Matrix.CreateScale(fs, fs))))
                    using (ctx.PushOpacity(fa))
                    {
                        var fill = Mix(C("#121831"), C("#16223a"), open);
                        var stroke = Mix(C("#1f2745"), C("#3c7d64"), open);
                        stroke = Mix(stroke, open > 0.5 ? C("#55a383") : C("#3a4675"), hover);
                        var glow = open > 0.01
                            ? new BoxShadows(new BoxShadow { Blur = 44, Spread = 6, Color = C("#4cc38a", 0.22 * open) })
                            : default(BoxShadows);
                        ctx.DrawRectangle(Br(fill), P(stroke, 1.4), new RoundedRect(new Rect(24, 14, 332, 332), 84), glow);
                        ctx.DrawEllipse(Br(C("#ffffff", 0.035)), null, center, 156, 156);
                        if (usage != null && usage.Error == null) DrawRings(ctx, t - t0 - 1800, now);
                    }
                }

                // roots, then soil
                double quiet = 1 - 0.72 * Clamp01(headWilt);
                using (ctx.PushOpacity(quiet))
                {
                    for (int i = 0; i < plant.Roots.Count; i++)
                    {
                        var r = plant.Roots[i];
                        double grow = Out3((t - t0 - 200 - i * 80) / 700);
                        if (grow <= 0) continue;
                        ctx.DrawGeometry(null, P(C(open > 0.5 ? "#d6b25a" : "#b8913f"), 2.4), r.Line.Partial(grow));
                        double pad = Clamp01((t - t0 - 700 - i * 80) / 400);
                        if (pad > 0) ctx.DrawEllipse(Br(C("#0b0f1d", pad)), P(C("#d6b25a", pad), 2), r.Pad, 4.5, 4.5);
                    }
                }
                double soil = Clamp01((t - t0) / 500);
                if (soil > 0)
                    using (ctx.PushOpacity(soil))
                    {
                        ctx.DrawGeometry(Br(C("#3a2a1f")), null, plant.SoilTop);
                        ctx.DrawGeometry(Br(C("#2a1e16")), null, plant.SoilBand);
                        ctx.DrawGeometry(null, P(C("#56412f"), 3), plant.SoilRim);
                        foreach (var s in plant.Specks) ctx.DrawEllipse(Br(s.Item3), null, s.Item1, s.Item2, s.Item2);
                        foreach (var r in plant.Rocks)
                        {
                            ctx.DrawEllipse(Br(r.Fill), null, r.At, r.Rx, r.Ry);
                            ctx.DrawEllipse(Br(C("#b3aa9c", 0.45)), null, new Point(r.At.X - r.Rx * 0.3, r.At.Y - r.Rx * 0.25), r.Rx * 0.4, r.Rx * 0.18);
                        }
                        ctx.DrawGeometry(null, P(C("#6fb85f"), 2), plant.Grass);
                    }

                // the plant itself sways from the soil while hovered
                double sway = hover * 1.8 * Math.PI / 180 * Math.Sin(2 * Math.PI * now / 2600);
                using (ctx.PushTransform(About(new Point(Plant.Cx, Plant.SoilY), Matrix.CreateRotation(sway))))
                {
                    double stem = Out3((t - t0 - 450) / 600);
                    if (stem > 0) ctx.DrawGeometry(null, P(C("#7ec470"), 5), plant.Stem.Partial(stem));

                    for (int l = 0; l < plant.Leaves.Count; l++) DrawLeaf(ctx, plant.Leaves[l], OutBack((t - t0 - 760 - l * 120) / 500), Wilt(now, l * 90));

                    DrawHead(ctx, now, t - t0, headWilt);
                }

                if (running) DrawPackets(ctx, now, t);
            }

            if (moving) Kick();
        }

        void DrawLeaf(DrawingContext ctx, Plant.Leaf leaf, double pop, double fold)
        {
            if (pop <= 0) return;
            var m = Matrix.CreateScale(pop, pop)
                  * Matrix.CreateScale(1 - 0.14 * fold, 1 - 0.28 * fold) * Matrix.CreateRotation(-30 * Math.PI / 180 * fold)
                  * Matrix.CreateScale(leaf.Size, leaf.Right ? leaf.Size : -leaf.Size)
                  * Matrix.CreateRotation(leaf.Tilt * Math.PI / 180)
                  * Matrix.CreateTranslation(leaf.At.X, leaf.At.Y);
            using (ctx.PushTransform(m))
            {
                ctx.DrawGeometry(Br(C("#5fae58")), P(C("#7ec470"), 1), LeafShape);
                ctx.DrawGeometry(null, P(C("#3d8237"), 1.1), LeafVeins);
            }
        }

        void DrawHead(DrawingContext ctx, double now, double t, double headWilt)
        {
            var center = new Point(Plant.Cx, Plant.Cy);
            var ink = C(plant.Ink);
            var dull = C("#8e97b3");
            using (ctx.PushOpacity(1 - 0.45 * Clamp01(headWilt)))
            {
                // the green pulse when the profile opens
                double pulseAt = double.IsNegativeInfinity(changedAt) ? appear + T0 + 2100 : changedAt;
                double pulse = (now - pulseAt) / 1100;
                if (running && pulse > 0 && pulse < 1)
                {
                    double s = 0.3 + 1.4 * Out3(pulse);
                    ctx.DrawEllipse(null, P(C("#7fe0ad", 0.9 * (1 - pulse)), 2 / s), center, 70 * s, 70 * s);
                }

                string[] levelInk = { "#5aa9f0", "#4cc3a0", "#e9d98a", "#e8825f" };
                for (int k = 0; k < plant.Seeds.Count; k++)
                {
                    var seed = plant.Seeds[k];
                    double grow = Out3((t - 1000 - k * 7) / 700);
                    if (grow <= 0) continue;
                    double w = Wilt(now, k * 6);
                    double flutter = hover * 3 * Math.PI / 180 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * (now / 1500 + k * 0.073)));
                    double s = grow * (1 - 0.16 * w);
                    var m = About(center, Matrix.CreateScale(s, s) * Matrix.CreateRotation(seed.Droop * w + flutter));
                    var color = code ? C(levelInk[seed.Level]) : Mix(ink, dull, 0.7 * Clamp01(headWilt));
                    color = Color.FromArgb((byte)(color.A * grow), color.R, color.G, color.B);
                    using (ctx.PushTransform(m))
                    {
                        ctx.DrawGeometry(null, P(color, 1.4), seed.Ray);
                        ctx.DrawGeometry(seed.TipFilled ? Br(color) : null, P(color, 1.2), seed.Tip);
                    }
                }
                double core = OutBack((t - 900) / 400);
                if (core > 0) ctx.DrawEllipse(Br(C("#c4aa5a")), null, center, 12 * core, 12 * core);
            }
        }

        void DrawRings(DrawingContext ctx, double t, double now)
        {
            double fill = Out3(t / 1300);
            DrawRing(ctx, SessionRing, usage.Session, fill, "#f2c14e", now);
            DrawRing(ctx, WeekRing, usage.Week, fill, "#8fb4ff", now);
        }

        static void DrawRing(DrawingContext ctx, Poly ring, double percent, double fill, string color, double now)
        {
            ctx.DrawGeometry(null, new Pen(Br(C("#1a2140")), 5), ring.Partial(1));
            double frac = Clamp01(percent / 100) * fill;
            if (frac <= 0.001) return;
            bool hot = percent >= 90;
            double alpha = hot ? 0.7 + 0.3 * Math.Cos(2 * Math.PI * now / 1800) : 1;
            ctx.DrawGeometry(null, P(C(hot ? "#e8825f" : color, alpha), 5), ring.Partial(frac));
        }

        // A white dot runs down each root to its pad, then leaves as a packet of four bits
        // that drifts to its own spot below and fades.
        void DrawPackets(DrawingContext ctx, double now, double t)
        {
            double start = double.IsNegativeInfinity(changedAt) ? appear + T0 + 2300 : changedAt + 600;
            var white = C("#ffffff");
            for (int i = 0; i < plant.Packets.Count; i++)
            {
                var pk = plant.Packets[i];
                double local = now - start - pk.Offset * 1000;
                if (local < 0) continue;
                double ph = (local % PacketCycle) / PacketCycle;
                var root = plant.Roots[pk.Root];
                if (ph < 0.5)
                {
                    double q = ph / 0.5;
                    double a = q < 0.12 ? q / 0.12 : q < 0.92 ? 1 : 1 - (q - 0.92) / 0.08;
                    double s = q < 0.92 ? 1 : 1 + (q - 0.92) / 0.08 * 0.9;
                    var at = root.Line.PointAt(q);
                    ctx.DrawEllipse(Br(C("#ffffff", 0.25 * a)), null, at, 7 * s, 7 * s);
                    ctx.DrawEllipse(Br(Color.FromArgb((byte)(255 * a), white.R, white.G, white.B)), null, at, 4 * s, 4 * s);
                }
                else
                {
                    double q = (ph - 0.5) / 0.5;
                    double a = q < 0.1 ? q / 0.1 : q < 0.56 ? 1 : 1 - (q - 0.56) / 0.44;
                    double e = Out3(q), u = 1 - e;
                    var from = root.Pad;
                    var at = new Point(u * u * from.X + 2 * u * e * pk.Control.X + e * e * pk.Target.X,
                                       u * u * from.Y + 2 * u * e * pk.Control.Y + e * e * pk.Target.Y);
                    var text = packetText[i];
                    var box = new Rect(at.X - text.Width / 2 - 5, at.Y - text.Height / 2 - 1, text.Width + 10, text.Height + 2);
                    using (ctx.PushOpacity(a))
                    {
                        ctx.DrawRectangle(Br(C("#0f2a22")), new Pen(Br(C("#3c7d64")), 1), new RoundedRect(box, 4),
                            new BoxShadows(new BoxShadow { Blur = 10, Color = C("#7fe0ad", 0.3) }));
                        ctx.DrawText(text, new Point(box.X + 5, box.Y + 1));
                    }
                }
            }
        }
    }
}
