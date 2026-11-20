using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

static class ClientUpdateRelay {
    [STAThread] static void Main(string[] args) {
        try {
            if (args.Length != 2 || args[0] != "--apply")
                throw new IOException("AnimeSSS не передал команду установки обновления.");
            string plan = Path.GetFullPath(args[1]);
            string directory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (!String.Equals(Path.GetDirectoryName(plan), directory, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(plan) != "plan.json")
                throw new IOException("AnimeSSS передал план из другой папки.");
            string manager = Environment.GetEnvironmentVariable("DUKHI_ANIMESSS_MANAGER_EXE");
            if (String.IsNullOrEmpty(manager) || !Path.IsPathRooted(manager) ||
                Path.GetFileName(manager) != "AnimeSSS.Local.exe" || !File.Exists(manager))
                throw new IOException("Не удалось найти менеджер, из которого запущен AnimeSSS.");
            var start = new ProcessStartInfo(manager, "--apply-client-update \"" + plan + "\"") {
                WorkingDirectory = Path.GetDirectoryName(manager), UseShellExecute = false,
            };
            using (var process = Process.Start(start)) {
                if (process == null) throw new IOException("Windows не запустила установку обновления.");
            }
        } catch (Exception error) {
            MessageBox.Show(error.Message, "Обновление AnimeSSS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Environment.ExitCode = 1;
        }
    }
}
