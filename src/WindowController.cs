using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Markup;
using Microsoft.Win32;

namespace DexBridge
{
    public sealed class WindowController : IDisposable
    {
        public Window Window { get; private set; }
        private readonly string _root;
        private readonly string _data;
        private readonly string _settingsPath;
        private readonly bool _automaticScan;
        private AppSettings _settings;
        private UsbBackend _backend;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _displayWatch;
        private readonly List<string> _logs = new List<string>();
        private readonly Dictionary<string, DeviceInfo> _properties = new Dictionary<string, DeviceInfo>();
        private readonly Dictionary<string, Task> _propertyReads = new Dictionary<string, Task>();
        private List<DeviceInfo> _devices = new List<DeviceInfo>();
        private Process _session;
        private bool _scanning, _starting, _stopping, _closed, _loadingSettings;
        private bool _videoReady;
        private string _error;
        private DateTime _sessionStarted;
        private string _sessionMode;
        private SessionPlan _activePlan;
        private string _activePhoneSerial;
        private bool _fallbackRunning;
        private int _connectionGeneration;

        public WindowController(string root, bool automaticScan, string toolsOverride)
        {
            _root = Path.GetFullPath(root); _data = Path.Combine(_root, "Data");
            _settingsPath = Path.Combine(_data, "settings.xml"); _automaticScan = automaticScan;
            _settings = AppSettings.Load(_settingsPath);
            var tools = !String.IsNullOrEmpty(toolsOverride) ? toolsOverride : !String.IsNullOrEmpty(_settings.ToolsDirectory) ? _settings.ToolsDirectory : Path.Combine(_root, "tools");
            _backend = new UsbBackend(tools, _data);
            using (var stream = typeof(WindowController).Assembly.GetManifestResourceStream("DexBridge.MainWindow.xaml"))
            {
                if (stream == null) throw new InvalidOperationException("The app's interface resource is missing.");
                Window = (Window)XamlReader.Load(stream);
            }
            using (var stream = typeof(WindowController).Assembly.GetManifestResourceStream("DexBridge.App.ico"))
            {
                if (stream != null) Window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            }
            LoadSettings();
            C<Button>("RefreshButton").Click += async (s, e) => { _error = null; _properties.Clear(); await RefreshAsync(); };
            C<ComboBox>("Devices").SelectionChanged += async (s, e) => { UpdateState(); await ReadSelectedPropertiesAsync(); };
            C<Button>("ConnectButton").Click += async (s, e) => { if (_session != null) await StopSessionAsync(); else await StartSessionAsync(); };
            C<Button>("FocusButton").Click += (s, e) => FocusSession();
            C<RadioButton>("DesktopMode").Checked += (s, e) => ModeChanged();
            C<RadioButton>("AutoMode").Checked += (s, e) => ModeChanged();
            C<RadioButton>("PhoneMode").Checked += (s, e) => ModeChanged();
            C<Button>("ChooseToolsButton").Click += async (s, e) => await ChooseToolsAsync();
            C<Button>("ExportButton").Click += (s, e) => ExportLog();
            C<Button>("DriverButton").Click += (s, e) => OpenDriverPage();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += async (s, e) => await RefreshAsync();
            _displayWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _displayWatch.Tick += async (s, e) => await RecoverBlankDesktopAsync();
            Window.Loaded += async (s, e) => { UpdateState(); if (_automaticScan) { await RefreshAsync(); if (!_closed) { _timer.Start(); _displayWatch.Start(); } } };
            Window.Closing += Closing;
            Window.Closed += (s, e) => Dispose();
            Log("DeX Bridge 1.1 — Android USB display receiver.");
            Log("Auto chooses a compatible session. DeX still depends on the phone's firmware.");
            UpdateState();
        }

        private T C<T>(string name) where T : FrameworkElement { return (T)Window.FindName(name); }
        private DeviceInfo Selected { get { return C<ComboBox>("Devices").SelectedItem as DeviceInfo; } }
        private static Brush Color(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }

