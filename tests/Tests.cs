using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DexBridge;

internal static class Tests
{
    private static int _checks;
    private static string _root, _fixture, _captures;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            _root = Path.GetFullPath(args[0]); _fixture = Path.GetFullPath(args[1]);
            _captures = Path.Combine(_root, ".impeccable", "review"); Directory.CreateDirectory(_captures);
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Application.Current.Dispatcher));
            CoreTests(); CompatibilityTests(); Pump(ProcessTests()); UiAndSessionTests(); BroadDeviceTests();
            Console.WriteLine("PASS: " + _checks + " checks. USB fixtures, real child processes, native WPF layouts and session lifecycle.");
            Console.WriteLine("Native UI captures: " + _captures);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { Application.Current.Shutdown(); }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _checks++;
    }

    private static void CoreTests()
    {
        var parsed = DeviceInfo.ParseList("List of devices attached\r\nUSB123 device usb:1 model:Galaxy_S24_Ultra\r\nUSB456 unauthorized\nUSB789 offline\n192.168.0.1:5555 device\nadb-a._adb-tls-connect._tcp. device\nemulator-5554 device\n* daemon started successfully\nmalformed\n");
        Check(parsed.Count == 3, "wireless and emulator filtering");
        Check(parsed[0].IsReady && parsed[0].DisplayName.Contains("Galaxy S24 Ultra"), "model parsing");
        Check(!parsed[1].IsReady && !parsed[2].IsReady, "unauthorized and offline states");
        Check(DeviceInfo.ParseList(null).Count == 0, "empty device output");
        Check(DeviceInfo.FormatOneUi("90000") == "9.0", "One UI 9 properties");
        Check(DeviceInfo.FormatOneUi("60100") == "6.1", "One UI minor properties");
        Check(DeviceInfo.FormatOneUi("") == "", "missing One UI property");
        var phone = parsed[0];
        phone.ApplyProperties("[ro.product.model]: [SM-S928B]\n[ro.build.version.sdk]: [37]\n[ro.build.version.oneui]: [90000]\n[ro.build.version.release]: [17]\n");
        Check(phone.Model == "SM-S928B" && phone.SdkVersion == 37 && phone.OneUiVersion == "9.0", "read software properties");
        var settings = new AppSettings();
        var desktop = ProcessTools.BuildSessionArguments(phone, settings);
        Check(desktop.Contains("--new-display=1920x1080/284") && desktop.Contains("--fullscreen"), "desktop launch arguments");
        Check(desktop.Contains("--serial=USB123") && desktop.Contains("--keyboard=uhid") && desktop.Contains("--mouse-bind=++++:bhsn"), "device selection and input modes");
        settings.Resolution = "2560x1440";
        Check(ProcessTools.BuildSessionArguments(phone, settings).Contains("--new-display=2560x1440/380"), "QHD readable display density");
        settings.AutoMode = false; settings.DesktopMode = false; settings.Audio = false; settings.Clipboard = false; settings.Fullscreen = false; settings.PhysicalKeyboard = false;
        var mirror = ProcessTools.BuildSessionArguments(phone, settings);
        Check(!mirror.Any(x => x.StartsWith("--new-display")) && mirror.Contains("--max-size=2560"), "phone mirroring preserves aspect ratio");
        Check(mirror.Contains("--no-audio") && mirror.Contains("--no-clipboard-autosync") && !mirror.Contains("--fullscreen") && !mirror.Contains("--keyboard=uhid"), "optional features disabled");
        bool rejected = false; try { ProcessTools.BuildSessionArguments(parsed[1], settings); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "unauthorized device cannot launch");
        settings.Resolution = "garbage"; settings.FrameRate = -99; settings.Normalize();
        Check(settings.Resolution == "1920x1080" && settings.FrameRate == 60, "invalid configuration values normalized");
        var config = Path.Combine(_fixture, "settings.xml"); settings.Save(config);
        var loaded = AppSettings.Load(config);
        Check(!loaded.DesktopMode && loaded.Resolution == "1920x1080", "configuration persistence");
        File.WriteAllText(config, "broken XML"); Check(AppSettings.Load(config).DesktopMode, "corrupt configuration recovers");
        File.WriteAllText(config, "<!DOCTYPE AppSettings [<!ENTITY xxe SYSTEM 'file:///C:/Windows/win.ini'>]><AppSettings><ToolsDirectory>&xxe;</ToolsDirectory></AppSettings>");
        Check(AppSettings.Load(config).ToolsDirectory == "", "external XML entities rejected");
        File.WriteAllText(config, "<AppSettings><DesktopMode>false</DesktopMode></AppSettings>");
        loaded = AppSettings.Load(config);
        Check(!loaded.AutoMode && !loaded.DesktopMode, "v1.0 phone-mode preference is preserved");
    }

    private static async Task ProcessTests()
    {
        var backend = new UsbBackend(Path.Combine(_fixture, "tools"), Path.Combine(_fixture, "Data"));
        Check(backend.IsInstalled, "complete tools bundle detection");
        Check(!new UsbBackend(Path.Combine(_fixture, "missing"), _fixture).IsInstalled, "missing tools detection");
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_STATE", "device");
        var phones = await backend.ScanAsync();
        Check(phones.Count == 1 && phones[0].Serial == "R58T24ULTRA", "process-level USB scan");
        await backend.ReadPropertiesAsync(phones[0]);
        Check(phones[0].OneUiVersion == "9.0", "process-level software detection");
        Check(await backend.IsUsbDeviceAsync(phones[0]), "verify actual USB transport");
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_USB", "false");
        Check(!await backend.IsUsbDeviceAsync(phones[0]), "reject non-USB transport");
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_USB", "unknown");
        Check(await backend.IsUsbDeviceAsync(phones[0]), "Windows unknown device path accepts matching USB-only serial");
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_USB_SERIAL", "DIFFERENT_PHONE");
        Check(!await backend.IsUsbDeviceAsync(phones[0]), "Windows USB fallback rejects a different phone");
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_USB_SERIAL", null);
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_USB", null);
        var values = new[] { "", "normal", "two words", "ends with slash \\", "quote\"value", "spaces and \\\"quotes\\\"", "`$();%&|" };
        var arguments = new List<string> { "-s", "fixture", "roundtrip" }; arguments.AddRange(values);
        var result = await ProcessTools.RunAsync(backend.AdbPath, arguments, backend.DataDirectory, 3000);
        var roundtrip = result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Take(values.Length).Select(x => Encoding.UTF8.GetString(Convert.FromBase64String(x))).ToArray();
        Check(values.SequenceEqual(roundtrip), "Windows argument quoting round-trip, including empty arguments, quotes, trailing slashes and shell metacharacters");
        bool timedOut = false;
        try { await ProcessTools.RunAsync(backend.AdbPath, new[] { "timeout" }, backend.DataDirectory, 100); } catch (TimeoutException) { timedOut = true; }
        Check(timedOut, "stuck child process times out");
    }

    private static void UiAndSessionTests()
    {
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_STATE", "none");
        using (var controller = new WindowController(_fixture, false, Path.Combine(_fixture, "tools")))
        {
            Pump(controller.RefreshAsync());
            Check(!Button(controller, "ConnectButton").IsEnabled, "disconnected open button disabled");
            Capture(controller, "windows-disconnected.png", 1100, 790);
            Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_STATE", "unauthorized"); Pump(controller.RefreshAsync());
            Check(Text(controller, "HeaderStatus") == "Waiting for permission" && !Button(controller, "ConnectButton").IsEnabled, "authorization state guides user");
            Capture(controller, "windows-unauthorized.png", 1100, 790);
            Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_STATE", "device"); Pump(controller.RefreshAsync());
            Check(Button(controller, "ConnectButton").IsEnabled && Text(controller, "PhoneDetails").Contains("One UI 9.0"), "authorized S24 fixture enables desktop");
            ((RadioButton)controller.Window.FindName("DesktopMode")).IsChecked = true;
            Capture(controller, "windows-ready.png", 1100, 790);
            Capture(controller, "windows-small.png", 900, 640);
            CheckControlsFit(controller.Window, 900);
            ((TabControl)controller.Window.FindName("Tabs")).SelectedIndex = 1;
            Capture(controller, "windows-help.png", 1100, 790);
            ((TabControl)controller.Window.FindName("Tabs")).SelectedIndex = 2;
            Capture(controller, "windows-diagnostics.png", 1100, 790);
            ((TabControl)controller.Window.FindName("Tabs")).SelectedIndex = 0;
            var capturedArgs = Path.Combine(_fixture, "arguments.txt");
            Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_ARGUMENTS", capturedArgs);
            Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_USB", "false"); Pump(InvokeTask(controller, "StartSessionAsync"));
            Check(Field(controller, "_session") == null && Text(controller, "StateDetail").Contains("USB transport"), "UI rejects wireless transport before starting session");
            Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_USB", null);
            Pump(InvokeTask(controller, "StartSessionAsync")); Pump(Task.Delay(200));
            Check(Field(controller, "_session") != null && !((StackPanel)controller.Window.FindName("SessionOptions")).IsEnabled, "session launches and locks changing settings");
            Check(File.ReadAllLines(capturedArgs).Contains("--new-display=1920x1080/284"), "scrcpy process receives real launch arguments");
            Capture(controller, "windows-running.png", 1100, 790);
            Pump(InvokeTask(controller, "StopSessionAsync"));
            Check(Field(controller, "_session") == null && Button(controller, "ConnectButton").IsEnabled, "close session releases process and allows reconnect");
            Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_FAIL", "true"); Pump(InvokeTask(controller, "StartSessionAsync")); Pump(Task.Delay(300));
            Check(Field(controller, "_session") == null && Text(controller, "HeaderStatus") == "Connection issue", "early scrcpy failure offers recovery");
            Capture(controller, "windows-error.png", 1100, 790);
            Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_FAIL", null);
            Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_STATE", "error"); Pump(controller.RefreshAsync());
            Check(!Button(controller, "ConnectButton").IsEnabled, "scan failure clears stale authorized devices");
        }
        using (var missing = new WindowController(Path.Combine(_fixture, "missing-app"), false, null))
        {
            Check(Text(missing, "HeaderStatus") == "Tools missing" && !Button(missing, "ConnectButton").IsEnabled, "missing tools recovery state");
            Capture(missing, "windows-missing-tools.png", 1100, 790);
        }
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_STATE", null);
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_ARGUMENTS", null);
    }

    private static void CompatibilityTests()
    {
        foreach (var maker in new[] { "Samsung", "Google", "Xiaomi", "OnePlus", "Motorola", "Sony", "ASUS", "Nokia", "Nothing", "OPPO" })
        {
            var phone = new DeviceInfo { Serial = "fixture", State = "device", Manufacturer = maker, Model = "Any model", SdkVersion = 35 };
            var plan = SessionPlan.Create(phone, new AppSettings(), false);
            Check(!plan.DesktopMode && plan.Audio, maker + " unknown desktop capability uses compatible mirroring");
        }
        foreach (int sdk in new[] { 21, 23, 26, 28, 29, 30, 33, 37 })
        {
            var phone = new DeviceInfo { Serial = "fixture", State = "device", SdkVersion = sdk };
            var plan = SessionPlan.Create(phone, new AppSettings(), false);
            var args = ProcessTools.BuildSessionArguments(phone, new AppSettings(), plan);
            Check(!plan.DesktopMode && args.Contains("--no-audio") == (sdk < 30), "Android API " + sdk + " audio compatibility");
            Check(args.Contains("--keyboard=uhid") == (sdk >= 29), "Android API " + sdk + " keyboard compatibility");
            if (sdk < 29) Check(args.Contains("--max-size=1280") && args.Contains("--max-fps=30"), "legacy device video defaults");
        }
        var galaxy = new DeviceInfo { Serial = "fixture", State = "device", SdkVersion = 35, OneUiVersion = "8.0", Model = "Unknown Galaxy model" };
        Check(SessionPlan.Create(galaxy, new AppSettings(), false).DesktopMode, "no Galaxy model whitelist blocks an unknown One UI 8 model");
        Check(!SessionPlan.Create(galaxy, new AppSettings(), true).DesktopMode, "forced automatic fallback uses phone screen");
        galaxy.OneUiVersion = "6.1";
        Check(!SessionPlan.Create(galaxy, new AppSettings(), false).DesktopMode, "older One UI automatically mirrors");
        galaxy.OneUiVersion = "unknown";
        Check(!SessionPlan.Create(galaxy, new AppSettings(), false).DesktopMode, "unreadable One UI version uses mirroring");
        var explicitDesktop = new AppSettings { AutoMode = false, DesktopMode = true };
        galaxy.SdkVersion = 29;
        Check(SessionPlan.Create(galaxy, explicitDesktop, false).DesktopMode, "Android 10 manual virtual display remains available");
        galaxy.SdkVersion = 28;
        bool rejected = false; try { SessionPlan.Create(galaxy, explicitDesktop, false); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Android 9 cannot request a virtual desktop");
        galaxy.SdkVersion = 19;
        rejected = false; try { SessionPlan.Create(galaxy, new AppSettings(), false); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "below Android 5 is rejected clearly");
    }

    private static void BroadDeviceTests()
    {
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_STATE", "device");
        foreach (var profile in new[] { "pixel", "legacy", "old-galaxy", "xiaomi", "too-old" })
        {
            Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_PROFILE", profile);
            var profileRoot = Path.Combine(_fixture, "profile-" + profile);
            using (var controller = new WindowController(profileRoot, false, Path.Combine(_fixture, "tools")))
            {
                ((RadioButton)controller.Window.FindName("AutoMode")).IsChecked = true;
                Pump(controller.RefreshAsync());
                Check(Button(controller, "ConnectButton").IsEnabled == (profile != "too-old"), profile + " automatic readiness");
                if (profile == "too-old") { Capture(controller, "windows-old-android.png", 1100, 790); continue; }
                var argsPath = Path.Combine(profileRoot, "arguments.txt");
                Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_ARGUMENTS", argsPath);
                Pump(InvokeTask(controller, "StartSessionAsync"));
                Pump(WaitUntil(() => File.Exists(argsPath) && Field(controller, "_videoReady") is bool && (bool)Field(controller, "_videoReady")));
                var args = File.ReadAllLines(argsPath);
                Check(!args.Any(x => x.StartsWith("--new-display=")), profile + " launches phone mirroring");
                if (profile == "legacy") Check(args.Contains("--no-audio") && !args.Contains("--keyboard=uhid") && args.Contains("--max-fps=30"), "legacy launch uses compatible audio, keyboard and video flags");
                if (profile == "pixel" || profile == "legacy") Capture(controller, "windows-" + profile + ".png", 1100, 790);
                Pump(InvokeTask(controller, "StopSessionAsync"));
            }
        }
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_PROFILE", "galaxy");
        foreach (var failure in new[] { "FAIL_DESKTOP", "BLANK_DESKTOP" })
        {
            var profileRoot = Path.Combine(_fixture, failure);
            using (var controller = new WindowController(profileRoot, false, Path.Combine(_fixture, "tools")))
            {
                ((RadioButton)controller.Window.FindName("AutoMode")).IsChecked = true;
                Pump(controller.RefreshAsync());
                var argsPath = Path.Combine(profileRoot, "arguments.txt");
                Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_ARGUMENTS", argsPath);
                Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_" + failure, "true");
                Pump(InvokeTask(controller, "StartSessionAsync"));
                if (failure == "BLANK_DESKTOP")
                {
                    Pump(WaitUntil(() => File.Exists(argsPath)));
                    controller.GetType().GetField("_sessionStarted", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, DateTime.UtcNow.AddSeconds(-20));
                    Pump(InvokeTask(controller, "RecoverBlankDesktopAsync"));
                }
                Pump(WaitUntil(() => Field(controller, "_session") != null && Field(controller, "_activePlan") != null && !((SessionPlan)Field(controller, "_activePlan")).DesktopMode && (bool)Field(controller, "_videoReady")));
                Check(!File.ReadAllLines(argsPath).Any(x => x.StartsWith("--new-display=")), failure + " falls back to mirroring exactly once");
                Check(Text(controller, "ModeHelp").Contains("phone mirroring"), "fallback status describes the active session");
                if (failure == "FAIL_DESKTOP") Capture(controller, "windows-auto-fallback.png", 1100, 790);
                Pump(InvokeTask(controller, "StopSessionAsync"));
                Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_" + failure, null);
            }
        }
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_PROFILE", null);
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_ARGUMENTS", null);
        Environment.SetEnvironmentVariable("DEXBRIDGE_TEST_STATE", null);
    }

    private static async Task WaitUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (!ready()) { if (DateTime.UtcNow >= deadline) throw new Exception("Fixture session did not reach its expected state."); await Task.Delay(40); }
    }

    private static Task InvokeTask(object target, string name) { return (Task)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null); }
    private static object Field(object target, string name) { return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    private static Button Button(WindowController controller, string name) { return (Button)controller.Window.FindName(name); }
    private static string Text(WindowController controller, string name) { return ((TextBlock)controller.Window.FindName(name)).Text; }

    private static void Pump(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(t => Application.Current.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }

    private static void Capture(WindowController controller, string name, int width, int height)
    {
        // Render the production WPF visual tree at the shipped logical viewport sizes.
        // Fixture phones are synthetic; these images do not prove a physical DeX session.
        var root = (FrameworkElement)controller.Window.Content;
        var size = new Size(width - 16, height - 40);
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(_captures, name))) encoder.Save(stream);
        Check(root.ActualWidth > 0 && root.ActualHeight > 0, "native WPF layout: " + name);
    }

    private static void CheckControlsFit(DependencyObject root, double width)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var element = child as FrameworkElement;
            if (element != null && element.IsVisible && (element is Button || element is ComboBox)) Check(element.ActualWidth <= width, "control fits small native viewport: " + element.Name);
            CheckControlsFit(child, width);
        }
    }
}
