using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed class ClientHotkey : ApplicationContext {
    const int LowLevelKeyboard = 13;
    const int OpenManagerKey = 0x55;
    const int KeyDown = 0x100, KeyUp = 0x101, SystemKeyDown = 0x104, SystemKeyUp = 0x105;
    const int Control = 0x11, Shift = 0x10, Alt = 0x12, LeftWindows = 0x5b, RightWindows = 0x5c;
    readonly Process client;
    readonly Timer timer = new Timer { Interval = 100 };
    readonly KeyboardCallback callback;
    IntPtr hook;
    bool reopenManager;
    bool swallowedKey;

    delegate IntPtr KeyboardCallback(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError=true)] static extern IntPtr SetWindowsHookEx(int type, KeyboardCallback callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int virtualKey);

    public ClientHotkey(Process client) {
        this.client = client;
        callback = HandleKey;
        hook = SetWindowsHookEx(LowLevelKeyboard, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        timer.Tick += CheckClient;
        timer.Start();
    }

    static bool Pressed(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

    IntPtr HandleKey(int code, IntPtr message, IntPtr data) {
        if (code == 0 && Marshal.ReadInt32(data) == OpenManagerKey) {
            int kind = message.ToInt32();
            if (swallowedKey && (kind == KeyUp || kind == SystemKeyUp)) {
                swallowedKey = false;
                return new IntPtr(1);
            }
            if (kind == KeyDown || kind == SystemKeyDown) {
                if (swallowedKey) return new IntPtr(1);
                uint foregroundPid;
                GetWindowThreadProcessId(GetForegroundWindow(), out foregroundPid);
                if (foregroundPid == client.Id && Pressed(Control) && Pressed(Shift) &&
                    !Pressed(Alt) && !Pressed(LeftWindows) && !Pressed(RightWindows)) {
                    reopenManager = true;
                    swallowedKey = true;
                    return new IntPtr(1);
                }
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    async void CheckClient(object sender, EventArgs args) {
        if (reopenManager) {
            timer.Stop();
            try {
                await Task.Run(() => {
                    if (!client.HasExited) {
                        try { client.Kill(); }
                        catch (InvalidOperationException) { if (!client.HasExited) throw; }
                    }
                    if (!client.WaitForExit(10000)) throw new IOException("Не удалось завершить AnimeSSS Local.");
                });
                // Give the detached WebView2 browser time to release its profile.
                await Task.Delay(1500);
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute=false });
                ExitThread();
            } catch (Exception error) {
                MessageBox.Show(error.Message, "Не удалось открыть менеджер AnimeSSS Local");
                reopenManager = false;
                timer.Start();
            }
        } else if (client.HasExited) {
            ExitThread();
        }
    }

    protected override void Dispose(bool disposing) {
        if (hook != IntPtr.Zero) {
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
