using System;
using System.Text;

namespace DandyPicker
{
    // The dandelions. Each profile's flower is grown from its name and folder:
    //  - The seeds spell them out. Starting just clockwise of the stem, each seed is
    //    one of four lengths (2 bits), so four seeds make one byte of
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
    }
}
