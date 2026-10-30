using System;
using System.Collections.Generic;
using System.Text;

namespace DandyPicker
{
    // Everything about one profile's plant that doesn't move: the seeds (which spell its
    // name and folder), stem, leaves (one per place in the list), soil and circuit-trace
    // roots. Laid out in a 380 x 700 space; PlantView draws and animates it.
    //
    // The roots, soil, leaves and packet targets come from a random-number generator seeded
    // with the profile's hash, so a profile always grows the same plant (and the same one
    // the design lab draws).
    class Plant
    {
        public const double Cx = 190, Cy = 180, R0 = 16, SoilY = 440;
        static readonly double[] Lens = { 66, 90, 114, 138 };

        public class Seed { public Geometry Ray, Tip; public bool TipFilled; public int Level; public double Droop; }
        public class Leaf { public Point At; public double Tilt, Size; public bool Right; }
        public class Rock { public Point At; public double Rx, Ry; public Color Fill; }
        public class Root { public Poly Line; public Point Pad; }
        public class Packet { public int Root; public Point Target, Control; public string Bits; public double Offset; }

        public readonly string Payload, Ink;   // Ink: the seed color
        public readonly uint Hash;
        public readonly List<Seed> Seeds = new List<Seed>();
        public readonly Poly Stem;
        public readonly List<Leaf> Leaves = new List<Leaf>();
        public Geometry SoilTop, SoilBand, SoilRim, Grass;
        public readonly List<Tuple<Point, double, Color>> Specks = new List<Tuple<Point, double, Color>>();
        public readonly List<Rock> Rocks = new List<Rock>();
        public readonly List<Root> Roots = new List<Root>();
        public readonly List<Packet> Packets = new List<Packet>();

        public Plant(Profile p, int index)
            : this(Dandelion.PayloadOf(p), Dandelion.ColorOf(p), p.wave ?? Dandelion.DefaultWave, p.bend ?? Dandelion.DefaultBend, p.tips ?? Dandelion.DefaultTips, index) { }

