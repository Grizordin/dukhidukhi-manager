using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

public partial class ExtensionManager {
    const string ManagerName = "Духи Духи менеджер";
    static readonly Color Ink = Color.FromArgb(246, 238, 233);
    static readonly Color Muted = Color.FromArgb(193, 174, 187);
    static readonly Color Gold = Color.FromArgb(237, 191, 107);
    static readonly Color Card = Color.FromArgb(32, 26, 39);
    static readonly Color Line = Color.FromArgb(73, 55, 75);

    readonly Panel managerHome = new Panel { Dock = DockStyle.Fill };
    readonly Panel tampermonkeyView = new Panel { Dock = DockStyle.Fill, Visible = false };
    Label tampermonkeyState;
    Button tampermonkeyDashboard;
    Button tampermonkeyImport;
    Button tampermonkeyInstall;
    Label blockerState;
    Button blockerInstall;
    Button blockerRemove;
    Label clientVersions;
    Label clientCompatibility;
    Image brandImage;
    string BlockerStorage { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnimeSSS.Local", "AdBlocker"); } }
    string BlockerVersionFile { get { return Path.Combine(BlockerStorage, "installed-version.txt"); } }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        int dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        int backdrop = 0x211920;
        DwmSetWindowAttribute(Handle, 35, ref backdrop, sizeof(int));
        int caption = 0xeeeef6;
        DwmSetWindowAttribute(Handle, 36, ref caption, sizeof(int));
    }

