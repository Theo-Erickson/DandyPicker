using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DandyPicker
{
    // App settings, saved as settings.json next to the .exe.
    public class Settings
    {
        public bool usage { get; set; }      // off unless the user turns it on (and reads the warning)

        static readonly string File_ = Path.Combine(AppContext.BaseDirectory, "settings.json");

        public static Settings Load()
        {
            try { if (File.Exists(File_)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(File_)) ?? new Settings(); }
            catch { }
            return new Settings();
        }

        public void Save() { File.WriteAllText(File_, JsonSerializer.Serialize(this), new UTF8Encoding(false)); }
    }

    public class UsageInfo
    {
        public double Session, Week;          // percent used
        public DateTime? SessionReset, WeekReset;
        public string Error;                  // set when it couldn't be read
    }

    // Plan usage (the 5-hour session and the week) for a profile's claude.ai account.
    //
    // There's no public API for this. When the user turns usage on, DandyPicker:
    //  1. reads the profile's claude.ai sign-in cookie (sessionKey) from Claude's own cookie
    //     file in that profile's data folder, decrypting it with the key Windows keeps for
    //     the current user (the same thing Claude itself does);
    //  2. sends it to claude.ai only, to the same unofficial addresses the claude.ai usage
    //     page uses, and reads the percentages.
    // The cookie is kept in memory for that one request and never saved or sent anywhere else.
    static class Usage
    {
        static readonly HttpClient Http = new HttpClient(new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        public const string Warning =
            "Showing usage ties DandyPicker to each profile's claude.ai sign-in. When it's on, DandyPicker:\n\n" +
            "•  Reads each profile's claude.ai sign-in cookie from Claude's files in that profile's data folder, " +
            "decrypting it with your Windows account's key, as Claude itself does.\n" +
            "•  Sends it to claude.ai, and only claude.ai, every few minutes to read your session and weekly usage.\n" +
            "•  Keeps it in memory just for that request. It is never saved or sent anywhere else.\n\n" +
            "This uses unofficial claude.ai addresses, not a supported API, so it may stop working at any time, " +
            "and Anthropic may not expect apps to use them. Anyone who can already run programs as you could read these files too.\n\n" +
            "You can turn it off again at any time.";

        public static async Task<UsageInfo> FetchAsync(Profile p)
        {
            string key;
            try { key = await Task.Run(() => ReadSessionKey(p)); }
            catch (Exception e) { return new UsageInfo { Error = e.Message }; }
            if (key == null) return new UsageInfo { Error = "Not signed in to claude.ai in this profile yet." };

            try
            {
                var orgs = await GetJson("https://claude.ai/api/organizations", key);
                string org = null;
                foreach (var o in orgs.EnumerateArray())
                {
                    string id = o.TryGetProperty("uuid", out var u) ? u.GetString() : null;
                    bool chat = o.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array
                                && caps.EnumerateArray().Any(c => c.GetString() == "chat");
                    if (id != null && (chat || org == null)) { org = id; if (chat) break; }
                }
                if (org == null) return new UsageInfo { Error = "claude.ai didn't list an account for this sign-in." };

                var u2 = await GetJson("https://claude.ai/api/organizations/" + org + "/usage", key);
                var info = new UsageInfo();
                ReadWindow(u2, "five_hour", out info.Session, out info.SessionReset);
                ReadWindow(u2, "seven_day", out info.Week, out info.WeekReset);
                return info;
            }
            catch (Exception e) { return new UsageInfo { Error = e.Message }; }
        }

        static void ReadWindow(JsonElement root, string name, out double percent, out DateTime? reset)
        {
            percent = 0; reset = null;
            if (!root.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return;
            if (w.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number)
                percent = u.GetDouble();   // already a percentage
            if (w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String && DateTime.TryParse(r.GetString(), out var at))
                reset = at.ToLocalTime();
        }

        static async Task<JsonElement> GetJson(string url, string sessionKey)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Cookie", "sessionKey=" + sessionKey);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
            using (var res = await Http.SendAsync(req))
            {
                if (res.StatusCode == HttpStatusCode.Unauthorized || res.StatusCode == HttpStatusCode.Forbidden)
                    throw new Exception("claude.ai turned the request down (" + (int)res.StatusCode + "). The sign-in may have expired, or claude.ai is blocking apps.");
                if (!res.IsSuccessStatusCode) throw new Exception("claude.ai answered " + (int)res.StatusCode + ".");
                string body = await res.Content.ReadAsStringAsync();
                using (var doc = JsonDocument.Parse(body)) return doc.RootElement.Clone();
            }
        }

        // ---------- the profile's sign-in cookie ----------

        // Claude is a packaged app, so a profile's files may be in the package's private
        // copy of AppData rather than the folder itself. Try both; newest cookie file wins.
        static IEnumerable<string> Folders(Profile p)
        {
            string dir = Profiles.DataDirOf(p).TrimEnd('\\');
            yield return dir;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(local, @"Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming", Path.GetFileName(dir));
        }

        static string ReadSessionKey(Profile p)
        {
            string best = null, bestDir = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (var dir in Folders(p))
                foreach (var f in new[] { Path.Combine(dir, "Network", "Cookies"), Path.Combine(dir, "Cookies") })
                    if (File.Exists(f) && File.GetLastWriteTimeUtc(f) > bestTime) { best = f; bestDir = dir; bestTime = File.GetLastWriteTimeUtc(f); }
            if (best == null) return null;

            byte[] aes = ReadKey(Path.Combine(bestDir, "Local State"));
            SQLitePCL.Batteries_V2.Init();   // Windows' own winsqlite3.dll

            // Read a copy, so Claude's own file is never touched.
            string copy = Path.Combine(Path.GetTempPath(), "dandypicker-" + Guid.NewGuid().ToString("N"));
            try
            {
                try
                {
                    using (var src = new FileStream(best, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var dst = File.Create(copy)) src.CopyTo(dst);
                }
                catch (IOException)
                {
                    throw new Exception("Claude has its sign-in file locked while this profile is open. Close the profile once and DandyPicker can read it.");
                }

                using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
                {
                    db.Open();
                    int version = 0;
                    using (var cmd = db.CreateCommand())
                    {
                        cmd.CommandText = "SELECT value FROM meta WHERE key = 'version'";
                        try { int.TryParse(Convert.ToString(cmd.ExecuteScalar()), out version); } catch { }
                    }
                    using (var cmd = db.CreateCommand())
                    {
                        cmd.CommandText = "SELECT encrypted_value, value FROM cookies WHERE name = 'sessionKey' AND host_key LIKE '%claude.ai' ORDER BY last_access_utc DESC LIMIT 1";
                        using (var r = cmd.ExecuteReader())
                        {
                            if (!r.Read()) return null;
                            string plain = r.IsDBNull(1) ? "" : r.GetString(1);
                            if (plain.Length > 0) return plain;
                            return Decrypt((byte[])r[0], aes, version);
                        }
                    }
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { File.Delete(copy); } catch { }
            }
        }

        // The cookie key, kept in "Local State" and protected by Windows for this user.
        static byte[] ReadKey(string localState)
        {
            if (!File.Exists(localState)) throw new Exception("Couldn't find this profile's Local State file.");
            string text;
            using (var s = new FileStream(localState, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(s)) text = r.ReadToEnd();
            using (var doc = JsonDocument.Parse(text))
            {
                string b64 = doc.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString();
                byte[] blob = Convert.FromBase64String(b64);
                if (blob.Length < 5 || Encoding.ASCII.GetString(blob, 0, 5) != "DPAPI") throw new Exception("This profile's cookie key is in a format DandyPicker doesn't know.");
                return ProtectedData.Unprotect(blob.Skip(5).ToArray(), null, DataProtectionScope.CurrentUser);
            }
        }

        static string Decrypt(byte[] value, byte[] key, int dbVersion)
        {
            if (value == null || value.Length < 3 + 12 + 16) return null;
            string prefix = Encoding.ASCII.GetString(value, 0, 3);
            if (prefix != "v10" && prefix != "v11") throw new Exception("This profile's sign-in is encrypted in a way DandyPicker doesn't know (" + prefix + ").");
            byte[] nonce = value.Skip(3).Take(12).ToArray();
            byte[] cipher = value.Skip(15).Take(value.Length - 15 - 16).ToArray();
            byte[] tag = value.Skip(value.Length - 16).ToArray();
            byte[] plain = new byte[cipher.Length];
            using (var gcm = new AesGcm(key, 16)) gcm.Decrypt(nonce, cipher, tag, plain);
            // Newer cookie files put a 32-byte hash of the site in front of the value.
            int skip = dbVersion >= 24 && plain.Length > 32 ? 32 : 0;
            return Encoding.UTF8.GetString(plain, skip, plain.Length - skip);
        }
    }
}