        public Plant(string payload, string color, double wave, double bend, string tips, int index)
        {
            Payload = payload;
            Ink = color;
            Hash = Dandelion.Hash(payload);
            var rnd = new Rng(Hash ^ 0x9e3779b9);
            int[] levels = Dandelion.Levels(payload);

            // ---- seeds: they start just clockwise of the stem and go round ----
            const double gap = 0.5;
            int count = levels.Length;
            for (int k = 0; k < count; k++)
            {
                double a = Math.PI / 2 + gap / 2 + (2 * Math.PI - gap) * k / Math.Max(1, count - 1);
                double len = Lens[levels[k]];
                double ux = Math.Cos(a), uy = Math.Sin(a), px = -uy, py = ux;
                double amp = wave * 18 * (k % 2 == 1 ? 1 : -1) * (0.6 + 0.4 * Math.Sin(k * 1.3 + Hash % 7));
                var p0 = new Point(Cx + ux * R0, Cy + uy * R0);
                var p1 = new Point(Cx + ux * len, Cy + uy * len);
                var m = new Point(Cx + ux * (R0 + len) / 2 + px * amp, Cy + uy * (R0 + len) / 2 + py * amp);
                var ray = new StreamGeometry();
                using (var c = ray.Open()) { c.BeginFigure(p0, false); c.QuadraticBezierTo(m, p1); c.EndFigure(false); }
                double ta = Math.Atan2(p1.Y - m.Y, p1.X - m.X);
                var tip = new StreamGeometry();
                using (var c = tip.Open())
                {
                    if (tips == "puff")
                    {
                        c.BeginFigure(new Point(p1.X - 4, p1.Y), true);
                        c.ArcTo(new Point(p1.X + 4, p1.Y), new Size(4, 4), 0, false, SweepDirection.Clockwise);
                        c.ArcTo(new Point(p1.X - 4, p1.Y), new Size(4, 4), 0, false, SweepDirection.Clockwise);
                        c.EndFigure(true);
                    }
                    else
                    {
                        int from = tips == "star" ? 0 : -2, to = tips == "star" ? 5 : 2;
                        double step = tips == "star" ? Math.PI / 3 : 0.42, reach = tips == "star" ? 6.5 : 11;
                        for (int j = from; j <= to; j++)
                        {
                            double fa = ta + j * step;
                            c.BeginFigure(p1, false);
                            c.LineTo(new Point(p1.X + Math.Cos(fa) * reach, p1.Y + Math.Sin(fa) * reach));
                            c.EndFigure(false);
                        }
                    }
                }
                // wilting turns each seed toward the ground (straight-up ones pick a side)
                double toDown = Math.PI / 2 - a;
                while (toDown > Math.PI) toDown -= 2 * Math.PI;
                while (toDown <= -Math.PI) toDown += 2 * Math.PI;
                if (Math.Abs(Math.Abs(toDown) - Math.PI) < 0.35) toDown = (k % 2 == 1 ? 1 : -1) * Math.PI;
                double droop = Math.Max(-34, Math.Min(34, toDown * 180 / Math.PI * 0.28)) * Math.PI / 180;
                Seeds.Add(new Seed { Ray = ray, Tip = tip, TipFilled = tips == "puff", Level = levels[k], Droop = droop });
            }

            // ---- stem, drawn from the soil up ----
            double b = (bend - 0.5) * 2;
            var P0 = new Point(Cx + b * 14, SoilY); var P1 = new Point(Cx - b * 40, 360);
            var P2 = new Point(Cx + b * 30, 280); var P3 = new Point(Cx, 192);
            Func<double, Point> onStem = t =>
            {
                double u = 1 - t;
                return new Point(u * u * u * P0.X + 3 * u * u * t * P1.X + 3 * u * t * t * P2.X + t * t * t * P3.X,
                                 u * u * u * P0.Y + 3 * u * u * t * P1.Y + 3 * u * t * t * P2.Y + t * t * t * P3.Y);
            };
            var stemPts = new List<Point>();
            for (int i = 0; i <= 40; i++) stemPts.Add(onStem(i / 40.0));
            Stem = new Poly(stemPts);

            // ---- leaves: one per place in the list ----
            int n = index + 1;
            for (int l = 0; l < n; l++)
            {
                bool right = l % 2 == 0;
                double size = 0.78 + rnd.Next() * 0.5;
                double tilt = (right ? -30 : 210) + (rnd.Next() - 0.5) * 16;
                Leaves.Add(new Leaf { At = onStem(0.14 + 0.46 * (l + 0.5) / n), Tilt = tilt, Size = size, Right = right });
            }

            // ---- soil: a bumpy mound with pebbles and a little grass ----
            double[] xs = { 54, 100, 146, 190, 234, 280, 326 };
            double[] lift = { 0, 10, 16, 18, 16, 10, 0 };
            var top = new Point[xs.Length];
            for (int i = 0; i < xs.Length; i++) top[i] = new Point(xs[i], SoilY + 10 - lift[i] - (i > 0 && i < 6 ? rnd.Next() * 8 : 0));
            Func<double, double> surface = x =>
            {
                for (int i = 0; i < top.Length - 1; i++)
                    if (x <= top[i + 1].X) { double q = (x - top[i].X) / (top[i + 1].X - top[i].X); return top[i].Y + q * (top[i + 1].Y - top[i].Y); }
                return top[top.Length - 1].Y;
            };
            SoilRim = Smooth(top, false);
            SoilTop = Smooth(top, true);
            double b1 = 462 + rnd.Next() * 6, b2 = 462 + rnd.Next() * 6, b3 = 462 + rnd.Next() * 6;
            SoilBand = Geometry.Parse(Fmt("M58 458 Q120 {0} 190 {1} Q262 {2} 322 458 Q314 476 190 480 Q66 476 58 458Z", b1, b2, b3));
            for (int i = 0; i < 10; i++)
            {
                double sx = 74 + rnd.Next() * 232;
                double sy = surface(sx) + 6 + rnd.Next() * 22, sr = 1.2 + rnd.Next() * 1.6;
                Specks.Add(Tuple.Create(new Point(sx, sy), sr, Avalonia.Media.Color.Parse(rnd.Next() < 0.5 ? "#5e4836" : "#4a382a")));
            }
            string[] rockColors = { "#6f675e", "#7d7468", "#5c564f", "#8a7f70" };
            int rc = 3 + (int)Math.Floor(rnd.Next() * 3);
            for (int i = 0; i < rc; i++)
            {
                double rx = 4 + rnd.Next() * 7, x = 76 + rnd.Next() * 228;
                if (Math.Abs(x - 190) < 20) x += 40;
                double y = surface(x) + 3 + rnd.Next() * 14;
                double ry = rx * (0.55 + rnd.Next() * 0.2);
                Rocks.Add(new Rock { At = new Point(x, y), Rx = rx, Ry = ry, Fill = Avalonia.Media.Color.Parse(rockColors[(int)Math.Floor(rnd.Next() * rockColors.Length)]) });
            }
            var grass = new StringBuilder();
            for (int i = 0; i < 3; i++)
            {
                double gx = 80 + rnd.Next() * 220; if (Math.Abs(gx - 190) < 24) gx += 50;
                double gy = surface(gx) + 1;
                grass.Append(Fmt("M{0} {1} q-2 -7 -6 -11 M{0} {1} q1 -9 3 -13 M{2} {1} q3 -6 7 -8 ", gx, gy, gx + 3));
            }
            Grass = Geometry.Parse(grass.ToString());

            // ---- roots: on each side a nested fan, so traces never cross or double back ----
            const double top0 = 474;
            foreach (int dir in new[] { -1, 1 })
            {
                int sideCount = 1 + (int)Math.Floor(rnd.Next() * 3);
                for (int i = 0; i < sideCount; i++)           // i = 0 is the outermost
                {
                    double x0 = Cx + dir * (16 + (sideCount - 1 - i) * 18);
                    double y1 = top0 + 10 + i * 18 + rnd.Next() * 6;
                    double x1 = x0 + dir * 16, y2 = y1 + 16;
                    double reach = 38 + i * 34 + rnd.Next() * 14;
                    double x2 = dir < 0 ? Math.Min(30 + reach, x1 - 16) : Math.Max(350 - reach, x1 + 16);
                    double y3 = Math.Min(572, y2 + 18 + rnd.Next() * 52);
                    AddRoot(new Point(x0, top0), new Point(x0, y1), new Point(x1, y2), new Point(x2, y2), new Point(x2, y3));
                    if (i == sideCount - 1 && rnd.Next() < 0.55 && Math.Abs(x1 - x2) > 40)
                    {
                        double xb = x2 + (x1 - x2) * (0.45 + rnd.Next() * 0.2), yb = Math.Min(572, y2 + 16 + rnd.Next() * 26);
                        AddRoot(new Point(xb, y2), new Point(xb, yb));
                    }
                }
            }
            if (rnd.Next() < 0.5) AddRoot(new Point(Cx, top0), new Point(Cx, 520 + rnd.Next() * 48));

            // ---- packets: four of the profile's own bits each, bound for their own spot below ----
            var bits = new StringBuilder();
            foreach (byte x in Encoding.UTF8.GetBytes(payload)) bits.Append(Convert.ToString(x, 2).PadLeft(8, '0'));
            string all = bits.ToString();
            int nib = 0;
            for (int ri = 0; ri < Roots.Count; ri++)
            {
                var pad = Roots[ri].Pad;
                for (int q = 0; q < 2; q++)
                {
                    double tx = Clamp(pad.X + (rnd.Next() - 0.5) * 120 + (pad.X - Cx) * 0.25, 20, 360);
                    double ty = Clamp(pad.Y + 50 + rnd.Next() * 70, pad.Y + 40, 690);
                    int at = (nib * 4) % Math.Max(4, all.Length - 4);
                    Packets.Add(new Packet
                    {
                        Root = ri, Target = new Point(tx, ty),
                        Control = new Point(pad.X + (tx - pad.X) * 0.15, pad.Y + (ty - pad.Y) * 0.7),
                        Bits = all.Length >= at + 4 ? all.Substring(at, 4) : "0101",
                        Offset = q * 1.6 + ri * 0.35,
                    });
                    nib++;
                }
            }
        }

