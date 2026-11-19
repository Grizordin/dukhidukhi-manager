using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

sealed class ClientUpdate {
    const string ClientName = "animesss-desktop.exe";
    const string ReceiptName = "CLIENT_PATCH_REPORT.json";
    const string PatchName = "Update-PatchedClient.ps1";
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
            UpToDate = local != null && SameHash(sourceHash, local.source_sha256),
        };
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

    internal void EnsureCurrent(string source) {
        var local = VerifiedCopy();
        if (source == null) {
            if (local != null) return;
            throw new IOException("Установите официальный AnimeSSS, затем снова нажмите «Открыть AnimeSSS».");
        }
        string hash = ExtensionPackages.Hash(File.ReadAllBytes(source));
        if (local != null && SameHash(hash, local.source_sha256)) return;
        string patch = Path.Combine(root, PatchName);
        if (!File.Exists(patch)) patch = Path.GetFullPath(Path.Combine(root, "..", PatchName));
        if (!File.Exists(patch)) throw new FileNotFoundException("Патчер AnimeSSS отсутствует в комплекте менеджера.");
        RunPatch(patch, source, hash);
        local = VerifiedCopy();
        if (local == null || !SameHash(hash, local.source_sha256))
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
