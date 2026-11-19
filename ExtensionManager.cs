using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Diagnostics;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

public partial class ExtensionManager : Form {
    const string ExtensionId = "bnimknkampfeeemccidiigealnpoheap";
    const string BundledTampermonkey = "tampermonkey-legacy-quick-controls-updates";
    readonly WebView2 web = new WebView2();
    readonly Label status = new Label();
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    readonly ClientUpdate clientUpdate = new ClientUpdate(AppDomain.CurrentDomain.BaseDirectory);
    readonly string[] args;
    CoreWebView2Environment environment;
    string profile;
    bool busy;
    bool checkingManagerUpdate;
    readonly List<WebView2> popups = new List<WebView2>();
    [STAThread] public static void Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>=1&&args[0]=="--package-test") {PackageTest(args);return;}
        if(args.Length==4 && args[0]=="--launch-wait") {
            try {
                foreach(string id in args[1].Split(',')) {
                    try {var p=Process.GetProcessById(Int32.Parse(id));if(!p.WaitForExit(15000))throw new IOException("Профиль ещё занят. Закройте копию AnimeSSS через меню «Выйти» и повторите запуск.");}
                    catch(ArgumentException) {}
                }
                var start=new ProcessStartInfo(args[2],"--via-launcher") {WorkingDirectory=Path.GetDirectoryName(args[2]),UseShellExecute=false};
                if(args[3]=="-")start.EnvironmentVariables.Remove("WEBVIEW2_USER_DATA_FOLDER");else start.EnvironmentVariables["WEBVIEW2_USER_DATA_FOLDER"]=args[3];
                start.EnvironmentVariables.Remove("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS");
                using (var client = Process.Start(start)) {
                    if (client == null) throw new IOException("Windows не запустила AnimeSSS Local.");
                    if (args[3] == "-") {
                        using (var hotkey = new ClientHotkey(client)) Application.Run(hotkey);
                    }
                }
            }catch(Exception e){MessageBox.Show(e.Message,"Не удалось запустить копию AnimeSSS");}
            return;
        }
        if (args.Length == 1 && args[0].StartsWith("animesss-local:", StringComparison.OrdinalIgnoreCase)) return;
        if (args.Length == 1 && args[0] == "--after-manager-update") args = new string[0];
        if (args.Length > 0) {
            Application.Run(new ExtensionManager(args));
            return;
        }
        if (!ManagerUpdate.WaitForSetup()) {
            MessageBox.Show("Дождитесь завершения установки и откройте менеджер снова.", "Духи Духи менеджер");
            return;
        }
        bool firstManager;
        using (var managerLock = new System.Threading.Mutex(true, @"Local\AnimeSSS.DukhiDukhi.Manager", out firstManager)) {
            if (!firstManager) {
                MessageBox.Show("Менеджер уже открыт.", "Духи Духи менеджер");
                return;
            }
            Application.Run(new ExtensionManager(args));
        }
    }
    public ExtensionManager(string[] arguments) {
        args = arguments;
        BuildExtensionUI();
        Shown += async delegate { await Initialize(); };
    }

    async Task Initialize() {
        try {
            bool probing=args.Length>=2&&(args[0].StartsWith("--probe",StringComparison.Ordinal)||args[0]=="--extensions-probe");
            profile=probing?Path.GetFullPath(args[1]):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"com.animesss.modktop","webview-profile");
            if (!probing) {
                using (var running = FindRunningClient()) {
                    if (running != null) throw new IOException("Сначала завершите AnimeSSS Local через меню «Выйти», затем откройте менеджер.");
                }
            }
            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",null,EnvironmentVariableTarget.Process);
            var options=new CoreWebView2EnvironmentOptions();options.AreBrowserExtensionsEnabled=true;
            environment=await CoreWebView2Environment.CreateAsync(null,profile,options);await web.EnsureCoreWebView2Async(environment);
            web.CoreWebView2.NewWindowRequested+=async (sender,e)=>{
                if (IsExtensionStore(e.Uri)) { e.Handled = true; return; }
                using (var deferral = e.GetDeferral()) {
                    e.Handled = true;
                    Uri uri;
                    bool confirmation = Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) &&
                        IsTampermonkeyPage(uri, "/ask.html");
                    var window = new Form {
                        Text = confirmation ? "Tampermonkey — установка скрипта" : "AnimeSSS — окно расширения",
                        Width = 1000, Height = 740, StartPosition = FormStartPosition.CenterParent,
                    };
                    var child = new WebView2 { Dock = DockStyle.Fill };
                    window.Controls.Add(child);
                    window.FormClosed += (s, closed) => popups.Remove(child);
                    window.Show(this);
                    popups.Add(child);
                    try {
                        await child.EnsureCoreWebView2Async(environment);
                        child.CoreWebView2.WindowCloseRequested += (s, closed) => window.Close();
                        e.NewWindow = child.CoreWebView2;
                        if (confirmation) window.Activate();
                    } catch (Exception error) {
                        window.Close();
                        if (!IsDisposed) status.Text = "Не удалось открыть окно расширения: " + error.Message;
                    }
                }
            };
            web.CoreWebView2.Settings.IsStatusBarEnabled=true;
            web.CoreWebView2.NavigationStarting += (sender, e) => {
                if (IsExtensionStore(e.Uri)) e.Cancel = true;
            };
            status.Text="Профиль AnimeSSS подключён.";
            if (probing) ShowTampermonkeyView();
            if(args.Length>=2&&args[0]=="--extensions-probe"){await RunExtensionProbe();return;}
            if(probing) {
                var result=new Dictionary<string,object>();result["runtime"]=environment.BrowserVersionString;result["profile"]=profile;
                try {var existing=await web.CoreWebView2.Profile.GetBrowserExtensionsAsync();var ext=args.Length>=3?await web.CoreWebView2.Profile.AddBrowserExtensionAsync(Path.GetFullPath(args[2])):existing.FirstOrDefault(x=>x.Id==ExtensionId)??await web.CoreWebView2.Profile.AddBrowserExtensionAsync(Path.Combine(root,"extensions","tampermonkey-legacy-quick-controls"));result["extension"]=new {id=ext.Id,name=ext.Name,enabled=ext.IsEnabled};
                    web.CoreWebView2.Navigate("chrome-extension://"+ext.Id+"/options.html");await Task.Delay(5000);
                    result["dashboard_url"]=web.Source==null?null:web.Source.AbsoluteUri;result["dashboard_title"]=web.CoreWebView2.DocumentTitle;
                    result["dashboard_text"]=await web.CoreWebView2.ExecuteScriptAsync("document.body ? document.body.innerText.slice(0,2500) : ''");
                    result["anonymous_statistics"]=await DisableStatistics();
                    result["settings_controls"]=await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify(Array.from(document.querySelectorAll('input, label')).map(x=>({tag:x.tagName,id:x.id,type:x.type,checked:x.checked,text:x.innerText,html:x.outerHTML.slice(0,500)})))");
                    result["userscripts_api"]=await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({userScripts:typeof chrome.userScripts,request:typeof chrome.permissions})");
                    if(args[0]=="--probe-internal") {
                        web.CoreWebView2.Navigate("edge://extensions/");await Task.Delay(1500);
                        result["internal_url"]=web.Source.AbsoluteUri;
                        result["internal_api"]=await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({url:location.href,developerPrivate:typeof chrome.developerPrivate,text:document.body?document.body.innerText.slice(0,700):''})");
                    }
                    if(args.Length>=5) {
                        web.CoreWebView2.Navigate("chrome-extension://"+ext.Id+"/options.html#nav=new-user-script");await Task.Delay(1200);
                        result["editor_state"]=await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({url:location.href,editors:Array.from(document.querySelectorAll('.CodeMirror,textarea')).map(x=>({tag:x.tagName,id:x.id,className:x.className,cm:!!x.CodeMirror})),text:document.body.innerText.slice(-1500)})");
                        string code;using(var http=new System.Net.WebClient()){code=http.DownloadString(args[3]);}
                        result["editor_set"]=await web.CoreWebView2.ExecuteScriptAsync("(function(){var cm=Array.from(document.querySelectorAll('.CodeMirror')).find(x=>x.CodeMirror);if(!cm)return 'missing-editor';cm.CodeMirror.setValue("+new JavaScriptSerializer().Serialize(code)+");return 'set';})()");
                        result["editor_save"]=await web.CoreWebView2.ExecuteScriptAsync("(function(){var b=document.getElementById('button_bmV3LXVzZXItc2NyaXB0X3NhdmU_bu');if(!b)return 'missing-save';b.click();return 'clicked';})()");
                        await Task.Delay(1000);web.CoreWebView2.Navigate(args[4]);await Task.Delay(2500);
                        result["script_result"]=await web.CoreWebView2.ExecuteScriptAsync("document.documentElement.dataset.localModProbe||null");
                        var views=new List<WebView2>(popups);views.Add(web);var pages=new List<object>();
                        foreach(var view in views) {pages.Add(new {url=view.Source==null?null:view.Source.AbsoluteUri,text=await view.CoreWebView2.ExecuteScriptAsync("document.body?document.body.innerText.slice(0,5000):''"),controls=await view.CoreWebView2.ExecuteScriptAsync("JSON.stringify(Array.from(document.querySelectorAll('input,button')).map(x=>({tag:x.tagName,id:x.id,type:x.type,value:x.value,text:x.innerText})))")});}
                        result["install_pages"]=pages;
                    }
                }catch(Exception ex){result["error"]=ex.ToString();}
                File.WriteAllText(Path.Combine(root,"probe-result.json"),new JavaScriptSerializer().Serialize(result));
                if(args[0]=="--probe-handoff"||args[0]=="--probe-handoff-natural")LaunchClient();else Close();return;
            }
            try {
                await AttachInstalledTampermonkey();
            } catch (Exception error) {
                status.Text = "Tampermonkey: " + error.Message;
            }
            await RefreshExtensionList();
            await RefreshClientVersion(prepareMissingCopy: true);
            managerHome.Enabled = true;
            await StartManagerUpdateChecks();
        }catch(Exception e){status.Text=e.Message;if(args.Length>0){File.WriteAllText(Path.Combine(root,"probe-result.json"),new JavaScriptSerializer().Serialize(new {error=e.ToString()}));Close();}else MessageBox.Show(this,e.Message,"Ошибка WebView2");}
    }

    async Task StartManagerUpdateChecks() {
        if (!File.Exists(Path.Combine(root, "manager-install.ini"))) return;
        ManagerUpdate.ClearCache();
        var timer = new Timer { Interval = (int)TimeSpan.FromHours(6).TotalMilliseconds };
        timer.Tick += async delegate { await CheckManagerUpdate(); };
        FormClosed += delegate { timer.Dispose(); };
        timer.Start();
        await CheckManagerUpdate();
    }

    async Task CheckManagerUpdate() {
        if (checkingManagerUpdate || busy || IsDisposed) return;
        checkingManagerUpdate = true;
        ManagerUpdate.Update update = null;
        try {
            using (var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(75))) {
                update = await ManagerUpdate.CheckAsync(timeout.Token);
                if (update == null || IsDisposed || busy) return;
                byte[] setup = await ManagerUpdate.DownloadAsync(update, timeout.Token);
                if (IsDisposed || busy) return;
                using (var running = FindRunningClient()) {
                    if (running != null) return;
                }
                status.Text = "Устанавливаю обновление менеджера " + update.Version + "…";
                string installation = Directory.GetParent(root.TrimEnd(Path.DirectorySeparatorChar)).FullName;
                ManagerUpdate.LaunchInstaller(update, setup, installation);
                foreach (var popup in popups.ToArray()) popup.Dispose();
                web.Dispose();
                Close();
            }
        } catch (Exception error) {
            if (!IsDisposed && update != null) status.Text = "Обновление менеджера: " + error.Message;
        } finally {
            checkingManagerUpdate = false;
        }
    }

    async Task AttachInstalledTampermonkey() {
        string marker = Path.Combine(root, "manager-install.ini");
        string receipt = Path.Combine(root, "tampermonkey-attached.txt");
        string bundled = Path.Combine(root, "extensions", BundledTampermonkey);
        if (!Directory.Exists(bundled)) return;
        if (!File.Exists(marker) || (File.Exists(receipt) && File.ReadAllText(receipt) == BundledTampermonkey)) return;
        var registered = await web.CoreWebView2.Profile.GetBrowserExtensionsAsync();
        var existing = registered.FirstOrDefault(candidate => candidate.Id == ExtensionId);
        if (existing == null) return;
        bool wasEnabled = existing.IsEnabled;
        var extension = await web.CoreWebView2.Profile.AddBrowserExtensionAsync(bundled);
        if (extension.Id != ExtensionId) throw new IOException("Идентификатор Tampermonkey при переносе не совпал.");
        if (extension.IsEnabled != wasEnabled) await extension.EnableAsync(wasEnabled);
        File.WriteAllText(receipt, BundledTampermonkey);
    }

    string TampermonkeyFolder() {
        string extensions = Path.Combine(root, "extensions");
        return new[] {
            BundledTampermonkey, "tampermonkey-legacy-quick-controls-reminders",
            "tampermonkey-legacy-quick-controls-import", "tampermonkey-legacy-quick-controls",
        }.Select(name => Path.Combine(extensions, name)).FirstOrDefault(Directory.Exists)
            ?? Path.Combine(extensions, BundledTampermonkey);
    }
    async Task Install() {
        using(var running=FindRunningClient())if(running!=null)throw new IOException("Перед обновлением Tampermonkey закройте AnimeSSS через меню «Выйти», затем повторите установку.");
        var dir=TampermonkeyFolder();
        if(!File.Exists(Path.Combine(dir,"manifest.json")))throw new IOException("Папка официального Tampermonkey не найдена.");
        var ext=await web.CoreWebView2.Profile.AddBrowserExtensionAsync(dir);
        if(ext.Id!=ExtensionId)throw new InvalidOperationException("Идентификатор Tampermonkey не совпал с официальным.");
        if (File.Exists(Path.Combine(root, "manager-install.ini")))
            File.WriteAllText(Path.Combine(root, "tampermonkey-attached.txt"), Path.GetFileName(dir));
        status.Text="Tampermonkey Legacy 5.1.1.2 с горячими командами установлен в прежний профиль.";await Dashboard();
        await Task.Delay(2200);var disabled=await DisableStatistics();
        await RefreshExtensionList();
        status.Text=disabled=="false"?"Tampermonkey Legacy обновлён. Анонимная статистика выключена.":"Tampermonkey установлен. Проверьте настройку «Анонимная статистика» вручную.";
    }
    async Task<string> DisableStatistics() {
        var changed=await web.CoreWebView2.ExecuteScriptAsync("(function(){var x=Array.from(document.querySelectorAll('input[type=checkbox]')).find(x=>x.id.endsWith('c3RhdGlzdGljc19lbmFibGVk_cb')||/^(Анонимная статистика|Anonymous statistics)$/.test(x.name));if(!x)return 'missing';if(x.checked){x.click();Array.from(document.querySelectorAll('input[type=button][id$=\"_Save\"]:not([disabled])')).forEach(b=>b.click());}return x.checked;})()");
        if(changed!="false")return changed;
        await Task.Delay(700);web.CoreWebView2.Reload();await Task.Delay(1800);
        return await web.CoreWebView2.ExecuteScriptAsync("(function(){var x=Array.from(document.querySelectorAll('input[type=checkbox]')).find(x=>x.id.endsWith('c3RhdGlzdGljc19lbmFibGVk_cb')||/^(Анонимная статистика|Anonymous statistics)$/.test(x.name));return x?x.checked:'missing-after-reload';})()");
    }
    static bool IsTampermonkeyPage(Uri uri, string path) {
        return uri != null && uri.Scheme == "chrome-extension" && uri.Host == ExtensionId && uri.AbsolutePath == path;
    }

    async Task InstallScript() {
        var extensions = await web.CoreWebView2.Profile.GetBrowserExtensionsAsync();
        if (!extensions.Any(extension => extension.Id == ExtensionId && extension.IsEnabled))
            throw new IOException("Сначала включите Tampermonkey.");
        using (var dialog = new OpenFileDialog {
            Filter = "Userscript (*.user.js)|*.user.js|JavaScript (*.js)|*.js",
            Title = "Выберите скрипт для установки",
        }) {
            if (dialog.ShowDialog(this) == DialogResult.OK) await ImportScript(dialog.FileName);
        }
    }

    sealed class ScriptImportResult {
        public int installed { get; set; }
        public string error { get; set; }
    }

    async Task ImportScript(string path) {
        if (new FileInfo(path).Length > 8 * 1024 * 1024)
            throw new IOException("Файл больше 8 МиБ. Используйте импорт в панели Tampermonkey.");
        string code = File.ReadAllText(path);
        if (!code.Contains("==UserScript==")) throw new IOException("Заголовок userscript в файле не найден.");

        ShowTampermonkeyView();
        string dashboard = "chrome-extension://" + ExtensionId + "/options.html#nav=dashboard";
        web.CoreWebView2.Navigate(dashboard);
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(15)) {
            if (IsDisposed) return;
            if (IsTampermonkeyPage(web.Source, "/options.html") &&
                await web.CoreWebView2.ExecuteScriptAsync("typeof window.sendMessage === 'function' && !!chrome.runtime.connect") == "true")
                break;
            await Task.Delay(150);
        }
        if (wait.Elapsed >= TimeSpan.FromSeconds(15)) throw new IOException("Панель Tampermonkey не загрузилась.");

        status.Text = "Подтвердите установку в окне Tampermonkey.";
        var serializer = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
        const string import = @"(code => {
            if (location.protocol !== 'chrome-extension:' || location.host !== '" + ExtensionId + @"' ||
                location.pathname !== '/options.html') return false;
            window.dukhiImportResult = null;
            const port = chrome.runtime.connect({name: 'importEx'});
            port.onDisconnect.addListener(() => {
                if (!window.dukhiImportResult) window.dukhiImportResult = {error: 'Tampermonkey прервал импорт.'};
            });
            port.onMessage.addListener(result => {
                if (result.progress) return;
                window.dukhiImportResult = {installed: result.installed || 0, error: result.error || null};
                port.disconnect();
            });
            port.postMessage({method: 'importEx', code, reload: false});
            return true;
        })";
        string started = await web.CoreWebView2.ExecuteScriptAsync(import + "(" + serializer.Serialize(code) + ")");
        if (started != "true") throw new IOException("Не удалось начать импорт скрипта.");

        wait.Restart();
        while (wait.Elapsed < TimeSpan.FromMinutes(5)) {
            await Task.Delay(250);
            if (IsDisposed) return;
            if (!IsTampermonkeyPage(web.Source, "/options.html"))
                throw new IOException("Панель Tampermonkey закрылась до завершения импорта.");
            string response = await web.CoreWebView2.ExecuteScriptAsync("window.dukhiImportResult || null");
            if (response == "null") continue;
            var result = serializer.Deserialize<ScriptImportResult>(response);
            if (!String.IsNullOrEmpty(result.error)) throw new IOException(result.error);
            if (result.installed == 0) {
                status.Text = "Установка скрипта отменена.";
                return;
            }
            web.CoreWebView2.Reload();
            status.Text = "Скрипт установлен.";
            return;
        }
        throw new IOException("Tampermonkey не подтвердил установку. Проверьте список скриптов.");
    }
    async Task Dashboard() {
        var list=await web.CoreWebView2.Profile.GetBrowserExtensionsAsync();var ext=list.FirstOrDefault(x=>x.Id==ExtensionId);
        if(ext==null){status.Text="Сначала установите Tampermonkey.";return;}
        ShowTampermonkeyView();
        web.CoreWebView2.Navigate("chrome-extension://"+ext.Id+"/options.html");
        status.Text="Панель Tampermonkey";
    }
    async Task RefreshClientVersion(bool prepareMissingCopy) {
        try {
            var version = await Task.Run(() => clientUpdate.Inspect());
            ShowClientVersion(version);
            if (!prepareMissingCopy || version.LocalVersion != null || version.OfficialVersion == null) return;
            status.Text = "Подготовка локальной копии AnimeSSS…";
            await Task.Run(() => clientUpdate.EnsureCurrent());
            if (IsDisposed) return;
            ShowClientVersion(await Task.Run(() => clientUpdate.Inspect()));
            status.Text = "Локальная копия AnimeSSS готова.";
        } catch (Exception error) {
            if (IsDisposed) return;
            clientCompatibility.Text = "Не удалось подготовить локальную копию.";
            status.Text = error.Message;
        }
    }

    async Task LaunchUpdatedClient() {
        status.Text = "Проверка версии AnimeSSS…";
        Exception failure = null;
        try {
            await Task.Run(() => clientUpdate.EnsureCurrent());
        } catch (Exception error) {
            failure = error;
        }
        if (failure != null) {
            if (IsDisposed) return;
            var version = await Task.Run(() => clientUpdate.Inspect());
            if (IsDisposed) return;
            ShowClientVersion(version);
            clientCompatibility.Text = "Не удалось применить патч.";
            status.Text = failure.Message;
            if (version.LocalVersion == null) {
                MessageBox.Show(this, failure.Message, "Не удалось подготовить AnimeSSS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string message = "Не удалось обновить локальную копию.\n\n" + failure.Message
                + "\n\nОткрыть совместимую копию " + version.LocalVersion + "?";
            if (MessageBox.Show(this, message, "Обновление AnimeSSS", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        }
        if (!IsDisposed) LaunchClient();
    }

    void LaunchClient() {
        string exe=clientUpdate.LocalPath;
        if(!File.Exists(exe))throw new IOException("Модифицированный клиент не найден.");
        bool useProbeProfile = args.Length > 0 && args[0].StartsWith("--probe", StringComparison.Ordinal)
            && args[0] != "--probe-handoff-natural";
        string wait = Process.GetCurrentProcess().Id + "," + web.CoreWebView2.BrowserProcessId;
        string launchProfile = useProbeProfile ? profile : "-";
        string arguments = "--launch-wait " + wait + " \"" + exe + "\" \"" + launchProfile + "\"";
        Process.Start(new ProcessStartInfo(Application.ExecutablePath, arguments) { UseShellExecute = false });
        foreach (var popup in popups.ToArray()) popup.Dispose();
        web.Dispose();
        Close();
    }
    Process FindRunningClient() {
        string bundled = Path.GetFullPath(clientUpdate.LocalPath);
        foreach(var candidate in Process.GetProcessesByName("animesss-desktop")) {
            try {
                string runningPath=Path.GetFullPath(candidate.MainModule.FileName);
                if (String.Equals(runningPath, bundled, StringComparison.OrdinalIgnoreCase)
                    || System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(runningPath))
                        .IndexOf("com.animesss.modktop", StringComparison.Ordinal) >= 0) return candidate;
            }catch{}
            candidate.Dispose();
        }
        return null;
    }
}