        void AddRoot(params Point[] pts) { Roots.Add(new Root { Line = new Poly(new List<Point>(pts)), Pad = pts[pts.Length - 1] }); }

        static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }

        static string Fmt(string f, params object[] args)
        {
            var a = new object[args.Length];
            for (int i = 0; i < args.Length; i++) a[i] = ((double)args[i]).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            return string.Format(f, a);
        }

        // The mound's top edge, smoothed through the points; closed = the whole mound.
        static Geometry Smooth(Point[] pts, bool closed)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(pts[0], closed);
                for (int i = 1; i < pts.Length - 1; i++)
                    c.QuadraticBezierTo(pts[i], new Point((pts[i].X + pts[i + 1].X) / 2, (pts[i].Y + pts[i + 1].Y) / 2));
                c.LineTo(pts[pts.Length - 1]);
                if (closed)
                {
                    c.QuadraticBezierTo(new Point(318, 476), new Point(190, 480));
                    c.QuadraticBezierTo(new Point(62, 476), pts[0]);
                }
                c.EndFigure(closed);
            }
            return g;
        }

        // The design lab's generator (mulberry32), so both grow the same plant.
        class Rng
        {
            uint a;
            public Rng(uint seed) { a = seed; }
            public double Next()
            {
                unchecked
                {
                    a += 0x6D2B79F5;
                    uint t = a;
                    t = (t ^ (t >> 15)) * (t | 1);
                    t ^= t + (t ^ (t >> 7)) * (t | 61);
                    return (t ^ (t >> 14)) / 4294967296.0;
                }
            }
        }
    }

    // A polyline that can be drawn part of the way along, or asked where a point that far along is.
    class Poly
    {
        readonly List<Point> pts;
        readonly double[] at;
        public readonly double Length;

        public Poly(List<Point> points)
        {
            pts = points;
            at = new double[pts.Count];
            for (int i = 1; i < pts.Count; i++) at[i] = at[i - 1] + Dist(pts[i - 1], pts[i]);
            Length = at[at.Length - 1];
        }

        static double Dist(Point a, Point b) { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }

        public Point PointAt(double frac)
        {
            double d = Math.Max(0, Math.Min(1, frac)) * Length;
            for (int i = 1; i < pts.Count; i++)
                if (d <= at[i])
                {
                    double seg = at[i] - at[i - 1], q = seg <= 0 ? 0 : (d - at[i - 1]) / seg;
                    return new Point(pts[i - 1].X + (pts[i].X - pts[i - 1].X) * q, pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * q);
                }
            return pts[pts.Count - 1];
        }

        public Geometry Partial(double frac)
        {
            var g = new StreamGeometry();
            double d = Math.Max(0, Math.Min(1, frac)) * Length;
            using (var c = g.Open())
            {
                c.BeginFigure(pts[0], false);
                for (int i = 1; i < pts.Count; i++)
                {
                    if (d >= at[i]) { c.LineTo(pts[i]); continue; }
                    c.LineTo(PointAt(frac));
                    break;
                }
                c.EndFigure(false);
            }
            return g;
        }

        // A rounded rectangle's outline as points, starting at the top edge after the
        // top-left corner and going clockwise (as SVG draws one).
        public static Poly RoundedRect(double x, double y, double w, double h, double r)
        {
            var p = new List<Point>();
            Action<double, double, double> arc = (cx, cy, start) =>
            {
                for (int i = 1; i <= 12; i++) { double a = start + Math.PI / 2 * i / 12; p.Add(new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r)); }
            };
            p.Add(new Point(x + r, y));
            p.Add(new Point(x + w - r, y)); arc(x + w - r, y + r, -Math.PI / 2);
            p.Add(new Point(x + w, y + h - r)); arc(x + w - r, y + h - r, 0);
            p.Add(new Point(x + r, y + h)); arc(x + r, y + h - r, Math.PI / 2);
            p.Add(new Point(x, y + r)); arc(x + r, y + r, Math.PI);
            return new Poly(p);
        }
    }
}
