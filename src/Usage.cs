using System;
using System.Collections.Generic;
using System.Globalization;
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
        public DateTime ReadAt = DateTime.Now;
        public string StaleReason;            // set when these are the last good numbers, not new ones
    }

    // Plan usage (the 5-hour session and the week) for a profile's claude.ai account.
    //
    // There's no public API for this. When the user turns usage on, DandyPicker:
    //  1. reads the sign-in token Claude Desktop keeps in the profile's config.json
    //     ("oauth:tokenCacheV2"), decrypting it with the profile's key, which Windows keeps for
    //     the current user (the same thing Claude itself does). config.json isn't locked, so
    //     this works while the profile is open, when Claude keeps the token fresh;
    //  2. sends it to api.anthropic.com only, to the usage address Claude Code's /usage uses.
    // It never refreshes the token (that would replace the profile's own sign-in). If there's no
    // usable token, as after a profile has been closed a while, it falls back to the claude.ai
    // sign-in cookie, which is unlocked exactly when the profile is closed, and the unofficial
    // address the claude.ai usage page uses.
    // Tokens and cookies are kept in memory for that one request and never saved or sent anywhere else.
    static class Usage
    {
        static readonly HttpClient Http = new HttpClient(new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        public const string Warning =
            "Showing usage ties DandyPicker to each profile's claude.ai sign-in. When it's on, DandyPicker:\n\n" +
            "•  Reads each profile's sign-in token from Claude's files in that profile's data folder, " +
            "decrypting it with your Windows account's key, as Claude itself does. If that token has expired, " +
            "it reads the profile's claude.ai sign-in cookie instead.\n" +
            "•  Sends the token to api.anthropic.com, or the cookie to claude.ai, and nowhere else, every few minutes " +
            "to read your session and weekly usage. It never renews the token, so it can't sign the profile out.\n" +
            "•  Keeps them in memory just for that request. They are never saved or sent anywhere else.\n\n" +
            "These are unofficial addresses, not a supported API, so they may stop working at any time, " +
            "and Anthropic may not expect apps to use them. Anyone who can already run programs as you could read these files too.\n\n" +
            "You can turn it off again at any time.";

        public static async Task<UsageInfo> FetchAsync(Profile p)
        {
            // 1. Claude Desktop's own sign-in token, via api.anthropic.com.
            string tokenWhy = null;
            List<string> tokens = null;
            try { tokens = await Task.Run(() => ReadDesktopTokens(p)); }
            catch (Exception e) { tokenWhy = e.Message; }
            if (tokens != null)
                foreach (var t in tokens)
                {
                    try { return await FromToken(t); }
                    catch (Exception e) { tokenWhy = e.Message; }
                }

            // 2. The claude.ai cookie (readable while the profile is closed).
            var info = await FromCookie(p);
            if (info.Error != null && tokenWhy != null) info.Error = tokenWhy + "\n" + info.Error;
            return info;
        }

        static async Task<UsageInfo> FromToken(string token)
        {
            var root = await GetJson("https://api.anthropic.com/api/oauth/usage", req =>
            {
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                req.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
                req.Headers.TryAddWithoutValidation("User-Agent", "DandyPicker");
            }, "api.anthropic.com");
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("five_hour", out _))
                throw new Exception("api.anthropic.com answered, but not with usage.");
            var info = new UsageInfo();
            ReadWindow(root, "five_hour", out info.Session, out info.SessionReset);
            ReadWindow(root, "seven_day", out info.Week, out info.WeekReset);
            return info;
        }

        static async Task<UsageInfo> FromCookie(Profile p)
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

        static Task<JsonElement> GetJson(string url, string sessionKey)
        {
            return GetJson(url, req =>
            {
                req.Headers.TryAddWithoutValidation("Cookie", "sessionKey=" + sessionKey);
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
            }, "claude.ai");
        }

        static async Task<JsonElement> GetJson(string url, Action<HttpRequestMessage> auth, string site)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            auth(req);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            using (var res = await Http.SendAsync(req))
            {
                if (res.StatusCode == HttpStatusCode.Unauthorized || res.StatusCode == HttpStatusCode.Forbidden)
                    throw new Exception(site + " turned the request down (" + (int)res.StatusCode + "). The sign-in may have expired, or " + site + " is blocking apps.");
                if ((int)res.StatusCode == 429) throw new Exception(site + " asked DandyPicker to slow down (429).");
                if (!res.IsSuccessStatusCode) throw new Exception(site + " answered " + (int)res.StatusCode + ".");
                string body = await res.Content.ReadAsStringAsync();
                using (var doc = JsonDocument.Parse(body)) return doc.RootElement.Clone();
            }
        }

        // ---------- Claude Desktop's sign-in token ----------

        // Access tokens from the profile's config.json, freshest first, leaving out expired ones.
        // Returns null when the profile has none. The layout inside the token cache isn't
        // documented, so this looks for access tokens (sk-ant-oat...) anywhere in it, each with
        // the expiry time stored beside it, if any. Refresh tokens (sk-ant-ort...) are never used.
        static List<string> ReadDesktopTokens(Profile p)
        {
            string cfg = null, dir = null;
            DateTime newest = DateTime.MinValue;
            foreach (var d in Folders(p))
            {
                string f = Path.Combine(d, "config.json");
                if (File.Exists(f) && File.GetLastWriteTimeUtc(f) > newest) { cfg = f; dir = d; newest = File.GetLastWriteTimeUtc(f); }
            }
            if (cfg == null) return null;

            string text;
            using (var s = new FileStream(cfg, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(s)) text = r.ReadToEnd();

            var found = new List<KeyValuePair<string, DateTime?>>();
            byte[] aes = null;
            using (var doc = JsonDocument.Parse(text))
                foreach (var name in new[] { "oauth:tokenCacheV2", "oauth:tokenCache" })
                {
                    if (!doc.RootElement.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) continue;
                    string plain;
                    try
                    {
                        if (aes == null) aes = ReadKey(Path.Combine(dir, "Local State"));
                        plain = Decrypt(Convert.FromBase64String(v.GetString()), aes, 0);
                    }
                    catch (Exception) { continue; }
                    if (plain == null) continue;
                    if (plain.StartsWith("sk-ant-oat")) { found.Add(new KeyValuePair<string, DateTime?>(plain.Trim(), null)); continue; }
                    try { using (var inner = JsonDocument.Parse(plain)) CollectTokens(inner.RootElement, found, 0); }
                    catch (JsonException) { }
                }
            if (found.Count == 0) return null;

            var now = DateTime.UtcNow.AddMinutes(1);
            var usable = found.Where(t => t.Value == null || t.Value > now)
                              .OrderByDescending(t => t.Value ?? DateTime.MaxValue)
                              .Select(t => t.Key).Distinct().Take(3).ToList();
            if (usable.Count == 0) throw new Exception("This profile's desktop sign-in token has expired. Open the profile and Claude renews it.");
            return usable;
        }

        static void CollectTokens(JsonElement e, List<KeyValuePair<string, DateTime?>> found, int depth)
        {
            if (depth > 8) return;
            if (e.ValueKind == JsonValueKind.Array) { foreach (var x in e.EnumerateArray()) CollectTokens(x, found, depth + 1); return; }
            if (e.ValueKind == JsonValueKind.String)
            {
                // Some caches keep each entry as a JSON string inside the JSON.
                string s = e.GetString();
                if (s != null && s.StartsWith("{"))
                    try { using (var d = JsonDocument.Parse(s)) CollectTokens(d.RootElement, found, depth + 1); } catch (JsonException) { }
                return;
            }
            if (e.ValueKind != JsonValueKind.Object) return;

            DateTime? expires = null;
            var tokens = new List<string>();
            foreach (var prop in e.EnumerateObject())
            {
                var v = prop.Value;
                if (v.ValueKind == JsonValueKind.String && v.GetString().StartsWith("sk-ant-oat")) tokens.Add(v.GetString());
                else if (prop.Name.IndexOf("expir", StringComparison.OrdinalIgnoreCase) >= 0) expires = ReadTime(v);
                else CollectTokens(v, found, depth + 1);
            }
            foreach (var t in tokens) found.Add(new KeyValuePair<string, DateTime?>(t, expires));
        }

        // An expiry time as Unix seconds, Unix milliseconds or an ISO date; UTC.
        static DateTime? ReadTime(JsonElement v)
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double n) && n > 0)
            {
                try { return n > 1e11 ? DateTimeOffset.FromUnixTimeMilliseconds((long)n).UtcDateTime : DateTimeOffset.FromUnixTimeSeconds((long)n).UtcDateTime; }
                catch (ArgumentOutOfRangeException) { return null; }
            }
            if (v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                return at.UtcDateTime;
            return null;
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
