using System.Net;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace KeyTranslate;

internal sealed class MainForm : Form
{
    private const int HotkeyId = 1001;
    private const int AutoToggleHotkeyId = 1002;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint KeyUpFlag = 0x0002;
    private const string SettingsFile = "keytranslate.json";
    private const string TranslationCacheFile = "translation-cache.json";
    private const string LanguagePlaceholder = "Choose a language...";
    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "KeyTranslate";
    private readonly ComboBox sourceBox = new();
    private readonly ComboBox targetBox = new();
    private readonly TextBox hotkeyBox = new();
    private readonly CheckBox startupCheckBox = new();
    private readonly Button autoToggleButton = new();
    private readonly Label status = new();
    private readonly Label statusDot = new();
    private readonly Button saveButton = new();
    private readonly NotifyIcon trayIcon = new();
    private readonly ContextMenuStrip trayMenu = new();
    private readonly ToolStripMenuItem autoTranslateMenuItem = new("Automatic translation");
    private readonly ToolStripMenuItem delayMenu = new("Auto-translate delay");
    private readonly System.Windows.Forms.Timer idleTimer = new();
    private readonly LowLevelKeyboardProc keyboardProc;
    private IntPtr keyboardHook;
    private readonly string[] languages = ["Auto-detect", "English", "Russian", "Ukrainian", "German", "French", "Spanish", "Polish", "Italian", "Portuguese", "Japanese", "Chinese"];
    private readonly Dictionary<string, string> codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Auto-detect"] = "autodetect",
        ["English"] = "en",
        ["Russian"] = "ru",
        ["Ukrainian"] = "uk",
        ["German"] = "de",
        ["French"] = "fr",
        ["Spanish"] = "es",
        ["Polish"] = "pl",
        ["Italian"] = "it",
        ["Portuguese"] = "pt",
        ["Japanese"] = "ja",
        ["Chinese"] = "zh"
    };
    private Settings settings = new();
    private bool busy;
    private bool exitRequested;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly Dictionary<string, string> translationCache = new(StringComparer.Ordinal);
    private readonly object cacheLock = new();

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? name);
    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { public uint VkCode; public uint ScanCode; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;

    public MainForm()
    {
        settings = LoadSettings();
        LoadTranslationCache();
        Text = "KeyTranslate";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(650, 580);
        ClientSize = new Size(720, 650);
        BackColor = Color.FromArgb(245, 247, 251);
        Font = new Font("Segoe UI", 10);
        BuildUi();
        SetupTrayIcon();
        keyboardProc = KeyboardHookCallback;
        keyboardHook = SetWindowsHookEx(WhKeyboardLl, keyboardProc, GetModuleHandle(null), 0);
        FormClosing += HandleFormClosing;
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) HideToTray(); };
        idleTimer.Interval = Math.Max(1, settings.DelaySeconds) * 1000;
        idleTimer.Tick += async (_, _) => { idleTimer.Stop(); if (settings.AutoTranslate) await TranslateSelectionAsync(true); };
    }

    private void BuildUi()
    {
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 3, BackColor = Color.FromArgb(245, 247, 251) };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 78)); outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); Controls.Add(outer);
        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label { Text = "KeyTranslate", Font = new Font("Segoe UI", 25, FontStyle.Bold), ForeColor = Color.FromArgb(21, 33, 59), AutoSize = true, Location = new Point(0, 0) });
        header.Controls.Add(new Label { Text = "Fast translation for selected text · local cache first", ForeColor = Color.FromArgb(100, 112, 138), AutoSize = true, Location = new Point(2, 45) }); outer.Controls.Add(header, 0, 0);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, Padding = new Padding(0, 6, 0, 12) }; outer.Controls.Add(content, 0, 1);
        var settingsCard = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(22), ColumnCount = 1, RowCount = 11 };
        for (var i = 0; i < 11; i++) settingsCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        settingsCard.Controls.Add(CardTitle("TRANSLATION SETTINGS"), 0, 0);
        settingsCard.Controls.Add(FieldLabel("From language"), 0, 1); ConfigureCombo(sourceBox, settings.Source, Point.Empty, 0, allowAutoDetect: true); sourceBox.Dock = DockStyle.Fill; sourceBox.Margin = new Padding(0, 4, 0, 12); settingsCard.Controls.Add(sourceBox, 0, 2);
        settingsCard.Controls.Add(FieldLabel("To language"), 0, 3); ConfigureCombo(targetBox, settings.Target, Point.Empty, 0); targetBox.Dock = DockStyle.Fill; targetBox.Margin = new Padding(0, 4, 0, 12); settingsCard.Controls.Add(targetBox, 0, 4);
        settingsCard.Controls.Add(FieldLabel("Manual hotkey"), 0, 5); hotkeyBox.Text = settings.Hotkey; hotkeyBox.Dock = DockStyle.Fill; hotkeyBox.Margin = new Padding(0, 4, 0, 12); hotkeyBox.Font = new Font("Segoe UI", 11); settingsCard.Controls.Add(hotkeyBox, 0, 6);
        var delayPanel = new Panel { Dock = DockStyle.Fill, Height = 34 };
        delayPanel.Controls.Add(FieldLabel("Auto delay (seconds)"));
        var delayBox = new NumericUpDown { Minimum = 1, Maximum = 30, Value = settings.DelaySeconds, Width = 70, Location = new Point(230, 0) };
        delayBox.ValueChanged += (_, _) => settings.DelaySeconds = (int)delayBox.Value;
        delayPanel.Controls.Add(delayBox);
        settingsCard.Controls.Add(delayPanel, 0, 7);
        autoToggleButton.Text = "AutoTranslate"; autoToggleButton.Dock = DockStyle.Fill; autoToggleButton.Height = 38; autoToggleButton.Margin = new Padding(0, 14, 0, 8); autoToggleButton.FlatStyle = FlatStyle.Flat; autoToggleButton.FlatAppearance.BorderColor = Color.FromArgb(190, 202, 230); autoToggleButton.Click += (_, _) => ToggleAutoTranslate(); UpdateAutoButton(); settingsCard.Controls.Add(autoToggleButton, 0, 8);
        startupCheckBox.Text = "Run KeyTranslate when Windows starts";
        startupCheckBox.Checked = settings.RunAtStartup;
        startupCheckBox.AutoSize = true;
        startupCheckBox.Margin = new Padding(0, 8, 0, 10);
        settingsCard.Controls.Add(startupCheckBox, 0, 9);
        saveButton.Text = "Save settings"; saveButton.Dock = DockStyle.Fill; saveButton.Height = 38; saveButton.BackColor = Color.FromArgb(36, 87, 230); saveButton.ForeColor = Color.White; saveButton.FlatStyle = FlatStyle.Flat; saveButton.FlatAppearance.BorderSize = 0; saveButton.Click += (_, _) => SaveSettingsAndHotkey(); settingsCard.Controls.Add(saveButton, 0, 10); content.Controls.Add(settingsCard, 0, 0);

        var statusPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(234, 240, 255), Padding = new Padding(16, 12, 16, 8) };
        statusDot.Text = "●";
        statusDot.ForeColor = Color.FromArgb(47, 158, 104);
        statusDot.AutoSize = true;
        statusDot.Font = new Font("Segoe UI", 14, FontStyle.Bold);
        statusDot.Location = new Point(16, 10);
        status.Text = string.IsNullOrWhiteSpace(settings.Source) || string.IsNullOrWhiteSpace(settings.Target)
            ? "Choose source and target languages, then save settings"
            : $"Ready · press {settings.Hotkey} after selecting text";
        status.ForeColor = Color.FromArgb(41, 65, 108);
        status.AutoSize = true;
        status.Location = new Point(40, 14);
        statusPanel.Controls.Add(statusDot);
        statusPanel.Controls.Add(status);
        outer.Controls.Add(statusPanel, 0, 2);
    }

    private Label CardTitle(string text) => new() { Text = text, ForeColor = Color.FromArgb(135, 145, 167), Font = new Font("Segoe UI", 9, FontStyle.Bold), Dock = DockStyle.Fill, Height = 28 };
    private Label FieldLabel(string text) => new() { Text = text, ForeColor = Color.FromArgb(83, 97, 122), Dock = DockStyle.Fill, Height = 25, Padding = new Padding(0, 5, 0, 0) };

    private void ConfigureCombo(ComboBox box, string value, Point location, int width, bool allowAutoDetect = false)
    {
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Items.Add(LanguagePlaceholder);
        box.Items.AddRange(allowAutoDetect ? languages : languages[1..]);
        box.SelectedItem = box.Items.Contains(value) ? value : LanguagePlaceholder;
        box.Location = location;
        box.Width = width;
        box.Height = 30;
    }

    private void SetupTrayIcon()
    {
        trayMenu.Items.Add("Open settings", null, (_, _) => ShowFromTray());
        autoTranslateMenuItem.CheckOnClick = true;
        autoTranslateMenuItem.Checked = settings.AutoTranslate;
        autoTranslateMenuItem.Click += (_, _) => ToggleAutoTranslate();
        trayMenu.Items.Add(autoTranslateMenuItem);
        trayMenu.Items.Add(delayMenu);
        foreach (var seconds in new[] { 1, 2, 3, 5, 10, 15, 20, 30 })
        {
            var item = new ToolStripMenuItem($"{seconds} seconds") { Tag = seconds, Checked = settings.DelaySeconds == seconds };
            item.Click += (_, _) => SetDelay((int)item.Tag!);
            delayMenu.DropDownItems.Add(item);
        }
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit KeyTranslate", null, (_, _) => ExitApplication());
        trayIcon.Icon = SystemIcons.Application;
        trayIcon.Text = "KeyTranslate · translation is active";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.DoubleClick += (_, _) => ShowFromTray();
        trayIcon.Visible = true;
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!exitRequested)
        {
            e.Cancel = true;
            HideToTray();
        }
        else
        {
            if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
            trayIcon.Visible = false;
            trayIcon.Dispose();
            UnregisterHotKey(Handle, HotkeyId); UnregisterHotKey(Handle, AutoToggleHotkeyId);
        }
    }

    private void HideToTray()
    {
        Hide();
        trayIcon.ShowBalloonTip(1200, "KeyTranslate is running", $"Press {settings.Hotkey} after selecting text to translate it.", ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        exitRequested = true;
        Close();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryRegisterHotkey();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId) _ = TranslateSelectionAsync();
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == AutoToggleHotkeyId) ToggleAutoTranslate();
        base.WndProc(ref m);
    }

    private void TryRegisterHotkey()
    {
        try
        {
            var (modifiers, key) = ParseHotkey(settings.Hotkey);
            if (!RegisterHotKey(Handle, HotkeyId, modifiers, key))
                SetStatus($"{settings.Hotkey} is already in use by another app.", false);

            if (!RegisterHotKey(Handle, AutoToggleHotkeyId, 0, 0x76))
                SetStatus("F7 is already in use by another app.", false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, false);
        }
    }

    private void SaveSettingsAndHotkey()
    {
        try
        {
            ParseHotkey(hotkeyBox.Text);
            if (sourceBox.SelectedItem is not string source || source == LanguagePlaceholder)
                throw new InvalidOperationException("Choose a source language.");

            if (targetBox.SelectedItem is not string target || target == LanguagePlaceholder)
                throw new InvalidOperationException("Choose a target language.");

            settings.Source = source;
            settings.Target = target;
            settings.Hotkey = hotkeyBox.Text.Trim();
            settings.RunAtStartup = startupCheckBox.Checked;
            ApplyStartupSetting();
            SaveSettings();
            idleTimer.Interval = settings.DelaySeconds * 1000;
            UpdateDelayMenu();
            UpdateAutoButton();
            UnregisterHotKey(Handle, HotkeyId);
            UnregisterHotKey(Handle, AutoToggleHotkeyId);
            TryRegisterHotkey();
            SetStatus(settings.AutoTranslate
                ? $"Ready · auto-translate is on · {settings.DelaySeconds} sec pause"
                : $"Ready · press {settings.Hotkey} after selecting text", true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "KeyTranslate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyStartupSetting()
    {
        using var startupKey = Registry.CurrentUser.CreateSubKey(StartupRegistryKey);

        if (startupKey is null)
            throw new InvalidOperationException("Could not access the Windows startup settings.");

        if (Environment.ProcessPath is not { Length: > 0 } executablePath)
            throw new InvalidOperationException("Could not determine the KeyTranslate executable path.");

        if (settings.RunAtStartup)
        {
            startupKey.SetValue(StartupValueName, $"\"{executablePath}\"", RegistryValueKind.String);
        }
        else
        {
            startupKey.DeleteValue(StartupValueName, false);
        }
    }

    private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        var key = code >= 0 ? Marshal.PtrToStructure<KeyboardData>(lParam).VkCode : 0;
        var isTypingKey = key is >= 0x30 and <= 0x5A or >= 0xBA and <= 0xE2 or 0x08 or 0x20 or 0x0D or 0x09;
        if (code >= 0 && (wParam.ToInt32() == WmKeyDown || wParam.ToInt32() == WmSysKeyDown) && isTypingKey && GetForegroundWindow() != Handle && !busy && settings.AutoTranslate && !exitRequested)
        {
            try { BeginInvoke(NoteTyping); } catch { }
        }
        return CallNextHookEx(keyboardHook, code, wParam, lParam);
    }

    private void NoteTyping()
    {
        if (!busy && settings.AutoTranslate) { idleTimer.Stop(); idleTimer.Start(); SetStatus($"Typing detected · translating after {settings.DelaySeconds} sec pause", null); }
    }

    private void ToggleAutoTranslate()
    {
        settings.AutoTranslate = !settings.AutoTranslate;
        if (!settings.AutoTranslate) idleTimer.Stop();
        SaveSettings();
        UpdateAutoButton();
        SetStatus(settings.AutoTranslate ? $"Auto-translate on · waits {settings.DelaySeconds} sec after typing" : $"Auto-translate off · press {settings.Hotkey} after highlighting text", true);
    }

    private void UpdateAutoButton()
    {
        autoToggleButton.Text = settings.AutoTranslate ? "Auto-translate: ON" : "Auto-translate: OFF";
        autoToggleButton.BackColor = settings.AutoTranslate ? Color.FromArgb(234, 240, 255) : Color.White;
        autoTranslateMenuItem.Checked = settings.AutoTranslate;
    }

    private void SetDelay(int seconds)
    {
        settings.DelaySeconds = Math.Clamp(seconds, 1, 30);
        idleTimer.Interval = settings.DelaySeconds * 1000;
        SaveSettings();
        UpdateDelayMenu();
        SetStatus($"Auto-translate delay set to {settings.DelaySeconds} sec", true);
    }

    private void UpdateDelayMenu()
    {
        foreach (ToolStripMenuItem item in delayMenu.DropDownItems)
            item.Checked = (int)item.Tag! == settings.DelaySeconds;
    }

    private void SaveSettings() => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, SettingsFile), JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

    private void LoadTranslationCache()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, TranslationCacheFile);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (loaded is null) return;
            lock (cacheLock)
            {
                foreach (var pair in loaded)
                    translationCache[pair.Key] = pair.Value;
            }
        }
        catch
        {
        }
    }

    private void SaveTranslationCache()
    {
        try
        {
            Dictionary<string, string> snapshot;
            lock (cacheLock) snapshot = new(translationCache);
            var path = Path.Combine(AppContext.BaseDirectory, TranslationCacheFile);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, true);
        }
        catch
        {
        }
    }

    private async Task TranslateSelectionAsync(bool autoSelect = false)
    {
        if (busy)
            return;

        if (string.IsNullOrWhiteSpace(settings.Source) || string.IsNullOrWhiteSpace(settings.Target))
        {
            SetStatus("Choose source and target languages, then save settings.", false);
            return;
        }

        busy = true;
        var previousClipboard = string.Empty;
        var clipboardWasChanged = false;
        SetStatus("Translating selection…", null);

        try
        {
            var sourceWindow = GetForegroundWindow();
            await WaitForHotkeyReleaseAsync();
            if (sourceWindow != IntPtr.Zero && sourceWindow != Handle) SetForegroundWindow(sourceWindow);
            await Task.Delay(60);
            if (autoSelect)
            {
                keybd_event(0x10, 0, 0, UIntPtr.Zero);
                keybd_event(0x24, 0, 0, UIntPtr.Zero);
                keybd_event(0x24, 0, KeyUpFlag, UIntPtr.Zero);
                keybd_event(0x10, 0, KeyUpFlag, UIntPtr.Zero);
                await Task.Delay(100);
            }

            previousClipboard = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            Clipboard.Clear();
            clipboardWasChanged = true;
            keybd_event(0x11, 0, 0, UIntPtr.Zero);
            keybd_event(0x43, 0, 0, UIntPtr.Zero);
            keybd_event(0x43, 0, KeyUpFlag, UIntPtr.Zero);
            keybd_event(0x11, 0, KeyUpFlag, UIntPtr.Zero);
            await Task.Delay(140);

            var selected = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            if (string.IsNullOrWhiteSpace(selected))
                throw new InvalidOperationException("No selected text found. Select a word or sentence first.");

            var translated = await TranslateAsync(selected);
            Clipboard.Clear();
            Clipboard.SetText(translated);
            keybd_event(0x11, 0, 0, UIntPtr.Zero);
            keybd_event(0x56, 0, 0, UIntPtr.Zero);
            keybd_event(0x56, 0, KeyUpFlag, UIntPtr.Zero);
            keybd_event(0x11, 0, KeyUpFlag, UIntPtr.Zero);
            await Task.Delay(100);
            SetStatus($"Translated {settings.Source} → {settings.Target} · ready", true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, false);
        }
        finally
        {
            if (clipboardWasChanged)
            {
                try
                {
                    Clipboard.Clear();
                    if (!string.IsNullOrEmpty(previousClipboard))
                        Clipboard.SetText(previousClipboard);
                }
                catch
                {
                }
            }

            busy = false;
        }
    }

    private static async Task WaitForHotkeyReleaseAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var modifiersDown = (GetAsyncKeyState(0x11) & 0x8000) != 0 || (GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0x10) & 0x8000) != 0 || (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
            if (!modifiersDown) return;
            await Task.Delay(25);
        }
    }

    private async Task<string> TranslateAsync(string text)
    {
        var cacheKey = $"{settings.Source}|{settings.Target}|{text.Trim()}";
        lock (cacheLock) if (translationCache.TryGetValue(cacheKey, out var cached)) return cached;
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={codes[settings.Source]}|{codes[settings.Target]}";
        using var response = await Http.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode is 403 or 429)
            throw new InvalidOperationException("MyMemory daily limit reached. Try again after the service resets its quota.");
        response.EnsureSuccessStatusCode();
        var result = JsonSerializer.Deserialize<MyMemoryResponse>(json);
        var apiStatus = result?.ResponseStatus.ToString();
        var apiDetails = result?.ResponseDetails ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(apiStatus) && apiStatus != "200")
        {
            var isLimit = apiDetails.Contains("quota", StringComparison.OrdinalIgnoreCase) || apiDetails.Contains("limit", StringComparison.OrdinalIgnoreCase) || apiDetails.Contains("available", StringComparison.OrdinalIgnoreCase);
            throw new InvalidOperationException(isLimit ? "MyMemory daily limit reached. Try again after the service resets its quota." : $"MyMemory returned an error: {apiDetails}");
        }
        if (result?.ResponseData?.TranslatedText is not { Length: > 0 } value) throw new InvalidOperationException("The free translation service returned no translation.");
        var translated = WebUtility.HtmlDecode(value);
        lock (cacheLock)
        {
            translationCache[cacheKey] = translated;
        }
        SaveTranslationCache();
        return translated;
    }

    private void SetStatus(string text, bool? okay) { status.Text = text; statusDot.ForeColor = okay == true ? Color.FromArgb(47, 158, 104) : okay == false ? Color.FromArgb(212, 107, 85) : Color.FromArgb(227, 166, 47); }
    private Settings LoadSettings()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, SettingsFile);
            var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();

            if (string.Equals(loaded.Hotkey, "Ctrl+Alt+A", StringComparison.OrdinalIgnoreCase))
                loaded.Hotkey = "F8";

            loaded.DelaySeconds = Math.Clamp(loaded.DelaySeconds, 1, 30);

            return loaded;
        }
        catch
        {
            return new Settings();
        }
    }
    private static (uint, uint) ParseHotkey(string value) { var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); if (parts.Length < 1) throw new InvalidOperationException("Enter a key such as F8, or a combination such as Ctrl+Alt+T."); uint modifiers = 0; foreach (var part in parts[..^1]) modifiers |= part.ToUpperInvariant() switch { "CTRL" or "CONTROL" => ModControl, "ALT" => ModAlt, "SHIFT" => ModShift, "WIN" => ModWin, _ => throw new InvalidOperationException("Use Ctrl, Alt, Shift, or Win as modifiers.") }; var key = parts[^1].ToUpperInvariant(); uint vk = key switch { "SPACE" => 0x20, "F8" => 0x77, "F9" => 0x78, "F10" => 0x79, _ when key.Length == 1 => key[0], _ => throw new InvalidOperationException("The final key must be a letter, Space, F8, F9, or F10.") }; return (modifiers, vk); }
}