        private void LoadSettings()
        {
            _loadingSettings = true;
            C<RadioButton>("AutoMode").IsChecked = _settings.AutoMode;
            C<RadioButton>("DesktopMode").IsChecked = !_settings.AutoMode && _settings.DesktopMode;
            C<RadioButton>("PhoneMode").IsChecked = !_settings.AutoMode && !_settings.DesktopMode;
            foreach (ComboBoxItem item in C<ComboBox>("Resolution").Items) if ((string)item.Tag == _settings.Resolution) C<ComboBox>("Resolution").SelectedItem = item;
            C<ComboBox>("FrameRate").SelectedIndex = _settings.FrameRate == 30 ? 0 : 1;
            C<CheckBox>("Fullscreen").IsChecked = _settings.Fullscreen;
            C<CheckBox>("Audio").IsChecked = _settings.Audio;
            C<CheckBox>("Clipboard").IsChecked = _settings.Clipboard;
            C<CheckBox>("Keyboard").IsChecked = _settings.PhysicalKeyboard;
            _loadingSettings = false;
        }

        private AppSettings CollectSettings()
        {
            return new AppSettings
            {
                AutoMode = C<RadioButton>("AutoMode").IsChecked == true,
                DesktopMode = C<RadioButton>("DesktopMode").IsChecked == true,
                Resolution = (string)((ComboBoxItem)C<ComboBox>("Resolution").SelectedItem).Tag,
                FrameRate = C<ComboBox>("FrameRate").SelectedIndex == 0 ? 30 : 60,
                Fullscreen = C<CheckBox>("Fullscreen").IsChecked == true,
                Audio = C<CheckBox>("Audio").IsChecked == true,
                Clipboard = C<CheckBox>("Clipboard").IsChecked == true,
                PhysicalKeyboard = C<CheckBox>("Keyboard").IsChecked == true,
                ToolsDirectory = _settings.ToolsDirectory
            };
        }

        private void SaveSettings()
        {
            _settings = CollectSettings();
            try { _settings.Save(_settingsPath); }
            catch (Exception e) { Log("Settings could not be saved: " + e.Message); }
        }

        private void ModeChanged()
        {
            if (_loadingSettings) return;
            _connectionGeneration++;
            _error = null; UpdateState();
        }

        public async Task RefreshAsync()
        {
            if (_scanning || _starting || _closed || !_backend.IsInstalled) { UpdateState(); return; }
            _scanning = true; C<Button>("RefreshButton").IsEnabled = false;
            try
            {
                var found = await _backend.ScanAsync();
                if (_closed) return;
                ApplyDeviceList(found);
                await ReadSelectedPropertiesAsync();
            }
            catch (Exception e)
            {
                if (_closed) return;
                // Clear stale devices so an earlier phone cannot be launched after detection fails.
                ApplyDeviceList(new List<DeviceInfo>());
                if (_error != e.Message) Log(e.Message);
                _error = e.Message;
            }
            finally { _scanning = false; if (!_closed) UpdateState(); }
        }

        private void ApplyDeviceList(List<DeviceInfo> found)
        {
            var signature = String.Join("|", found.Select(x => x.Serial + ":" + x.State));
            var previous = String.Join("|", _devices.Select(x => x.Serial + ":" + x.State));
            if (signature == previous) return;
            var selected = Selected == null ? null : Selected.Serial;
            foreach (var phone in found)
            {
                DeviceInfo known;
                if (phone.IsReady && _properties.TryGetValue(phone.Serial, out known))
                {
                    phone.Model = known.Model; phone.Manufacturer = known.Manufacturer;
                    phone.AndroidVersion = known.AndroidVersion; phone.OneUiVersion = known.OneUiVersion; phone.SdkVersion = known.SdkVersion;
                }
            }
            // An unplugged/re-authorized phone must have its properties read again.
            foreach (var serial in _properties.Keys.ToArray()) if (!found.Any(x => x.Serial == serial && x.IsReady)) _properties.Remove(serial);
            _devices = found;
            C<ComboBox>("Devices").ItemsSource = _devices;
            C<ComboBox>("Devices").SelectedItem = _devices.FirstOrDefault(x => x.Serial == selected) ?? _devices.FirstOrDefault(x => x.IsReady) ?? _devices.FirstOrDefault();
            Log(found.Count == 0 ? "No USB phone detected." : "USB device list changed: " + String.Join(", ", found.Select(x => x.DisplayName)));
            UpdateState();
        }

