using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

static class ManagerUpdate {
    public const string VersionText = "0.7.31";
    public const string Repository = "Grizordin/dukhidukhi-manager";
    public const string InstallerName = "DukhiDukhi-Manager-Setup.exe";
    public const string SetupMutex = @"Local\AnimeSSS.DukhiDukhi.Setup";
    const string Product = "Духи Духи менеджер";
    const int MaxSetupSize = 64 * 1024 * 1024;

    internal sealed class Release {
        public string tag_name { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public Asset[] assets { get; set; }
    }

    internal sealed class Asset {
        public string name { get; set; }
        public string browser_download_url { get; set; }
        public string digest { get; set; }
        public int size { get; set; }
    }

    internal sealed class Update {
        public Version Version { get; private set; }
        public Asset Installer { get; private set; }
        public Update(Version version, Asset installer) {
            Version = version;
            Installer = installer;
        }
    }

    internal static Update SelectRelease(Release release, Version installed) {
        if (release == null || release.draft || release.prerelease ||
            !Regex.IsMatch(release.tag_name ?? "", @"^v[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}$")) return null;
        var version = Version.Parse(release.tag_name.Substring(1));
        if (version <= installed) return null;

        var installers = (release.assets ?? new Asset[0]).Where(asset => asset != null && asset.name == InstallerName).ToArray();
        if (installers.Length != 1) return null;
        var installer = installers[0];
        string expected = "https://github.com/" + Repository + "/releases/download/" + release.tag_name + "/" + InstallerName;
        if (installer.browser_download_url != expected || installer.size <= 0 || installer.size > MaxSetupSize ||
            !Regex.IsMatch(installer.digest ?? "", @"^sha256:[0-9a-f]{64}$", RegexOptions.IgnoreCase))
            throw new IOException("В релизе менеджера отсутствуют корректные данные установщика.");
        return new Update(version, installer);
    }

    internal static void VerifyInstaller(Update update, byte[] bytes) {
        if (bytes.Length != update.Installer.size ||
            !String.Equals("sha256:" + ExtensionPackages.Hash(bytes), update.Installer.digest, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Контрольная сумма установщика менеджера не совпала.");
    }

    static HttpClient CreateClient() {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) {
            Timeout = TimeSpan.FromSeconds(25),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DukhiDukhi-Manager/" + VersionText);
        return client;
    }

    internal static async Task<Update> CheckAsync(CancellationToken cancellation) {
        string address = "https://api.github.com/repos/" + Repository + "/releases/latest";
        using (var client = CreateClient())
        using (var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellation)) {
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            byte[] data = await AdBlocker.ReadDownload(response, 1024 * 1024, cancellation);
            var release = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }
                .Deserialize<Release>(Encoding.UTF8.GetString(data));
            return SelectRelease(release, Version.Parse(VersionText));
        }
    }

    internal static async Task<byte[]> DownloadAsync(Update update, CancellationToken cancellation) {
        using (var client = CreateClient()) {
            byte[] bytes = await AdBlocker.Download(client, update.Installer.browser_download_url, update.Installer.size, cancellation);
            VerifyInstaller(update, bytes);
            return bytes;
        }
    }

    static string CacheDirectory() {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DukhiDukhiManager", "manager-updates");
    }

    internal static void ClearCache() {
        try {
            var cache = CacheDirectory();
            if (!Directory.Exists(cache)) return;
            foreach (string path in Directory.EnumerateFiles(cache, "manager-update-*.exe")) {
                try { File.Delete(path); }
                catch (IOException) {}
                catch (UnauthorizedAccessException) {}
            }
        } catch (IOException) {}
        catch (UnauthorizedAccessException) {}
    }

    internal static void VerifyInstallerIdentity(string path, Version version) {
        var file = FileVersionInfo.GetVersionInfo(path);
        if ((file.ProductName ?? "").TrimEnd() != Product ||
            new Version(file.FileMajorPart, file.FileMinorPart, file.FileBuildPart) != version)
            throw new IOException("Версия или название установщика не совпадают с релизом менеджера.");
    }

    internal static void LaunchInstaller(Update update, byte[] bytes, string installation) {
        string cache = CacheDirectory();
        Directory.CreateDirectory(cache);
        string path = Path.Combine(cache, "manager-update-" + Guid.NewGuid().ToString("N") + ".exe");
        try {
            File.WriteAllBytes(path, bytes);
            VerifyInstallerIdentity(path, update.Version);
            string arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DUKHIAUTOUPDATE=1 /DIR=\"" + installation + "\"";
            using (var process = Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = false })) {
                if (process == null) throw new IOException("Windows не запустила установщик менеджера.");
            }
        } catch {
            try { File.Delete(path); } catch (IOException) {} catch (UnauthorizedAccessException) {}
            throw;
        }
    }

    internal static bool WaitForSetup() {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(20)) {
            Mutex setup;
            if (!Mutex.TryOpenExisting(SetupMutex, out setup)) return true;
            setup.Dispose();
            Thread.Sleep(100);
        }
        return false;
    }
}
