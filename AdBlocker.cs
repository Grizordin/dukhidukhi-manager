using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

static class AdBlocker {
    public const string Id = "ejhlhnliiahoeeacfgcoppoddjjnmlkm";
    public const string VideoRulesRevision = "3";
    const string Key = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAutfPFT1uytGj2iYf8+FNcn5G7XPXhBeRRjSGcJFrBMXCQAYU8KK57zzKezSAB79hgEJjcajgBJBSokFRfmqB2YtLCbzppSRgRXQ7psZc0J/ZQEjy5cxSrHiDYBWRci9IeNzive8mzXiUcyzZBe1t7B0ao88Yu8vdws1L4YKUpvifueVs0CfjuYAi2JRKxTF3ejTmQiiVteFFiELPbl1ETE9Fyi5NGOLuEfW+JUgPYpafZeu+ed9q7SHJpQAf8YY+PR3CzCHSjikiiLN6mUB8MRtQz4iOOSz1hdtKiSQ5xHRjED/0GNHcTrfJw24/noUWL4ThcK9MIngSvpqc7Rp4YwIDAQAB";
    const string Releases = "https://api.github.com/repos/uBlockOrigin/uBOL-home/releases?per_page=20";
    const string DownloadRoot = "https://github.com/uBlockOrigin/uBOL-home/releases/download/";
    const string VersionPattern = @"^20[0-9]{2}\.[0-9]{1,4}\.[0-9]{1,4}$";
    static readonly Encoding Utf8 = new UTF8Encoding(false);

    public sealed class Filters {
        public string Version;
        public byte[] Rules;
        public byte[] License;
    }

    static JavaScriptSerializer Json() {
        return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
    }

    public static Filters Bundled(string root) {
        string source = Path.Combine(root, "blocker-filters");
        return new Filters {
            Version = File.ReadAllText(Path.Combine(source, "version.txt")).Trim(),
            Rules = File.ReadAllBytes(Path.Combine(source, "easylist.json")),
            License = File.ReadAllBytes(Path.Combine(source, "LICENSE.txt")),
        };
    }

    static string Field(Dictionary<string, object> record, string name) {
        object value;
        return record.TryGetValue(name, out value) ? value as string : null;
    }