        private async Task ReadSelectedPropertiesAsync()
        {
            var phone = Selected;
            if (phone == null || !phone.IsReady || _closed || _properties.ContainsKey(phone.Serial)) return;
            Task pending;
            if (_propertyReads.TryGetValue(phone.Serial, out pending))
            {
                try { await pending; } catch (Exception e) { if (!_closed) Log("Phone details are unavailable: " + e.Message); }
                return;
            }
            try
            {
                pending = _backend.ReadPropertiesAsync(phone);
                _propertyReads[phone.Serial] = pending;
                await pending;
                if (_closed) return;
                if (_devices.Any(x => Object.ReferenceEquals(x, phone))) _properties[phone.Serial] = phone;
                C<ComboBox>("Devices").Items.Refresh();
                UpdateState();
            }
            catch (Exception e) { _properties.Remove(phone.Serial); if (!_closed) Log("Could not read phone details: " + e.Message); }
            finally { _propertyReads.Remove(phone.Serial); }
        }

        private void SetStatus(string title, string detail, string shortStatus, string background, string dot)
        {
            C<TextBlock>("StateTitle").Text = title; C<TextBlock>("StateDetail").Text = detail;
            C<TextBlock>("HeaderStatus").Text = shortStatus;
            C<Border>("StateSurface").Background = Color(background);
            ((System.Windows.Shapes.Ellipse)Window.FindName("StatusDot")).Fill = Color(dot);
        }

