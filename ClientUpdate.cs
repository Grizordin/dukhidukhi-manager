using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Win32;

sealed class ClientUpdate {
    const string ClientName = "animesss-desktop.exe";
    // Public key embedded in the official AnimeSSS updater.
    const string UpdatePublicKey = "RWRAjQIgh9rjNPW377JtF+IMX7nd+eYQ9SRAuBgGyIEhTN8L2WlPd9oC";
    internal const string ManagerPathVariable = "DUKHI_ANIMESSS_MANAGER_EXE";
    const string ClientUpdateMutex = @"Local\AnimeSSS.DukhiDukhi.ClientUpdate";
    const string ReceiptName = "CLIENT_PATCH_REPORT.json";
    const string PatchName = "Update-PatchedClient.ps1";
    static readonly HashSet<string> ManagerFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ClientName, "animesss-launcher.exe", "uninstall.exe",
    };
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AnimeSSS";
    readonly string root;

    public ClientUpdate(string root) {
        this.root = root;
        LocalPath = Path.Combine(root, "app", ClientName);
    }

    public string LocalPath { get; private set; }

    public sealed class Receipt {
        public string source_sha256 { get; set; }
        public string output_sha256 { get; set; }
        public string output_version { get; set; }
        public Dictionary<string, string> client_assets { get; set; }
    }

    public sealed class State {
        public string OfficialVersion { get; set; }
        public string LocalVersion { get; set; }
        public bool UpToDate { get; set; }
    }

    static string OfficialPath() {
        string registered;
        using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey)) {
            registered = key == null ? null : key.GetValue("InstallLocation") as string;
        }
        if (!String.IsNullOrWhiteSpace(registered)) {
            string folder = Environment.ExpandEnvironmentVariables(registered.Trim().Trim('"'));
            if (Path.IsPathRooted(folder)) {
                string path = Path.Combine(folder, ClientName);
                if (File.Exists(path)) return path;
            }
        }
        string standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AnimeSSS", ClientName);
        return File.Exists(standard) ? standard : null;
    }

    static string VersionOf(string path) {
        if (path == null) return null;
        try {
            var info = FileVersionInfo.GetVersionInfo(path);
            return String.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
        } catch (IOException) {
            return "уточняется";
        } catch (UnauthorizedAccessException) {
            return "уточняется";
        }
    }

    static bool SameHash(string left, string right) {
        return !String.IsNullOrEmpty(left) && String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    Receipt VerifiedCopy() {
        string path = Path.Combine(root, ReceiptName);
        if (!File.Exists(LocalPath) || !File.Exists(path)) return null;
        try {
            var receipt = new JavaScriptSerializer().Deserialize<Receipt>(File.ReadAllText(path));
            if (receipt != null && SameHash(receipt.output_sha256, ExtensionPackages.Hash(File.ReadAllBytes(LocalPath))))
                return receipt;
        } catch (ArgumentException) {
            // An incomplete receipt is regenerated from the official client.
        } catch (InvalidOperationException) {
            // An unreadable receipt is not proof that the executable is patched.
        }
        return null;
    }

    State Inspect(string source, string sourceHash, Receipt local) {
        return new State {
            OfficialVersion = VersionOf(source),
            LocalVersion = local == null ? null : local.output_version,
            UpToDate = local != null && SameHash(sourceHash, local.source_sha256) && AssetsCurrent(source, local),
        };
    }

    bool AssetsCurrent(string source, Receipt receipt) {
        if (receipt.client_assets == null) return false;
        var assets = new Dictionary<string, string>(receipt.client_assets, StringComparer.OrdinalIgnoreCase);
        var official = new DirectoryInfo(Path.GetDirectoryName(source));
        var installed = new DirectoryInfo(Path.GetDirectoryName(LocalPath));
        try { return MatchAssets(official, installed, "", assets) && assets.Count == 0; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    static bool MatchAssets(DirectoryInfo expected, DirectoryInfo installed, string prefix, Dictionary<string, string> assets) {
        if (!installed.Exists ||
            ((expected.Attributes | installed.Attributes) & FileAttributes.ReparsePoint) != 0) return false;
        foreach (var file in expected.EnumerateFiles()) {
            if (prefix.Length == 0 && ManagerFiles.Contains(file.Name)) continue;
            string path = prefix + file.Name;
            string hash;
            if (!assets.TryGetValue(path, out hash)) return false;
            var actual = new FileInfo(Path.Combine(installed.FullName, file.Name));
            if (!actual.Exists || actual.Length != file.Length ||
                ((file.Attributes | actual.Attributes) & FileAttributes.ReparsePoint) != 0 ||
                !SameHash(hash, AssetHash(file.FullName)) || !SameHash(hash, AssetHash(actual.FullName))) return false;
            assets.Remove(path);
        }
        foreach (var folder in expected.EnumerateDirectories()) {
            if (!MatchAssets(folder, new DirectoryInfo(Path.Combine(installed.FullName, folder.Name)),
                    prefix + folder.Name + "\\", assets)) return false;
        }
        return true;
    }

    static string AssetHash(string path) {
        using (var file = File.OpenRead(path))
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "");
    }

    public State Inspect() {
        string source = OfficialPath();
        string hash = null;
        try {
            if (source != null) hash = ExtensionPackages.Hash(File.ReadAllBytes(source));
        } catch (IOException) {
            // The official updater may be replacing the file.
        } catch (UnauthorizedAccessException) {
            // Keep the available local version visible.
        }
        return Inspect(source, hash, VerifiedCopy());
    }

    public void EnsureCurrent() {
        EnsureCurrent(OfficialPath());
    }

    public void ApplyInstalledUpdate(string planPath) {
        bool first;
        using (var updateLock = new Mutex(true, ClientUpdateMutex, out first)) {
            if (!first) throw new IOException("Обновление AnimeSSS уже выполняется.");
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "com.animesss.modktop");
            ApplyLauncherPlan(planPath, OfficialPath(), profile);
        }
    }

    internal void ApplyLauncherPlan(string planPath, string official, string profile) {
        string helpers = Path.Combine(profile, "bootstrap", "helpers");
        planPath = Path.GetFullPath(planPath);
        string planFolder = Path.GetDirectoryName(planPath);
        if (!SamePath(Path.GetDirectoryName(planFolder), helpers) || Path.GetFileName(planPath) != "plan.json")
            throw new IOException("Не удалось подтвердить расположение плана обновления.");
        CheckPlainPath(planPath);

        var json = new JavaScriptSerializer { MaxJsonLength = 16384, RecursionLimit = 5 };
        var plan = json.Deserialize<LauncherPlan>(ReadPlan(planPath));
        Version version;
        if (plan == null || !Version.TryParse(plan.version, out version) || version.Build < 0 ||
            plan.version != version.ToString() ||
            String.IsNullOrEmpty(plan.signature) || plan.repair || plan.parent <= 0 ||
            !SamePath(plan.install_dir, Path.GetDirectoryName(LocalPath))) {
            throw new IOException("AnimeSSS передал несовместимый план обновления.");
        }
        string cached = Path.Combine(profile, "updates", "AnimeSSS_" + plan.version + "_x64-setup.exe");
        if (!SamePath(cached, plan.installer))
            throw new IOException("Установщик обновления находится вне кэша AnimeSSS.");
        CheckPlainPath(cached);
        var current = VerifiedCopy();
        Version localVersion;
        if (current == null || !Version.TryParse(current.output_version, out localVersion) || version <= localVersion)
            throw new IOException("План обновления не подходит установленной копии AnimeSSS.");
        if (String.IsNullOrEmpty(official))
            throw new IOException("Для установки обновления нужен официальный AnimeSSS.");
        string officialFolder = Path.GetDirectoryName(official);
        if (SamePath(officialFolder, Path.GetDirectoryName(LocalPath)))
            throw new IOException("Официальный AnimeSSS должен находиться в отдельной папке.");
        WaitForUpdatedClient(plan.parent);

        if (!VersionAtLeast(official, version))
            InstallVerifiedUpdate(planFolder, cached, plan, officialFolder);
        if (!VersionAtLeast(official, version))
            throw new IOException("Установщик не подтвердил установку AnimeSSS " + version + ".");
        EnsureCurrent(official);
    }

    sealed class LauncherPlan {
        public string version { get; set; }
        public string signature { get; set; }
        public string install_dir { get; set; }
        public string installer { get; set; }
        public bool repair { get; set; }
        public int parent { get; set; }
    }

    static bool SamePath(string first, string second) {
        return !String.IsNullOrEmpty(first) && !String.IsNullOrEmpty(second) &&
            String.Equals(Path.GetFullPath(first).TrimEnd('\\', '/'), Path.GetFullPath(second).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
    }

    static void CheckPlainPath(string path) {
        for (string item = path; item != null; item = Path.GetDirectoryName(item)) {
            if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Путь обновления AnimeSSS перенаправлен.");
        }
    }

    static string ReadPlan(string path) {
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            if (file.Length == 0 || file.Length > 16384)
                throw new IOException("Размер плана обновления AnimeSSS некорректен.");
            using (var reader = new StreamReader(file, new UTF8Encoding(false, true))) return reader.ReadToEnd();
        }
    }

    static bool VersionAtLeast(string path, Version version) {
        Version actual;
        return Version.TryParse(VersionOf(path), out actual) && actual >= version;
    }

    void WaitForUpdatedClient(int pid) {
        Process client;
        try { client = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }
        using (client) {
            if (client.HasExited) return;
            if (!SamePath(client.MainModule.FileName, LocalPath))
                throw new IOException("План обновления относится к другому процессу.");
            if (!client.WaitForExit(30000))
                throw new IOException("AnimeSSS не завершился для установки обновления.");
        }
    }

    void InstallVerifiedUpdate(string planFolder, string installer, LauncherPlan plan, string officialFolder) {
        string working = Path.Combine(planFolder, "dukhi-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(working);
        string stagedSetup = Path.Combine(working, Path.GetFileName(installer));
        string stagedSignature = stagedSetup + ".minisig";
        try {
            File.Copy(installer, stagedSetup);
            byte[] signature;
            try { signature = Convert.FromBase64String(plan.signature); }
            catch (FormatException error) { throw new IOException("Подпись обновления AnimeSSS повреждена.", error); }
            File.WriteAllBytes(stagedSignature, signature);
            // Keep the verified bytes locked against replacements through setup.
            using (File.Open(stagedSetup, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (File.Open(stagedSignature, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                VerifyInstaller(stagedSetup, stagedSignature, signature, plan.version);
                InstallWithoutRestart(stagedSetup, officialFolder);
            }
        } finally {
            try {
                File.Delete(stagedSignature);
                File.Delete(stagedSetup);
                Directory.Delete(working);
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    void VerifyInstaller(string setup, string signaturePath, byte[] signature, string version) {
        string verifier = Path.Combine(root, "vendor", "minisign", "minisign.exe");
        if (!File.Exists(verifier)) throw new FileNotFoundException("В комплекте менеджера отсутствует проверка подписи AnimeSSS.");
        var start = new ProcessStartInfo(verifier,
            "-V -Q -H -P " + UpdatePublicKey + " -m \"" + setup + "\" -x \"" + signaturePath + "\"") {
                WorkingDirectory = Path.GetDirectoryName(verifier), UseShellExecute = false, CreateNoWindow = true,
            };
        using (var process = Process.Start(start)) {
            if (process == null) throw new IOException("Не удалось запустить проверку подписи AnimeSSS.");
            if (!process.WaitForExit(30000)) {
                try { process.Kill(); } catch (InvalidOperationException) { }
                throw new IOException("Проверка подписи AnimeSSS не завершилась.");
            }
            if (process.ExitCode != 0) throw new IOException("Подпись установщика AnimeSSS не прошла проверку.");
        }
        // Minisign verifies the trusted comment as well as the installer itself.
        const string prefix = "trusted comment: ";
        var lines = new UTF8Encoding(false, true).GetString(signature).Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 4 || !lines[2].StartsWith(prefix, StringComparison.Ordinal))
            throw new IOException("В подписи AnimeSSS отсутствуют сведения о пакете.");
        var fields = lines[2].Substring(prefix.Length).Split('\t');
        if (Array.IndexOf(fields, "version:" + version) < 0 ||
            Array.IndexOf(fields, "file:" + Path.GetFileName(setup)) < 0)
            throw new IOException("Подписанные имя и версия пакета AnimeSSS не совпадают с обновлением.");
    }

    static void InstallWithoutRestart(string setup, string officialFolder) {
        // NSIS consumes /D as the final argument, without quoting even when the path has spaces.
        // Omitting /R keeps the stock app closed. The manager launches the patched app after verification.
        var start = new ProcessStartInfo(setup, "/S /UPDATE /D=" + officialFolder) {
            WorkingDirectory = officialFolder, UseShellExecute = false, CreateNoWindow = true,
        };
        start.EnvironmentVariables.Remove("WEBVIEW2_USER_DATA_FOLDER");
        start.EnvironmentVariables.Remove("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS");
        start.EnvironmentVariables.Remove(ManagerPathVariable);
        using (var process = Process.Start(start)) {
            if (process == null) throw new IOException("Windows не запустила установщик AnimeSSS.");
            process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException("Установщик AnimeSSS завершился с кодом " + process.ExitCode + ".");
        }
    }

    internal void EnsureCurrent(string source) {
        var local = VerifiedCopy();
        if (source == null) {
            if (local != null) return;
            throw new IOException("Установите официальный AnimeSSS, затем снова нажмите «Открыть AnimeSSS».");
        }
        string hash = ExtensionPackages.Hash(File.ReadAllBytes(source));
        if (local != null && SameHash(hash, local.source_sha256) && AssetsCurrent(source, local)) return;
        string patch = Path.Combine(root, PatchName);
        if (!File.Exists(patch)) patch = Path.GetFullPath(Path.Combine(root, "..", PatchName));
        if (!File.Exists(patch)) throw new FileNotFoundException("Патчер AnimeSSS отсутствует в комплекте менеджера.");
        RunPatch(patch, source, hash);
        local = VerifiedCopy();
        if (local == null || !SameHash(hash, local.source_sha256) || !AssetsCurrent(source, local))
            throw new IOException("Проверка локальной копии после патча не прошла.");
    }

    void RunPatch(string patch, string source, string hash) {
        string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        string arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + patch
            + "\" -SourcePath \"" + source + "\" -DistPath \"" + root.TrimEnd('\\', '/') + "\" -ExpectedSourceHash " + hash;
        var start = new ProcessStartInfo(powershell, arguments) {
            WorkingDirectory = root,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        start.EnvironmentVariables.Remove("PSModulePath");
        start.EnvironmentVariables.Remove("PSModuleAnalysisCachePath");
        using (var process = Process.Start(start)) {
            if (process == null) throw new IOException("Windows не запустила патчер AnimeSSS.");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(output, errors);
            if (process.ExitCode != 0) {
                string reason = errors.Result.Trim();
                string prefix = patch + " : ";
                if (reason.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) reason = reason.Substring(prefix.Length);
                int details = reason.IndexOf("\r\nAt ", StringComparison.Ordinal);
                if (details >= 0) reason = reason.Substring(0, details);
                throw new IOException(String.IsNullOrWhiteSpace(reason) ? "Патчер завершился с ошибкой." : reason);
            }
        }
    }
}