    void BuildExtensionUI() {
        Text = ManagerName;
        ClientSize = new Size(930, 674);
        MinimumSize = new Size(680, 570);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(19, 16, 25);
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10F);
        string iconPath = Path.Combine(root, "app", "app-icon.ico");
        if (File.Exists(iconPath)) {
            Icon = new Icon(iconPath);
            brandImage = ReadBrandImage(iconPath);
            Disposed += delegate { if (brandImage != null) brandImage.Dispose(); };
        }

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(24, 20, 24, 16), BackColor = BackColor,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 145));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(BuildHeader(), 0, 0);

        var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        body.Controls.Add(tampermonkeyView);
        body.Controls.Add(managerHome);
        layout.Controls.Add(body, 0, 1);

        status.Dock = DockStyle.Fill;
        status.ForeColor = Muted;
        status.Font = new Font("Segoe UI", 9F);
        status.Padding = new Padding(6, 8, 6, 0);
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.Text = "Подключение к профилю AnimeSSS…";
        status.AutoEllipsis = true;
        layout.Controls.Add(status, 0, 2);
        Controls.Add(layout);

        managerHome.Enabled = false;
        managerHome.AutoScroll = true;
        var cards = new TableLayoutPanel {
            Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 5, 0, 0),
        };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        managerHome.Controls.Add(cards);

        var tampermonkey = CreateCard(cards, "TM", Color.FromArgb(220, 138, 209),
            "Tampermonkey", "Пользовательские скрипты для AnimeSSS", 147, out tampermonkeyState);
        tampermonkeyInstall = ActionButton("Установить", Install, true);
        tampermonkeyDashboard = ActionButton("Открыть панель", Dashboard);
        tampermonkeyImport = ActionButton("Добавить .user.js", InstallScript);
        tampermonkey.Controls.AddRange(new Control[] { tampermonkeyInstall, tampermonkeyDashboard, tampermonkeyImport });

        var blocker = CreateCard(cards, "AD", Gold, "Блокировщик рекламы",
            "Фильтрует рекламные запросы, в том числе перед роликами", 147, out blockerState);
        blockerInstall = ActionButton("Установить", InstallOrUpdateBlocker, true);
        blockerRemove = ActionButton("Удалить", RemoveBlocker);
        blocker.Controls.AddRange(new Control[] { blockerInstall, blockerRemove });
        AddShortcuts(cards);

        var viewHeader = new FlowLayoutPanel {
            Dock = DockStyle.Top, Height = 54, Padding = new Padding(0, 6, 0, 0),
            BackColor = BackColor,
        };
        viewHeader.Controls.Add(ActionButton("‹  В менеджер", delegate {
            tampermonkeyView.Visible = false;
            managerHome.Visible = true;
            managerHome.BringToFront();
            status.Text = "";
            return Task.FromResult(0);
        }));
        viewHeader.Controls.Add(new Label {
            Text = "Tampermonkey · пользовательские скрипты", ForeColor = Muted,
            AutoSize = true, Margin = new Padding(12, 9, 0, 0),
        });
        web.Dock = DockStyle.Fill;
        web.DefaultBackgroundColor = BackColor;
        tampermonkeyView.Controls.Add(web);
        tampermonkeyView.Controls.Add(viewHeader);
    }

    Control BuildHeader() {
        var header = new RoundedPanel {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), Radius = 19,
            Surface = Color.FromArgb(64, 31, 56), End = Color.FromArgb(29, 23, 42),
            Border = Color.FromArgb(112, 65, 90),
        };
        if (brandImage != null) header.Controls.Add(new PictureBox {
            Location = new Point(22, 26), Size = new Size(48, 48),
            Image = brandImage, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent,
        });
        header.Controls.Add(Headline("Духи Духи", 83, 14, 23F, Ink));
        header.Controls.Add(Headline("Менеджер AnimeSSS · " + ManagerUpdate.VersionText, 85, 57, 10F, Gold));
        clientVersions = Headline("Версии AnimeSSS проверяются…", 22, 85, 9.5F, Ink);
        clientCompatibility = Headline("", 22, 105, 9F, Muted);
        header.Controls.Add(clientVersions);
        header.Controls.Add(clientCompatibility);
        var play = ActionButton("Открыть AnimeSSS   ↗", LaunchUpdatedClient, true);
        play.Enabled = false;
        play.Size = new Size(178, 42);
        header.Resize += (sender, e) => { play.Location = new Point(header.ClientSize.Width - play.Width - 22, 31); };
        header.Controls.Add(play);
        play.Location = new Point(header.ClientSize.Width - play.Width - 22, 31);
        managerHome.EnabledChanged += (sender, e) => { play.Enabled = managerHome.Enabled; };
        return header;
    }

    void ShowClientVersion(ClientUpdate.State version) {
        if (IsDisposed) return;
        clientVersions.Text = "Установлен AnimeSSS: " + (version.OfficialVersion ?? "не найден")
            + "    ·    Совместимая копия: " + (version.LocalVersion ?? "ещё не создана");
        clientCompatibility.Text = version.UpToDate ? "Патч для установленной версии готов."
            : version.OfficialVersion == null ? "Установите официальный AnimeSSS, чтобы получать обновления."
            : "Совместимость установленной версии проверяется перед запуском.";
    }

    static Image ReadBrandImage(string iconPath) {
        using (var stream = File.OpenRead(iconPath))
        using (var reader = new BinaryReader(stream)) {
            if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1) return null;
            int count = reader.ReadUInt16();
            for (int i = 0; i < count; i++) {
                int size = reader.ReadByte();
                reader.ReadBytes(7);
                uint length = reader.ReadUInt32();
                uint offset = reader.ReadUInt32();
                if (size != 64) continue;
                if (length > 1024 * 1024 || offset + (long)length > stream.Length) return null;
                stream.Position = offset;
                using (var png = new MemoryStream(reader.ReadBytes((int)length)))
                using (var image = Image.FromStream(png)) return new Bitmap(image);
            }
        }
        return null;
    }

    FlowLayoutPanel CreateCard(TableLayoutPanel cards, string mark, Color accent,
        string title, string description, int height, out Label state) {
        var card = new RoundedPanel {
            Dock = DockStyle.Fill, Height = height, Radius = 16,
            Margin = new Padding(0, 0, 0, 12), Surface = Card, End = Card, Border = Line,
        };
        var badge = new RoundedPanel {
            Location = new Point(18, 16), Size = new Size(43, 42), Radius = 11,
            Surface = Color.FromArgb(62, 45, 67), End = Color.FromArgb(62, 45, 67),
            Border = Color.FromArgb(96, 65, 100),
        };
        badge.Controls.Add(new Label {
            Text = mark, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Semibold", 12F), ForeColor = accent, BackColor = Color.Transparent,
        });
        card.Controls.Add(badge);
        card.Controls.Add(Headline(title, 73, 13, 15F, Ink));
        card.Controls.Add(Headline(description, 75, 42, 9.5F, Muted));
        state = new Label {
            Size = new Size(194, 23), Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Gold, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleRight,
            Text = "Загрузка…", AutoEllipsis = true,
        };
        var statusLabel = state;
        card.Resize += (sender, e) => { statusLabel.Location = new Point(card.ClientSize.Width - statusLabel.Width - 20, 16); };
        card.Controls.Add(statusLabel);
        statusLabel.Location = new Point(card.ClientSize.Width - statusLabel.Width - 20, 16);
        var actions = new FlowLayoutPanel {
            Location = new Point(17, 82), Height = height - 90, BackColor = Color.Transparent,
            WrapContents = true, Margin = Padding.Empty,
        };
        card.Resize += (sender, e) => { actions.Width = Math.Max(0, card.ClientSize.Width - 34); };
        card.Controls.Add(actions);
        cards.Controls.Add(card);
        actions.Width = Math.Max(0, card.ClientSize.Width - 34);
        return actions;
    }

    static Label Headline(string text, int x, int y, float size, Color color) {
        return new Label {
            Text = text, AutoSize = true, Location = new Point(x, y), ForeColor = color,
            Font = new Font("Segoe UI Semibold", size), BackColor = Color.Transparent,
        };
    }

    void AddShortcuts(TableLayoutPanel cards) {
        var hints = new RoundedPanel {
            Dock = DockStyle.Fill, Margin = new Padding(0), Height = 128, Radius = 14,
            Surface = Color.FromArgb(27, 23, 33), End = Color.FromArgb(27, 23, 33), Border = Line,
        };
        hints.Controls.Add(Headline("Горячие клавиши", 18, 10, 11F, Ink));
        hints.Controls.Add(Headline("Ctrl + Shift + Y", 18, 40, 9F, Gold));
        hints.Controls.Add(Headline("Переключить скрипты и обновить вкладку", 141, 40, 9F, Muted));
        hints.Controls.Add(Headline("Ctrl + Shift + U", 18, 66, 9F, Gold));
        hints.Controls.Add(Headline("Закрыть AnimeSSS и вернуться в менеджер", 141, 66, 9F, Muted));
        hints.Controls.Add(Headline("F12", 18, 92, 9F, Gold));
        hints.Controls.Add(Headline("Открыть или закрыть инспектор страницы", 141, 92, 9F, Muted));
        cards.Controls.Add(hints);
    }

    Button ActionButton(string label, Func<Task> action, bool primary = false) {
        var button = new RoundedButton {
            Text = label, AutoSize = true, Height = 37, MinimumSize = new Size(100, 37),
            Font = new Font("Segoe UI Semibold", 9F), Primary = primary,
            Margin = new Padding(0, 0, 9, 8), Padding = new Padding(11, 0, 11, 0),
            Cursor = Cursors.Hand, UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat,
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += async delegate {
            if (busy || managerHome.Enabled == false) return;
            busy = true;
            UseWaitCursor = true;
            try { await action(); }
            catch (Exception error) {
                if (!IsDisposed) status.Text = error.Message;
            } finally {
                busy = false;
                if (!IsDisposed) UseWaitCursor = false;
            }
        };
        return button;
    }

    void ShowTampermonkeyView() {
        managerHome.Visible = false;
        tampermonkeyView.Visible = true;
        tampermonkeyView.BringToFront();
    }

    async Task RefreshExtensionList() {
        var extensions = await web.CoreWebView2.Profile.GetBrowserExtensionsAsync();
        var tampermonkey = extensions.FirstOrDefault(extension => extension.Id == ExtensionId);
        tampermonkeyState.Text = tampermonkey == null ? "Не установлен"
            : tampermonkey.IsEnabled ? "Установлен" : "Отключён";
        tampermonkeyInstall.Text = tampermonkey == null ? "Установить" : "Обновить";
        tampermonkeyDashboard.Enabled = tampermonkey != null && tampermonkey.IsEnabled;
        tampermonkeyImport.Enabled = tampermonkeyDashboard.Enabled;
        var blocker = extensions.FirstOrDefault(extension => extension.Id == AdBlocker.Id);
        string version = blocker == null || !File.Exists(BlockerVersionFile) ? "" :
            (File.ReadLines(BlockerVersionFile).FirstOrDefault() ?? "").Trim();
        blockerState.Text = blocker == null ? "Не установлен" : !blocker.IsEnabled ? "Отключён" :
            String.IsNullOrEmpty(version) ? "Установлен" : version;
        blockerInstall.Text = blocker == null ? "Установить" : "Обновить фильтры";
        blockerRemove.Enabled = blocker != null;
    }

    async Task InstallOrUpdateBlocker() {
        var extensions = await web.CoreWebView2.Profile.GetBrowserExtensionsAsync();
        var current = extensions.FirstOrDefault(extension => extension.Id == AdBlocker.Id);
        AdBlocker.Filters filters;
        if (current == null) {
            status.Text = "Установка блокировщика…";
            filters = AdBlocker.Bundled(root);
        } else {
            status.Text = "Проверка обновлений фильтров на GitHub…";
            filters = await AdBlocker.Latest();
            string[] installedInfo = File.Exists(BlockerVersionFile) ? File.ReadAllLines(BlockerVersionFile) : new string[0];
            Version installed;
            var offered = new Version(filters.Version);
            bool currentRules = installedInfo.Length >= 2 && installedInfo[1] == AdBlocker.VideoRulesRevision;
            bool keepInstalled = installedInfo.Length > 0 && Version.TryParse(installedInfo[0].Trim(), out installed) &&
                (installed > offered || (installed == offered && currentRules));
            if (keepInstalled) {
                if (!current.IsEnabled) {
                    await current.EnableAsync(true);
                    await RefreshExtensionList();
                }
                status.Text = "Установлены актуальные фильтры.";
                return;
            }
        }
        string folder = await Task.Run(() => AdBlocker.Prepare(filters, BlockerStorage));
        var installedBlocker = await web.CoreWebView2.Profile.AddBrowserExtensionAsync(folder);
        if (installedBlocker.Id != AdBlocker.Id) throw new IOException("WebView2 вернул неожиданный ID блокировщика.");
        File.WriteAllLines(BlockerVersionFile, new[] { filters.Version, AdBlocker.VideoRulesRevision });
        await RefreshExtensionList();
        status.Text = "Фильтры блокировщика · " + filters.Version;
    }

    async Task RemoveBlocker() {
        var extensions = await web.CoreWebView2.Profile.GetBrowserExtensionsAsync();
        var blocker = extensions.FirstOrDefault(extension => extension.Id == AdBlocker.Id);
        if (blocker == null) return;
        await blocker.RemoveAsync();
        File.Delete(BlockerVersionFile);
        await RefreshExtensionList();
        status.Text = "Блокировщик удалён.";
    }

    static bool IsExtensionStore(string address) {
        Uri uri;
        return Uri.TryCreate(address, UriKind.Absolute, out uri) &&
            (uri.Host == "chromewebstore.google.com" ||
             (uri.Host == "chrome.google.com" && uri.AbsolutePath.StartsWith("/webstore", StringComparison.OrdinalIgnoreCase)));
    }

    static GraphicsPath RoundedShape(Rectangle bounds, int radius) {
        int size = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        if (size < 2) { path.AddRectangle(bounds); return path; }
        path.AddArc(bounds.Left, bounds.Top, size, size, 180, 90);
        path.AddArc(bounds.Right - size, bounds.Top, size, size, 270, 90);
        path.AddArc(bounds.Right - size, bounds.Bottom - size, size, size, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - size, size, size, 90, 90);
        path.CloseFigure();
        return path;
    }

    sealed class RoundedPanel : Panel {
        public Color Surface, End, Border;
        public int Radius;

        public RoundedPanel() {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e) {
            if (Width < 2 || Height < 2) return;
            var bounds = ClientRectangle;
            bounds.Width--;
            bounds.Height--;
            e.Graphics.Clear(Parent == null ? Color.FromArgb(19, 16, 25) : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = RoundedShape(bounds, Radius))
            using (var brush = new LinearGradientBrush(bounds, Surface, End, 16F))
            using (var pen = new Pen(Border)) {
                e.Graphics.FillPath(brush, shape);
                e.Graphics.DrawPath(pen, shape);
            }
        }
    }

    sealed class RoundedButton : Button {
        public bool Primary;
        bool hovered;

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e) {
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = Enabled && Primary ? (hovered ? Color.FromArgb(251, 213, 144) : Gold)
                : hovered && Enabled ? Color.FromArgb(76, 49, 79) : Color.FromArgb(53, 38, 59);
            Color text = !Enabled ? Color.FromArgb(142, 124, 140)
                : Primary ? Color.FromArgb(43, 26, 31) : Ink;
            using (var shape = RoundedShape(bounds, 9))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Focused ? Gold : Primary && Enabled ? Gold : Color.FromArgb(102, 67, 98))) {
                e.Graphics.FillPath(brush, shape);
                e.Graphics.DrawPath(pen, shape);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