        private void UpdateState()
        {
            if (_closed) return;
            var phone = Selected;
            bool running = _session != null;
            bool automatic = C<RadioButton>("AutoMode").IsChecked == true;
            bool desktop = automatic ? phone != null && phone.IsDesktopCandidate : C<RadioButton>("DesktopMode").IsChecked == true;
            bool compatible = phone != null && phone.IsReady && phone.CanMirror && (!desktop || phone.CanCreateVirtualDisplay);
            C<StackPanel>("SessionOptions").IsEnabled = !running && !_starting && !_fallbackRunning;
            C<CheckBox>("Audio").IsEnabled = phone == null || phone.CanCaptureAudio;
            C<CheckBox>("Keyboard").IsEnabled = phone == null || phone.SdkVersion >= 29;
            C<ComboBox>("Devices").IsEnabled = !running && !_starting && !_fallbackRunning && _devices.Count > 0;
            C<Button>("RefreshButton").IsEnabled = !_scanning && !_starting && !_fallbackRunning && _backend.IsInstalled;
            C<Button>("ChooseToolsButton").IsEnabled = !running && !_starting && !_scanning && !_fallbackRunning;
            C<Button>("ConnectButton").IsEnabled = !_starting && !_stopping && !_fallbackRunning && (running || (_backend.IsInstalled && compatible));
            C<Button>("ConnectButton").Content = _stopping ? "Closing display…" : _starting || _fallbackRunning ? "Connecting…" : running ? "_Close display" : automatic ? "_Connect phone" : desktop ? "_Open desktop" : "_Mirror phone";
            C<Button>("FocusButton").Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            string modeHelp = automatic ? desktop ? "Auto tries a desktop on this One UI version, then uses mirroring if it fails or stays blank." : "Auto mirrors this phone. A desktop requires support from the phone's firmware." : desktop ? "Creates a virtual display on Android 10+. A desktop launcher must be available on the phone." : "Mirrors your phone's existing screen using your computer's keyboard and mouse.";
            if (running && automatic && _activePlan != null && !_activePlan.DesktopMode) modeHelp = "Auto is using phone mirroring for this session.";
            if (phone != null && phone.SdkVersion > 0 && phone.SdkVersion < 29) modeHelp += " Older Android: basic keyboard, 1280-pixel size limit, 30 fps limit, and no audio.";
            else if (phone != null && !phone.CanCaptureAudio) modeHelp += " Audio requires Android 11+.";
            C<TextBlock>("ModeHelp").Text = modeHelp;
            C<TextBlock>("ToolsStatus").Text = (_backend.IsInstalled ? "Bundled connection tools are ready.\n" : "Connection tools are missing. Select scrcpy.exe from a complete extracted Windows scrcpy bundle.\n") + _backend.ToolsDirectory;
            C<TextBlock>("PhoneDetails").Text = phone == null ? "No authorized USB phone detected" : String.Join(" · ", new[] { phone.Model, String.IsNullOrEmpty(phone.AndroidVersion) ? null : "Android " + phone.AndroidVersion, String.IsNullOrEmpty(phone.OneUiVersion) ? null : "One UI " + phone.OneUiVersion }.Where(x => !String.IsNullOrEmpty(x)));
            if (_stopping) SetStatus("Closing your display", "The virtual display and its connection are being released.", "Closing display", "#EDF3F3", "#667A80");
            else if (_fallbackRunning) SetStatus("Switching to phone mirroring", "The desktop did not start. Auto is opening your phone's existing screen instead.", "Trying phone screen", "#EDF3F3", "#006C67");
            else if (_starting) SetStatus("Connecting to your phone", "Checking the USB connection and preparing your display…", "Connecting", "#EDF3F3", "#006C67");
            else if (running)
            {
                SetStatus(_videoReady ? "Your display is open" : "Waiting for the first video frame", _videoReady ? _sessionMode + " is running in a separate window. Press F11 to leave fullscreen or Alt + Q to close it." : "If the desktop remains blank, close the display and try Phone screen. A virtual display may not have a DeX launcher on this One UI version.", _videoReady ? "Display open" : "Starting display", "#E5F3EF", "#006C67");
            }
            else if (!_backend.IsInstalled) SetStatus("Connection tools are missing", "Open Diagnostics and choose scrcpy.exe from the complete tools bundle.", "Tools missing", "#FFF2DF", "#8A4B14");
            else if (_error != null) SetStatus("The connection needs attention", _error, "Connection issue", "#FFF2DF", "#8A4B14");
            else if (phone == null) SetStatus("Plug in your phone", "Connect a USB data cable, unlock your phone, and allow USB debugging when prompted.", "Waiting for USB", "#EDF3F3", "#667A80");
            else if (phone.State == "unauthorized") SetStatus("Allow this computer on your phone", "Unlock your phone and tap Allow on the USB debugging prompt. The Connect button becomes available automatically.", "Waiting for permission", "#FFF2DF", "#8A4B14");
            else if (!phone.IsReady) SetStatus("Reconnect your phone", "The phone reports '" + phone.State + "'. Unlock it, reconnect the cable, and check that USB debugging is enabled.", "Phone " + phone.State, "#FFF2DF", "#8A4B14");
            else if (!phone.CanMirror) SetStatus("This Android version is too old", "Phone mirroring requires Android 5.0 or newer.", "Unsupported Android", "#FFF2DF", "#8A4B14");
            else if (desktop && !phone.CanCreateVirtualDisplay) SetStatus("Choose Auto or Phone screen", "Virtual desktop needs Android 10+. This phone can use mirroring instead.", "Choose a mode", "#FFF2DF", "#8A4B14");
            else SetStatus("Your phone is ready", desktop ? "Open a virtual desktop, then check that Samsung DeX appears on your phone's current software." : "Open your phone screen and control it using this computer's keyboard and mouse.", "USB phone ready", "#E5F3EF", "#006C67");
        }

        private async Task StartSessionAsync()
        {
            _connectionGeneration++;
            await StartSessionForModeAsync(false);
        }