    internal static async Task<byte[]> Download(HttpClient client, string address, int limit, CancellationToken cancellation) {
        for (int hop = 0; hop < 5; hop++) {
            var target = new Uri(address);
            if (target.Scheme != "https" || !target.IsDefaultPort || !String.IsNullOrEmpty(target.UserInfo) ||
                !new[] { "api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" }.Contains(target.Host))
                throw new IOException("GitHub перенаправил загрузку на неожиданный адрес.");
            using (var response = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, cancellation)) {
                int status = (int)response.StatusCode;
                if (status >= 300 && status < 400 && response.Headers.Location != null) {
                    address = new Uri(target, response.Headers.Location).AbsoluteUri;
                    continue;
                }
                response.EnsureSuccessStatusCode();
                return await ReadDownload(response, limit, cancellation);
            }
        }
        throw new IOException("Слишком много перенаправлений при скачивании с GitHub.");
    }

    internal static async Task<byte[]> ReadDownload(HttpResponseMessage response, int limit, CancellationToken cancellation) {
        if (response.Content.Headers.ContentLength > limit) throw new IOException("Загружаемый файл слишком большой.");
        using (var stream = await response.Content.ReadAsStreamAsync())
        using (var output = new MemoryStream()) {
            var buffer = new byte[32768];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation)) != 0) {
                if (output.Length + read > limit) throw new IOException("Загружаемый файл слишком большой.");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
    }

    public static async Task<Filters> Latest() {
        const int Limit = 16 * 1024 * 1024;
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
        using (var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        using (var client = new HttpClient(handler) { MaxResponseContentBufferSize = Limit, Timeout = TimeSpan.FromSeconds(30) }) {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DukhiDukhi-Manager/1.0");
            byte[] feed = await Download(client, Releases, 2 * 1024 * 1024, timeout.Token);
            var releases = Json().DeserializeObject(Utf8.GetString(feed)) as IEnumerable;
            if (releases == null) throw new IOException("GitHub не вернул список релизов.");
            foreach (var release in releases.OfType<Dictionary<string, object>>()) {
                string version = Field(release, "tag_name") ?? "";
                if (!Regex.IsMatch(version, VersionPattern) || Equals(release["draft"], true) || Equals(release["prerelease"], true)) continue;
                string name = "uBOLite_" + version + ".chromium.zip";
                string url = DownloadRoot + version + "/" + name;
                var assets = release["assets"] as IEnumerable;
                if (assets == null) continue;
                var asset = assets.OfType<Dictionary<string, object>>().FirstOrDefault(x => Field(x, "name") == name);
                if (asset == null) continue;
                string digest = Field(asset, "digest") ?? "";
                if (Field(asset, "browser_download_url") != url || !Regex.IsMatch(digest, @"^sha256:[0-9a-f]{64}$"))
                    throw new IOException("GitHub не предоставил контрольную сумму пакета фильтров.");
                byte[] archive = await Download(client, url, Limit, timeout.Token);
                if ("sha256:" + ExtensionPackages.Hash(archive) != digest)
                    throw new IOException("Контрольная сумма фильтров не совпала.");
                using (var stream = new MemoryStream(archive))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read)) {
                    var metadata = Json().Deserialize<Dictionary<string, object>>(Utf8.GetString(ReadEntry(zip, "manifest.json", 1024 * 1024)));
                    if (Field(metadata, "version") != version) throw new IOException("Версия пакета не совпала с релизом.");
                    return new Filters {
                        Version = version,
                        Rules = ReadEntry(zip, "rulesets/main/easylist.json", 6 * 1024 * 1024),
                        License = ReadEntry(zip, "LICENSE.txt", 128 * 1024),
                    };
                }
            }
        }
        throw new IOException("В свежих релизах нет пакета фильтров для Chromium.");
    }

    static byte[] ReadEntry(ZipArchive zip, string path, int limit) {
        var entry = zip.GetEntry(path);
        if (entry == null || entry.Length > limit) throw new IOException("В пакете нет допустимого файла " + path + ".");
        using (var source = entry.Open())
        using (var output = new MemoryStream()) {
            var buffer = new byte[32768];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) != 0) {
                if (output.Length + read > limit) throw new IOException("Файл фильтров превысил допустимый размер.");
                output.Write(buffer, 0, read);
            }
            if (output.Length != entry.Length) throw new IOException("Пакет фильтров повреждён.");
            return output.ToArray();
        }
    }

    public static string Prepare(Filters filters, string storage) {
        if (!Regex.IsMatch(filters.Version, VersionPattern)) throw new IOException("Неверная версия фильтров.");
        var rules = FilterRules(filters.Rules);
        var preRoll = new object[] {
            VideoAdRule(1, "||g.doubleclick.net/gampad/ads"),
            VideoAdRule(2, "||pubads.g.doubleclick.net/gampad/ads"),
            VideoAdRule(3, "||securepubads.g.doubleclick.net/gampad/ads"),
            VideoAdRule(4, "||pagead2.googlesyndication.com/gampad/ads"),
            VideoAdRule(5, "||an.yandex.ru/vast"),
            VideoAdRule(6, "||vast*.videocdn.tv^"),
            KodikVastRule(),
            VideoHubAdRule(8, @"^https?://(yandex\.(ru|com|by|kz)|ya\.ru)\.?(:[0-9]+)?/(ads/)?adfox/[0-9]+/getCode([?]|$)"),
            VideoHubAdRule(9, @"^https?://exchange\.buzzoola\.com\.?(:[0-9]+)?/ad/[0-9]+([?]|$)"),
        };
        var manifest = new {
            manifest_version = 3,
            name = "Духи Духи · Блокировщик рекламы",
            description = "EasyList и правила для рекламных запросов в видеоплеерах.",
            version = filters.Version,
            key = Key,
            permissions = new[] { "declarativeNetRequest" },
            declarative_net_request = new {
                rule_resources = new[] {
                    new { id = "easylist", enabled = true, path = "easylist.json" },
                    new { id = "video_ads", enabled = true, path = "video-ads.json" },
                },
            },
        };
        var files = new Dictionary<string, byte[]> {
            { "manifest.json", Utf8.GetBytes(Json().Serialize(manifest)) },
            { "easylist.json", Utf8.GetBytes(Json().Serialize(rules)) },
            { "video-ads.json", Utf8.GetBytes(Json().Serialize(preRoll)) },
            { "LICENSE.txt", filters.License },
            { "SOURCE.txt", Utf8.GetBytes("EasyList by The EasyList authors (https://easylist.to/).\r\n" +
                "Chromium rules compiled by the uBlock Origin Lite project, release " + filters.Version +
                "\r\n" + DownloadRoot + filters.Version + "/uBOLite_" + filters.Version + ".chromium.zip" +
                "\r\nOnly block and allow rules are kept. Local video rules are in video-ads.json.\r\n") },
        };
        string fingerprints = String.Join("", files.OrderBy(pair => pair.Key).Select(pair => ExtensionPackages.Hash(pair.Value)));
        string folder = Path.Combine(storage, filters.Version + "-" +
            ExtensionPackages.Hash(Utf8.GetBytes(fingerprints)).Substring(0, 20));
        Directory.CreateDirectory(storage);
        if (Directory.Exists(folder)) {
            foreach (var file in files) {
                string expected = Path.Combine(folder, file.Key);
                if (!File.Exists(expected) || !File.ReadAllBytes(expected).SequenceEqual(file.Value))
                    throw new IOException("Файлы этой версии блокировщика были изменены.");
            }
            if (Directory.EnumerateFileSystemEntries(folder).Count() != files.Count)
                throw new IOException("В папке блокировщика обнаружены посторонние файлы.");
            return folder;
        }

        string stage = Path.Combine(storage, "staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try {
            foreach (var file in files) File.WriteAllBytes(Path.Combine(stage, file.Key), file.Value);
            Directory.Move(stage, folder);
        } finally {
            if (Directory.Exists(stage)) {
                foreach (string name in files.Keys) File.Delete(Path.Combine(stage, name));
                Directory.Delete(stage);
            }
        }
        return folder;
    }

    public static object[] FilterRules(byte[] bytes) {
        var source = Json().DeserializeObject(Utf8.GetString(bytes)) as IEnumerable;
        if (source == null) throw new IOException("EasyList не содержит списка правил.");
        string[] actions = { "block", "allow", "allowAllRequests" };
        var rules = source.OfType<Dictionary<string, object>>().Where(rule => {
            object action;
            if (!rule.TryGetValue("action", out action)) return false;
            var fields = action as Dictionary<string, object>;
            return fields != null && actions.Contains(Field(fields, "type"));
        }).ToArray();
        if (rules.Length == 0 || rules.Length > 30000) throw new IOException("Недопустимое число правил EasyList.");
        return rules;
    }

    static object VideoAdRule(int id, string pattern) {
        return new {
            id, priority = 5,
            action = new { type = "block" },
            condition = new {
                urlFilter = pattern,
                resourceTypes = new[] { "xmlhttprequest", "sub_frame", "media" },
            },
        };
    }

    static object KodikVastRule() {
        // Regional uBO Lite rules identify these VAST XML requests by their player origin.
        return new {
            id = 7, priority = 10,
            action = new { type = "block" },
            condition = new {
                urlFilter = "/assets/vendor/*.xml^",
                initiatorDomains = new[] {
                    "aniqit.com", "anivod.com", "kodik.biz", "kodik.cc", "kodik.info",
                    "kodikdb.com", "kodikplayer.com", "u-cdn.org", "u-cdn.top", "u-play.pro", "u-stream.in",
                },
                resourceTypes = new[] { "xmlhttprequest" },
            },
        };
    }

    static object VideoHubAdRule(int id, string pattern) {
        // The player's VAST fetch wrapper uses these paths for Adfox and Buzzoola.
        return new {
            id, priority = 10,
            action = new { type = "block" },
            condition = new {
                regexFilter = pattern,
                isUrlFilterCaseSensitive = true,
                initiatorDomains = new[] { "cdnvideohub.com", "animesss.com", "animesss.tv" },
                resourceTypes = new[] { "xmlhttprequest" },
            },
        };
    }
}
