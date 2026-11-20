
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

static class WeatherMaskHost {
    internal const string Origin = "chrome-extension://mdjemcolglkldcmaocemjmbbkgmfpkil/";
    const string Host = "com.dukhidukhi.weather";
    const int MaxBytes = 16384;
    const int HeaderBytes = 16;

    internal static void Register(string root, string executable) {
        string path = Path.Combine(root, "weather-mask-host.json");
        var manifest = new JavaScriptSerializer().Serialize(new {
            name = Host, description = "Духи Духи: вырезы погодного слоя",
            path = executable, type = "stdio", allowed_origins = new[] { Origin },
        });
        if (!File.Exists(path) || File.ReadAllText(path) != manifest)
            File.WriteAllText(path, manifest, new UTF8Encoding(false));
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Edge\NativeMessagingHosts\" + Host)) {
            if (!String.Equals(key.GetValue(null) as string, path, StringComparison.OrdinalIgnoreCase))
                key.SetValue(null, path);
        }
    }

    internal static void Run() {
        try {
            string channel;
            using (var identity = WindowsIdentity.GetCurrent())
                channel = @"Local\DukhiDukhi.WeatherMasks." + identity.User.Value;
            using (var input = new BinaryReader(Console.OpenStandardInput()))
            using (var output = new BinaryWriter(Console.OpenStandardOutput()))
            using (var memory = MemoryMappedFile.CreateOrOpen(channel, HeaderBytes + MaxBytes))
            using (var view = memory.CreateViewAccessor())
            using (var locked = new Mutex(false, channel + ".lock"))
            using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, channel + ".changed")) {
                byte[] message = ReadFrame(input);
                if (message == null) return;
                if (Encoding.UTF8.GetString(message) == "\"watch\"") Watch(input, output, view, locked, wake);
                else Publish(input, message, view, locked, wake);
            }
        } catch (EndOfStreamException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }

    static byte[] ReadFrame(BinaryReader input) {
        int length = input.ReadInt32();
        if (length < 2 || length > MaxBytes) return null;
        var bytes = input.ReadBytes(length);
        return bytes.Length == length ? bytes : null;
    }

    static bool Valid(double[][] boxes) {
        if (boxes == null || boxes.Length > 128) return false;
        foreach (var box in boxes) {
            if (box == null || box.Length != 4 || !(0 <= box[0] && box[0] < box[2] && box[2] <= 1 &&
                0 <= box[1] && box[1] < box[3] && box[3] <= 1)) return false;
        }
        return true;
    }

    static void Publish(BinaryReader input, byte[] bytes, MemoryMappedViewAccessor view, Mutex locked, EventWaitHandle wake) {
        var json = new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 4 };
        using (var publisher = Process.GetCurrentProcess()) {
            int pid = publisher.Id;
            try {
                do {
                    if (!Valid(json.Deserialize<double[][]>(Encoding.UTF8.GetString(bytes)))) return;
                    WithLock(locked, () => {
                        view.WriteArray(HeaderBytes, bytes, 0, bytes.Length);
                        view.Write(0, pid);
                        view.Write(8, bytes.Length);
                        view.Write(4, unchecked(view.ReadInt32(4) + 1));
                    });
                    wake.Set();
                } while ((bytes = ReadFrame(input)) != null);
            } finally {
                WithLock(locked, () => {
                    if (view.ReadInt32(0) != pid) return;
                    view.Write(0, 0);
                    view.Write(8, 0);
                    view.Write(4, unchecked(view.ReadInt32(4) + 1));
                });
                wake.Set();
            }
        }
    }

    static void Watch(BinaryReader input, BinaryWriter output, MemoryMappedViewAccessor view, Mutex locked, EventWaitHandle wake) {
        var disconnected = Task.Run(() => input.BaseStream.ReadByte());
        int? lastRevision = null;
        int currentOwner = 0;
        Process owner = null;
        bool visible = false;
        byte[] empty = Encoding.ASCII.GetBytes("[]");
        try {
            while (!disconnected.IsCompleted) {
                byte[] next = null;
                int pid = 0;
                WithLock(locked, () => {
                    int sequence = view.ReadInt32(4);
                    if (sequence == lastRevision) return;
                    lastRevision = sequence;
                    pid = view.ReadInt32(0);
                    int size = view.ReadInt32(8);
                    if (size < 2 || size > MaxBytes || pid == 0) { next = empty; return; }
                    next = new byte[size];
                    view.ReadArray(HeaderBytes, next, 0, size);
                });
                if (next != null && pid != currentOwner) {
                    if (owner != null) owner.Dispose();
                    owner = null;
                    currentOwner = pid;
                    if (pid != 0) {
                        try {
                            owner = Process.GetProcessById(pid);
                            var handle = owner.Handle; // Keep the process identity across PID reuse.
                        } catch (ArgumentException) { }
                        catch (System.ComponentModel.Win32Exception) {
                            if (owner != null) owner.Dispose();
                            owner = null;
                        }
                    }
                }
                if (owner == null || owner.HasExited) {
                    if (next != null || visible) Send(output, empty);
                    visible = false;
                } else if (next != null) {
                    Send(output, next);
                    visible = true;
                }
                wake.WaitOne(180);
            }
        } finally { if (owner != null) owner.Dispose(); }
    }

    static void Send(BinaryWriter output, byte[] bytes) {
        output.Write(bytes.Length);
        output.Write(bytes);
        output.Flush();
    }

    static void WithLock(Mutex gate, Action action) {
        try { gate.WaitOne(); } catch (AbandonedMutexException) { }
        try { action(); } finally { gate.ReleaseMutex(); }
    }
}