        private async Task StartSessionForModeAsync(bool forcePhone)
        {
            var phone = Selected;
            if (_starting || _session != null || phone == null || !phone.IsReady) return;
            _starting = true; _error = null; UpdateState();
            try
            {
                if (!await _backend.IsUsbDeviceAsync(phone)) throw new InvalidOperationException("This phone is not reporting a USB transport. Connect it with a USB data cable and refresh. If Windows cannot identify its transport, connect only one USB phone.");
                if (_closed) return;
                await ReadSelectedPropertiesAsync();
                if (_closed || Selected == null || Selected.Serial != phone.Serial) return;
                SaveSettings();
                var plan = SessionPlan.Create(phone, _settings, forcePhone);
                var info = ProcessTools.StartInfo(_backend.ScrcpyPath, ProcessTools.BuildSessionArguments(phone, _settings, plan), _data);
                info.EnvironmentVariables["ADB"] = _backend.AdbPath;
                info.EnvironmentVariables["SCRCPY_SERVER_PATH"] = Path.Combine(_backend.ToolsDirectory, "scrcpy-server");
                var process = new Process { StartInfo = info, EnableRaisingEvents = true };
                process.OutputDataReceived += (s, e) => ReceiveSessionOutput(process, e.Data);
                process.ErrorDataReceived += (s, e) => ReceiveSessionOutput(process, e.Data);
                process.Exited += (s, e) => Dispatch(() => SessionExited(process));
                _sessionStarted = DateTime.UtcNow; _sessionMode = plan.DesktopMode ? "Virtual desktop" : "Phone-screen mirroring"; _videoReady = false;
                _activePlan = plan; _activePhoneSerial = phone.Serial;
                _session = process;
                try { process.Start(); }
                catch { _session = null; process.Dispose(); throw; }
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                Log("Starting " + _sessionMode + " on " + phone.Model + ".");
                // Fast process failure may race event subscription; handle it on the UI dispatcher.
                if (process.HasExited) SessionExited(process);
            }
            catch (Exception e) { _error = e.Message; Log("Could not open the display: " + e.Message); }
            finally { _starting = false; if (!_closed) UpdateState(); }
        }

