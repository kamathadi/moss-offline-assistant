using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Clipboard = System.Windows.Clipboard;
using Forms = System.Windows.Forms;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;

namespace Moss;

public partial class MainWindow : Window
{
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModNoRepeat = 0x4000;
    private const int WmHotkey = 0x0312, WhMouseLl = 14, WmRightButtonUp = 0x0205;
    private const ushort VkControl = 0x11, VkC = 0x43, VkV = 0x56;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly string[] FeatureOrder = ["summarize", "proofread", "words", "rephrase"];
    private static readonly Dictionary<string, string> FriendlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["summarize"] = "Summarize", ["proofread"] = "Spelling & grammar",
        ["words"] = "Better words", ["rephrase"] = "Rephrase"
    };

    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Moss", "settings.json");
    private readonly string _modelPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "Models", "MossAI.gguf");
    private readonly Dictionary<string, int> _hotkeyIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["summarize"] = 1, ["proofread"] = 2, ["words"] = 3, ["rephrase"] = 4
    };

    private SettingsData _settings = new();
    private Process? _model;
    private Forms.NotifyIcon? _tray;
    private HwndSource? _source;
    private QuickActionsWindow? _quickActions;
    private HookProc? _mouseHookProc;
    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _targetWindow = IntPtr.Zero;
    private string _captureFeature = "";
    private string _currentResult = "";
    private bool _ready, _allowExit;

    public MainWindow()
    {
        InitializeComponent();
        _settings = LoadSettings();
        LoadSettingsIntoUI();
        ConfigureTray();
        SourceInitialized += (_, _) =>
        {
            _source = (HwndSource)PresentationSource.FromVisual(this)!;
            _source.AddHook(WndProc);
            RegisterHotkeys();
        };
        Closing += Window_Closing;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        Loaded += async (_, _) =>
        {
            _ready = true;
            ApplySettings(showStatus: false);
            await StartModelAsync();
        };
    }

    public void StartInBackground()
    {
        ShowInTaskbar = false;
        Show();
        Hide();
    }

    public void OpenSettings()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void ConfigureTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Text = "Moss · private writing tools",
            Icon = CreateMossIcon(),
            Visible = true
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Preferences", null, (_, _) => OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit Moss", null, (_, _) => ExitBackground());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => OpenSettings();
    }

    private static System.Drawing.Icon CreateMossIcon()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(25, 45, 30)), null, new Rect(2, 2, 60, 60), 17, 17);
            var geometry = Geometry.Parse("M32,53 C32,42 32,30 32,11 M32,32 C22,32 13,26 11,17 C21,16 29,20 32,32 M32,44 C43,44 51,37 53,27 C43,26 35,32 32,44");
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(155, 230, 154)), 4.3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            drawing.DrawGeometry(null, pen, geometry);
        }
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);
        var image = png.ToArray();
        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
            writer.Write((byte)64); writer.Write((byte)64); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(image.Length); writer.Write(22);
            writer.Write(image);
        }
        ico.Position = 0;
        return new System.Drawing.Icon(ico);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey) return IntPtr.Zero;
        handled = true;
        var id = wParam.ToInt32();
        var action = _hotkeyIds.FirstOrDefault(pair => pair.Value == id).Key;
        if (string.IsNullOrEmpty(action)) return IntPtr.Zero;
        var target = GetForegroundWindow();
        _ = Dispatcher.BeginInvoke(new Action(async () => await HandleShortcutAsync(action, target)));
        return IntPtr.Zero;
    }

    private async Task HandleShortcutAsync(string action, IntPtr target)
    {
        var selected = await ReadSelectionAsync(target);
        if (string.IsNullOrWhiteSpace(selected))
        {
            ShowTrayMessage("Select text first", "Then right-click it or use your Moss shortcut.");
            return;
        }
        OpenQuickActions(selected.Trim(), target, GetCursorPoint());
        if (_quickActions is not null) await RunActionInPopupAsync(action, selected.Trim());
    }

    private async Task<string> ReadSelectionAsync(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero || targetWindow == _source?.Handle) return "";
        _targetWindow = targetWindow;
        string priorText = "";
        var hadText = false;
        try { hadText = Clipboard.ContainsText(); if (hadText) priorText = Clipboard.GetText(); } catch { }
        try
        {
            SetForegroundWindow(targetWindow);
            await Task.Delay(70);
            SendKey(VkControl, true); SendKey(VkC, true); SendKey(VkC, false); SendKey(VkControl, false);
            await Task.Delay(135);
            var selected = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            if (hadText) Clipboard.SetText(priorText); else Clipboard.Clear();
            return selected;
        }
        catch
        {
            try { SendKey(VkControl, false); SendKey(VkC, false); if (hadText) Clipboard.SetText(priorText); else Clipboard.Clear(); } catch { }
            return "";
        }
    }

    private void OpenQuickActions(string selection, IntPtr target, System.Windows.Point screenPoint)
    {
        _quickActions?.Close();
        _targetWindow = target;
        var enabledActions = new List<QuickActionItem>();
        if (_settings.Summarize) enabledActions.Add(new("summarize", "Summarize", "Keep the important points"));
        if (_settings.Proofread)
        {
            var fix = CorrectCommonTypos(selection.Trim());
            if (!string.Equals(fix, selection.Trim(), StringComparison.Ordinal))
                enabledActions.Add(new("quickfix", $"Possible spelling fix: {fix}", "Review the correction"));
            enabledActions.Add(new("proofread", "Check spelling & grammar", "Review a clean-up suggestion"));
        }
        if (_settings.Words) enabledActions.Add(new("words", "Suggest better words", "Find a more fitting choice"));
        if (_settings.Rephrase) enabledActions.Add(new("rephrase", "Rephrase", "Keep the meaning, change the wording"));
        if (enabledActions.Count == 0) return;

        var popup = new QuickActionsWindow();
        _quickActions = popup;
        popup.ActionRequested += async action => await RunActionInPopupAsync(action, selection);
        popup.CopyRequested += CopyResult;
        popup.ReplaceRequested += async () => await ReplaceResultAsync();
        popup.Closed += (_, _) => { if (ReferenceEquals(_quickActions, popup)) _quickActions = null; };
        popup.ShowActions(enabledActions, selection, _settings.Replace);
        PositionPopup(popup, screenPoint);
        popup.Show();
        popup.Activate();
    }

    private async Task RunActionInPopupAsync(string action, string selection)
    {
        if (_quickActions is null) return;
        _quickActions.SetBusy(FriendlyNames.GetValueOrDefault(action, "Possible spelling fix"));
        var (result, status) = await RunActionAsync(action, selection);
        if (_quickActions is null) return;
        _currentResult = result;
        _quickActions.ShowResult(FriendlyNames.GetValueOrDefault(action, "Spelling suggestion"), selection, result, status, _settings.Replace);
    }

    private async Task<(string Result, string Status)> RunActionAsync(string action, string selectedText)
    {
        var input = selectedText.Trim();
        if (input.Length == 0) return ("", "Select some text first.");
        if (action == "quickfix") return (CorrectCommonTypos(input), "Possible spelling fix · review before replacing");
        if (action == "proofread") input = CorrectCommonTypos(input);
        var instruction = action switch
        {
            "summarize" => "Summarize the text in 1–3 concise sentences. Preserve the key facts.",
            "proofread" => "Correct spelling, punctuation, and grammar only. Preserve meaning and tone. Return only the corrected text.",
            "words" => "Suggest up to 3 more fitting or stronger words for this text. For each, show original → alternative and a short reason. Do not rewrite the text.",
            _ => "Rewrite the text more clearly while preserving meaning and voice. Return only the rewritten text."
        };
        try
        {
            var payload = new
            {
                model = "MossAI",
                temperature = action == "words" ? 0.35 : 0.15,
                max_tokens = action == "summarize" ? 220 : 450,
                messages = new[]
                {
                    new { role = "system", content = "You are MossAI, a concise writing helper running locally. Respond only to the user's text and instruction. Do not invent facts. Preserve the writer's voice and keep suggestions short." },
                    new { role = "user", content = instruction + "\n\nTEXT:\n" + input }
                }
            };
            using var response = await Http.PostAsync("http://127.0.0.1:43817/v1/chat/completions", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var result = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "";
            if (action == "proofread")
            {
                if (!PreservesImportantWords(input, result)) result = input;
                result = CorrectCommonTypos(result);
            }
            return (result, "Done · processed on this laptop");
        }
        catch (Exception)
        {
            return ("", "MossAI could not finish that just now. Try again in a moment.");
        }
    }

    private void PositionPopup(QuickActionsWindow popup, System.Windows.Point point)
    {
        popup.Left = Math.Clamp(point.X + 12, SystemParameters.VirtualScreenLeft + 8, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 410);
        popup.Top = Math.Clamp(point.Y + 12, SystemParameters.VirtualScreenTop + 8, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 360);
    }

    private void CopyResult()
    {
        if (_currentResult.Length == 0) return;
        Clipboard.SetText(_currentResult);
        ClosePopupAndRestoreTarget();
    }

    private async Task ReplaceResultAsync()
    {
        if (_currentResult.Length == 0 || !_settings.Replace) return;
        try
        {
            Clipboard.SetText(_currentResult);
            ClosePopupAndRestoreTarget();
            await Task.Delay(140);
            SendKey(VkControl, true); SendKey(VkV, true); SendKey(VkV, false); SendKey(VkControl, false);
        }
        catch { ShowTrayMessage("Couldn't replace the selection", "Copy the suggestion and paste it instead."); }
    }

    private void ClosePopupAndRestoreTarget()
    {
        _quickActions?.Close();
        if (_targetWindow != IntPtr.Zero) SetForegroundWindow(_targetWindow);
    }

    private async Task StartModelAsync()
    {
        var appDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;
        var server = Path.Combine(appDirectory, "Runtime", "llama-server.exe");
        if (!File.Exists(server) || !File.Exists(_modelPath))
        {
            RuntimeStatus.Text = "MossAI files need repair";
            ShowTrayMessage("MossAI isn't ready", "The local model files could not be found.");
            return;
        }
        try
        {
            using (var health = await Http.GetAsync("http://127.0.0.1:43817/health"))
                if (health.IsSuccessStatusCode) { RuntimeStatus.Text = "MossAI is ready · offline"; return; }
        }
        catch { }
        try
        {
            var start = new ProcessStartInfo(server,
                $"--model \"{_modelPath}\" --host 127.0.0.1 --port 43817 --ctx-size 2048 --threads {Math.Clamp(Environment.ProcessorCount / 2, 1, 6)} --parallel 1 --no-webui --jinja --log-disable")
            {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(server)!
            };
            _model = Process.Start(start) ?? throw new InvalidOperationException();
            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (_model.HasExited) throw new InvalidOperationException();
                try
                {
                    using var response = await Http.GetAsync("http://127.0.0.1:43817/health");
                    if (response.IsSuccessStatusCode) { RuntimeStatus.Text = "MossAI is ready · offline"; return; }
                }
                catch { }
                await Task.Delay(350);
            }
            RuntimeStatus.Text = "MossAI is still starting";
        }
        catch
        {
            RuntimeStatus.Text = "MossAI could not start";
            ShowTrayMessage("MossAI couldn't start", "Open Preferences to check the local installation.");
        }
    }

    private void ShowTrayMessage(string title, string message)
    {
        if (_tray?.Visible == true) _tray.ShowBalloonTip(2500, title, message, Forms.ToolTipIcon.Info);
    }

    private void RegisterHotkeys()
    {
        if (_source is null) return;
        foreach (var id in _hotkeyIds.Values) UnregisterHotKey(_source.Handle, id);
        var registered = 0;
        foreach (var action in FeatureOrder)
        {
            if (!IsFeatureEnabled(action) || !_settings.Shortcuts.TryGetValue(action, out var shortcut)) continue;
            if (!RegisterHotKey(_source.Handle, _hotkeyIds[action], shortcut.Modifiers | ModNoRepeat, (uint)shortcut.Key))
            {
                SettingsStatus.Text = $"{FriendlyNames[action]} shortcut is already in use. Record another one.";
                SettingsStatus.Foreground = Brushes.Orange;
            }
            else registered++;
        }
        if (registered > 0 && SettingsStatus.Foreground == Brushes.Orange)
        {
            SettingsStatus.Text = "Settings saved. One shortcut may be used by another app.";
            SettingsStatus.Foreground = (Brush)FindResource("Accent");
        }
    }

    private void InstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero || !ContextMenuToggle.IsChecked.GetValueOrDefault()) return;
        _mouseHookProc = MouseHookCallback;
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseHookProc, GetModuleHandle(null), 0);
    }

    private void RemoveMouseHook()
    {
        if (_mouseHook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
        _mouseHookProc = null;
    }

    private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && wParam.ToInt32() == WmRightButtonUp && ContextMenuToggle.IsChecked.GetValueOrDefault())
        {
            var target = GetForegroundWindow();
            var info = Marshal.PtrToStructure<MouseHookData>(lParam);
            if (target != IntPtr.Zero && target != _source?.Handle)
            {
                var point = new System.Windows.Point(info.Point.X, info.Point.Y);
                _ = Dispatcher.BeginInvoke(new Action(async () =>
                {
                    if (IsVisible || !ContextMenuToggle.IsChecked.GetValueOrDefault()) return;
                    await Task.Delay(90);
                    var selected = await ReadSelectionAsync(target);
                    if (!string.IsNullOrWhiteSpace(selected)) OpenQuickActions(selected.Trim(), target, point);
                }));
            }
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void ShortcutCapture_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string feature) return;
        _captureFeature = feature;
        SettingsStatus.Text = $"Press the shortcut for {FriendlyNames[feature]}. Use Ctrl, Alt, or Shift with another key. Esc cancels.";
        SettingsStatus.Foreground = (Brush)FindResource("Accent");
        button.Content = "Press keys…";
        button.Focus();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (string.IsNullOrEmpty(_captureFeature)) return;
        var pressed = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        if (pressed == Key.Escape)
        {
            CancelCapture();
            return;
        }
        if (pressed is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift) return;
        var modifiers = Keyboard.Modifiers;
        uint bits = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) bits |= ModControl;
        if (modifiers.HasFlag(ModifierKeys.Alt)) bits |= ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) bits |= ModShift;
        if ((bits & (ModControl | ModAlt | ModShift)) == 0)
        {
            SettingsStatus.Text = "Add Ctrl, Alt, or Shift to make a global shortcut.";
            return;
        }
        var key = KeyInterop.VirtualKeyFromKey(pressed);
        if (key == 0) return;
        if (_settings.Shortcuts.Any(entry => !entry.Key.Equals(_captureFeature, StringComparison.OrdinalIgnoreCase) && entry.Value.Key == key && entry.Value.Modifiers == bits))
        {
            SettingsStatus.Text = "That shortcut is already assigned to another Moss feature.";
            return;
        }
        _settings.Shortcuts[_captureFeature] = new ShortcutSpec { Key = key, Modifiers = bits };
        _captureFeature = "";
        RefreshShortcutLabels();
        ApplySettings();
        SettingsStatus.Text = "Shortcut saved.";
        SettingsStatus.Foreground = (Brush)FindResource("Accent");
    }

    private void CancelCapture()
    {
        _captureFeature = "";
        RefreshShortcutLabels();
        SettingsStatus.Text = "Shortcut capture cancelled.";
    }

    private void FeatureSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) ApplySettings();
    }

    private void ApplySettings(bool showStatus = true)
    {
        _settings.Summarize = SummarizeToggle.IsChecked.GetValueOrDefault();
        _settings.Proofread = ProofreadToggle.IsChecked.GetValueOrDefault();
        _settings.Words = WordsToggle.IsChecked.GetValueOrDefault();
        _settings.Rephrase = RephraseToggle.IsChecked.GetValueOrDefault();
        _settings.ContextMenu = ContextMenuToggle.IsChecked.GetValueOrDefault();
        _settings.Startup = StartupToggle.IsChecked.GetValueOrDefault();
        _settings.Replace = ReplaceToggle.IsChecked.GetValueOrDefault();
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
            if (_settings.Startup) key?.SetValue("Moss", $"\"{Environment.ProcessPath}\" --background");
            else key?.DeleteValue("Moss", false);
        }
        catch { if (showStatus) SettingsStatus.Text = "Saved, but Windows sign-in startup could not be updated."; }
        RegisterHotkeys();
        if (_settings.ContextMenu) InstallMouseHook(); else RemoveMouseHook();
        SummarizeBinding.Text = FormatShortcut("summarize");
        ProofreadBinding.Text = FormatShortcut("proofread");
        WordsBinding.Text = FormatShortcut("words");
        RephraseBinding.Text = FormatShortcut("rephrase");
        SummarizeBinding.Opacity = _settings.Summarize ? 1 : 0.48;
        ProofreadBinding.Opacity = _settings.Proofread ? 1 : 0.48;
        WordsBinding.Opacity = _settings.Words ? 1 : 0.48;
        RephraseBinding.Opacity = _settings.Rephrase ? 1 : 0.48;
        if (showStatus && string.IsNullOrEmpty(_captureFeature))
        {
            SettingsStatus.Text = "Settings saved automatically.";
            SettingsStatus.Foreground = (Brush)FindResource("Accent");
        }
    }

    private bool IsFeatureEnabled(string feature) => feature switch
    {
        "summarize" => _settings.Summarize,
        "proofread" => _settings.Proofread,
        "words" => _settings.Words,
        "rephrase" => _settings.Rephrase,
        _ => false
    };

    private string FormatShortcut(string feature)
    {
        if (!_settings.Shortcuts.TryGetValue(feature, out var shortcut)) return "Not set";
        var parts = new List<string>();
        if ((shortcut.Modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((shortcut.Modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((shortcut.Modifiers & ModShift) != 0) parts.Add("Shift");
        var key = KeyInterop.KeyFromVirtualKey(shortcut.Key);
        parts.Add(key == Key.Space ? "Space" : key.ToString());
        return string.Join(" + ", parts);
    }

    private SettingsData LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new SettingsData();
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Shortcuts", out _))
            {
                settings.Shortcuts["summarize"] = FromLegacyShortcut(settings.SummaryKey, true);
                settings.Shortcuts["proofread"] = FromLegacyShortcut(settings.ToolsKey, false);
            }
            settings.Shortcuts ??= new SettingsData().Shortcuts;
            foreach (var feature in FeatureOrder)
                if (!settings.Shortcuts.ContainsKey(feature)) settings.Shortcuts[feature] = new SettingsData().Shortcuts[feature];
            return settings;
        }
        catch { return new SettingsData(); }
    }

    private static ShortcutSpec FromLegacyShortcut(int index, bool summary) => index switch
    {
        1 => new ShortcutSpec { Key = 0x20, Modifiers = ModControl | ModShift },
        2 => new ShortcutSpec { Key = summary ? 0x4D : 0x4A, Modifiers = ModControl | ModAlt },
        _ => new ShortcutSpec { Key = summary ? 0x20 : 0x47, Modifiers = ModControl | ModAlt }
    };

    private void LoadSettingsIntoUI()
    {
        SummarizeToggle.IsChecked = _settings.Summarize;
        ProofreadToggle.IsChecked = _settings.Proofread;
        WordsToggle.IsChecked = _settings.Words;
        RephraseToggle.IsChecked = _settings.Rephrase;
        ContextMenuToggle.IsChecked = _settings.ContextMenu;
        StartupToggle.IsChecked = _settings.Startup;
        ReplaceToggle.IsChecked = _settings.Replace;
        RefreshShortcutLabels();
    }

    private void RefreshShortcutLabels()
    {
        SummarizeBinding.Text = FormatShortcut("summarize");
        ProofreadBinding.Text = FormatShortcut("proofread");
        WordsBinding.Text = FormatShortcut("words");
        RephraseBinding.Text = FormatShortcut("rephrase");
        SummarizeBinding.Opacity = _settings.Summarize ? 1 : 0.48;
        ProofreadBinding.Opacity = _settings.Proofread ? 1 : 0.48;
        WordsBinding.Opacity = _settings.Words ? 1 : 0.48;
        RephraseBinding.Opacity = _settings.Rephrase ? 1 : 0.48;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowExit) { e.Cancel = true; Hide(); ShowInTaskbar = false; return; }
        foreach (var id in _hotkeyIds.Values) UnregisterHotKey(_source?.Handle ?? IntPtr.Zero, id);
        RemoveMouseHook();
        _quickActions?.Close();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        try { if (_model is { HasExited: false }) _model.Kill(true); } catch { }
        _model?.Dispose();
    }

    private void ExitBackground()
    {
        _allowExit = true;
        Close();
        Application.Current.Shutdown();
    }

    private static System.Windows.Point GetCursorPoint()
    {
        var p = Forms.Cursor.Position;
        return new System.Windows.Point(p.X, p.Y);
    }

    private static string CorrectCommonTypos(string text)
    {
        var corrections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["definately"] = "definitely", ["definitley"] = "definitely", ["recieve"] = "receive", ["recieved"] = "received", ["recieving"] = "receiving",
            ["seperate"] = "separate", ["seperately"] = "separately", ["becuase"] = "because", ["beleive"] = "believe", ["wierd"] = "weird",
            ["freind"] = "friend", ["adress"] = "address", ["addres"] = "address", ["occured"] = "occurred", ["occurence"] = "occurrence",
            ["untill"] = "until", ["begining"] = "beginning", ["thier"] = "their", ["teh"] = "the", ["taht"] = "that", ["wich"] = "which",
            ["wether"] = "whether", ["agian"] = "again", ["agianst"] = "against", ["enviroment"] = "environment", ["goverment"] = "government",
            ["existance"] = "existence", ["occassion"] = "occasion", ["realy"] = "really", ["alot"] = "a lot", ["doesnt"] = "doesn't",
            ["dont"] = "don't", ["didnt"] = "didn't", ["isnt"] = "isn't", ["wasnt"] = "wasn't", ["cant"] = "can't", ["wont"] = "won't",
            ["couldnt"] = "couldn't", ["shouldnt"] = "shouldn't", ["wouldnt"] = "wouldn't", ["im"] = "I'm", ["ive"] = "I've", ["youre"] = "you're",
            ["theyre"] = "they're", ["thats"] = "that's", ["whats"] = "what's", ["could of"] = "could have", ["should of"] = "should have"
        };
        foreach (var pair in corrections) text = Regex.Replace(text, $@"\b{Regex.Escape(pair.Key)}\b", match =>
        {
            var corrected = pair.Value;
            return match.Value.Length > 0 && char.IsUpper(match.Value[0]) ? char.ToUpperInvariant(corrected[0]) + corrected[1..] : corrected;
        }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return text;
    }

    private static bool PreservesImportantWords(string input, string output)
    {
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "an", "and", "are", "as", "at", "be", "been", "by", "for", "from", "had", "has", "have", "he", "her", "his", "i", "in", "is", "it", "its", "me", "my", "of", "on", "or", "our", "she", "that", "the", "their", "them", "there", "they", "this", "to", "us", "was", "we", "were", "will", "with", "you", "your" };
        var original = Regex.Matches(input.ToLowerInvariant(), @"[a-z']+").Select(m => NormalizeWord(m.Value)).Where(w => w.Length > 2 && !stop.Contains(w)).Distinct().ToArray();
        if (original.Length == 0) return true;
        var result = Regex.Matches(output.ToLowerInvariant(), @"[a-z']+").Select(m => NormalizeWord(m.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return original.Count(result.Contains) >= Math.Ceiling(original.Length * 0.9);
    }

    private static string NormalizeWord(string word)
    {
        if (word.Length > 5 && word.EndsWith("ing")) return word[..^3];
        if (word.Length > 4 && word.EndsWith("ed")) return word[..^2];
        if (word.Length > 4 && word.EndsWith("es")) return word[..^2];
        if (word.Length > 3 && word.EndsWith('s')) return word[..^1];
        return word;
    }

    private static void SendKey(ushort key, bool down) => keybd_event((byte)key, 0, down ? 0u : 2u, UIntPtr.Zero);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData { public NativePoint Point; public uint MouseData; public uint Flags; public uint Time; public IntPtr ExtraInfo; }
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int hookId, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string? moduleName);

    private sealed class SettingsData
    {
        public bool Summarize { get; set; } = true;
        public bool Proofread { get; set; } = true;
        public bool Words { get; set; } = true;
        public bool Rephrase { get; set; } = true;
        public bool ContextMenu { get; set; } = true;
        public bool Startup { get; set; } = true;
        public bool Replace { get; set; } = true;
        public int SummaryKey { get; set; }
        public int ToolsKey { get; set; }
        public Dictionary<string, ShortcutSpec> Shortcuts { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["summarize"] = new() { Key = 0x20, Modifiers = ModControl | ModAlt },
            ["proofread"] = new() { Key = 0x47, Modifiers = ModControl | ModAlt },
            ["words"] = new() { Key = 0x57, Modifiers = ModControl | ModAlt },
            ["rephrase"] = new() { Key = 0x52, Modifiers = ModControl | ModAlt }
        };
    }

    private sealed class ShortcutSpec { public int Key { get; set; } public uint Modifiers { get; set; } }
}