        private void ReceiveSessionOutput(Process process, string line)
        {
            if (String.IsNullOrEmpty(line)) return;
            Dispatch(() =>
            {
                if (_closed) return;
                Log(line);
                if (_session == process && (line.IndexOf("Texture:", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Video size:", StringComparison.OrdinalIgnoreCase) >= 0)) { _videoReady = true; UpdateState(); }
            });
        }

        private void SessionExited(Process process)
        {
            if (_session != process) return;
            int exitCode = -1;
            try { exitCode = process.ExitCode; } catch (InvalidOperationException) { }
            bool wasStopping = _stopping;
            bool fallback = !wasStopping && !_fallbackRunning && _activePlan != null && _activePlan.Automatic && _activePlan.DesktopMode && (exitCode != 0 || !_videoReady);
            var generation = _connectionGeneration;
            var serial = _activePhoneSerial;
            _session = null; _stopping = false; _videoReady = false;
            Log("Display session ended (exit " + exitCode + ").");
            if (!wasStopping && exitCode != 0)
            {
                _error = "The display stopped. Check the phone's USB permission and reconnect. For a blank or unsupported desktop, choose Phone screen. Details are in Diagnostics.";
                if ((DateTime.UtcNow - _sessionStarted).TotalSeconds < 3) _error = "The display could not start. Check Diagnostics for the tool's error. Try Phone screen, a lower resolution, or turning off phone audio.";
            }
            process.Dispose();
            UpdateState();
            if (fallback) Dispatch(async () => await RecoverToPhoneAsync(generation, serial));
        }

        private async Task StopSessionAsync()
        {
            _connectionGeneration++;
            await CloseSessionAsync();
        }

        private async Task CloseSessionAsync()
        {
            var process = _session;
            if (process == null || _stopping) return;
            _stopping = true; UpdateState();
            try
            {
                CloseDisplayWindow(process);
                var stopped = await Task.Run(() => { try { return process.WaitForExit(2500); } catch (InvalidOperationException) { return true; } });
                if (!stopped)
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    await Task.Run(() => { try { process.WaitForExit(1000); } catch (InvalidOperationException) { } });
                }
                if (_session == process) SessionExited(process);
            }
            catch (Exception e) { Log("Display close: " + e.Message); _stopping = false; UpdateState(); }
        }

        private async Task RecoverBlankDesktopAsync()
        {
            if (_closed || _starting || _stopping || _fallbackRunning || _session == null || _videoReady || _activePlan == null || !_activePlan.Automatic || !_activePlan.DesktopMode) return;
            if ((DateTime.UtcNow - _sessionStarted).TotalSeconds < 12) return;
            await RecoverToPhoneAsync(_connectionGeneration, _activePhoneSerial);
        }

        private async Task RecoverToPhoneAsync(int generation, string serial)
        {
            if (_closed || _fallbackRunning || generation != _connectionGeneration || Selected == null || Selected.Serial != serial || !Selected.IsReady) return;
            _fallbackRunning = true;
            try
            {
                Log("Auto fallback: the virtual desktop failed or produced no frame; trying phone mirroring.");
                await CloseSessionAsync();
                if (_closed || generation != _connectionGeneration || Selected == null || Selected.Serial != serial) return;
                _error = null;
                await StartSessionForModeAsync(true);
            }
            finally { _fallbackRunning = false; if (!_closed) UpdateState(); }
        }

        private void FocusSession()
        {
            if (_session == null) return;
            try
            {
                var handle = FindDisplayWindow(_session.Id);
                if (handle != IntPtr.Zero) { ShowWindow(handle, 9); SetForegroundWindow(handle); }
            }
            catch (InvalidOperationException) { }
        }

        private async Task ChooseToolsAsync()
        {
            var dialog = new OpenFileDialog { Title = "Select scrcpy.exe from an extracted Windows bundle", Filter = "scrcpy executable|scrcpy.exe", CheckFileExists = true };
            if (dialog.ShowDialog(Window) != true) return;
            var tools = Path.GetDirectoryName(dialog.FileName);
            var backend = new UsbBackend(tools, _data);
            if (!backend.IsInstalled) { _error = "This folder is incomplete. It must contain adb.exe, scrcpy.exe, and scrcpy-server from the same scrcpy release."; UpdateState(); return; }
            _backend = backend; _settings.ToolsDirectory = tools; _properties.Clear(); _devices.Clear(); C<ComboBox>("Devices").ItemsSource = null;
            SaveSettings(); _error = null; Log("Using connection tools from " + tools); await RefreshAsync();
        }

        private void ExportLog()
        {
            var dialog = new SaveFileDialog { Title = "Save connection diagnostics", FileName = "DeX-Bridge-diagnostics.txt", Filter = "Text file|*.txt" };
            if (dialog.ShowDialog(Window) != true) return;
            try { File.WriteAllText(dialog.FileName, String.Join(Environment.NewLine, _logs), new UTF8Encoding(false)); Log("Diagnostic log saved."); }
            catch (Exception e) { _error = "Could not save the diagnostic log: " + e.Message; UpdateState(); }
        }

        private void OpenDriverPage()
        {
            try { Process.Start(new ProcessStartInfo("https://developer.android.com/studio/run/oem-usb") { UseShellExecute = true }); }
            catch (Exception e) { _error = "Could not open the driver page: " + e.Message; UpdateState(); }
        }

        private void Closing(object sender, CancelEventArgs e)
        {
            if (_closed) return;
            SaveSettings(); _timer.Stop(); _displayWatch.Stop(); _connectionGeneration++;
            _closed = true;
            var process = _session;
            if (process != null)
            {
                _session = null;
                try
                {
                    CloseDisplayWindow(process);
                    if (!process.WaitForExit(1500)) process.Kill();
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
                finally { process.Dispose(); }
            }
        }

        private void Log(string message)
        {
            if (_closed) return;
            var line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            _logs.Add(line); if (_logs.Count > 500) _logs.RemoveRange(0, _logs.Count - 500);
            C<TextBox>("LogText").Text = String.Join(Environment.NewLine, _logs);
            C<TextBox>("LogText").ScrollToEnd();
            // Logs stay in memory unless the user saves them. Portable settings never contain device IDs.
        }

        private void Dispatch(Action action)
        {
            if (_closed || Window.Dispatcher.HasShutdownStarted) return;
            try { Window.Dispatcher.BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        public void Dispose()
        {
            _timer.Stop();
            _displayWatch.Stop();
            if (!_closed) Closing(this, new CancelEventArgs());
        }

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);

        private static IntPtr FindDisplayWindow(int processId)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((window, parameter) =>
            {
                uint owner; GetWindowThreadProcessId(window, out owner);
                if (owner == (uint)processId && IsWindowVisible(window)) { found = window; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static void CloseDisplayWindow(Process process)
        {
            if (process.HasExited) return;
            var window = FindDisplayWindow(process.Id);
            if (window != IntPtr.Zero) PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE allows scrcpy to clean up.
            else process.CloseMainWindow();
        }
    }
}
