using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace DexBridge
{
    public sealed class DeviceInfo
    {
        public string Serial { get; set; }
        public string State { get; set; }
        public string Model { get; set; }
        public string Manufacturer { get; set; }
        public string AndroidVersion { get; set; }
        public string OneUiVersion { get; set; }
        public int SdkVersion { get; set; }
        public bool IsReady { get { return State == "device"; } }
        public bool CanMirror { get { return SdkVersion == 0 || SdkVersion >= 21; } }
        public bool CanCreateVirtualDisplay { get { return SdkVersion >= 29; } }
        public bool CanCaptureAudio { get { return SdkVersion >= 30; } }
        public bool IsDesktopCandidate
        {
            get
            {
                int major;
                var version = (OneUiVersion ?? "").Split('.')[0];
                // Firmware capability is a candidate, never a claim that a particular model has DeX.
                return CanCreateVirtualDisplay && Int32.TryParse(version, out major) && major >= 8;
            }
        }
        public string DisplayName
        {
            get
            {
                var name = String.IsNullOrEmpty(Model) ? "Android phone" : Model.Replace('_', ' ');
                return name + (IsReady ? "" : " — " + State) + "  (" + Serial + ")";
            }
        }

        public static List<DeviceInfo> ParseList(string output)
        {
            var list = new List<DeviceInfo>();
            foreach (var line in (output ?? "").Split('\n'))
            {
                var fields = Regex.Split(line.Trim(), @"\s+");
                if (fields.Length < 2 || !new[] { "device", "unauthorized", "offline", "recovery", "sideload", "bootloader" }.Contains(fields[1])) continue;
                // Wireless ADB endpoints and mDNS names must not enter this USB-only workflow.
                if (fields[0].Contains(":") || fields[0].IndexOf("._adb", StringComparison.OrdinalIgnoreCase) >= 0 || fields[0].StartsWith("emulator-", StringComparison.OrdinalIgnoreCase)) continue;
                var model = fields.FirstOrDefault(x => x.StartsWith("model:", StringComparison.Ordinal));
                list.Add(new DeviceInfo { Serial = fields[0], State = fields[1], Model = model == null ? "" : model.Substring(6) });
            }
            return list;
        }

        public void ApplyProperties(string output)
        {
            var props = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(output ?? "", @"^\[([^\]]+)\]: \[([^\]]*)\]", RegexOptions.Multiline)) props[match.Groups[1].Value] = match.Groups[2].Value;
            string value;
            if (props.TryGetValue("ro.product.model", out value)) Model = value;
            if (props.TryGetValue("ro.product.manufacturer", out value)) Manufacturer = value;
            if (props.TryGetValue("ro.build.version.release", out value)) AndroidVersion = value;
            if (props.TryGetValue("ro.build.version.sdk", out value)) { int sdk; if (Int32.TryParse(value, out sdk)) SdkVersion = sdk; }
            if (props.TryGetValue("ro.build.version.oneui", out value)) OneUiVersion = FormatOneUi(value);
        }

        public static string FormatOneUi(string raw)
        {
            int version;
            if (!Int32.TryParse(raw, out version) || version < 10000) return raw ?? "";
            return (version / 10000).ToString(CultureInfo.InvariantCulture) + "." + ((version / 100) % 100).ToString(CultureInfo.InvariantCulture);
        }
    }

    public sealed class AppSettings
    {
        public bool AutoMode { get; set; }
        public bool DesktopMode { get; set; }
        public string Resolution { get; set; }
        public int FrameRate { get; set; }
        public bool Fullscreen { get; set; }
        public bool Audio { get; set; }
        public bool Clipboard { get; set; }
        public bool PhysicalKeyboard { get; set; }
        public string ToolsDirectory { get; set; }

        public AppSettings()
        {
            AutoMode = true; DesktopMode = true; Resolution = "1920x1080"; FrameRate = 60;
            Fullscreen = true; Audio = true; Clipboard = true; PhysicalKeyboard = true;
            ToolsDirectory = "";
        }

        public void Normalize()
        {
            if (!new[] { "1280x720", "1920x1080", "2560x1440" }.Contains(Resolution)) Resolution = "1920x1080";
            if (FrameRate != 30 && FrameRate != 60) FrameRate = 60;
            if (ToolsDirectory == null) ToolsDirectory = "";
        }

        public static AppSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new AppSettings();
                // Disable DTDs and external XML resolution in locally edited configuration files.
                var options = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
                using (var reader = System.Xml.XmlReader.Create(path, options))
                {
                    var result = (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(reader);
                    // Keep an existing v1.0 user's explicit desktop/phone choice during migration.
                    var document = new System.Xml.XmlDocument { XmlResolver = null };
                    using (var migrationReader = System.Xml.XmlReader.Create(path, options)) document.Load(migrationReader);
                    if (document.DocumentElement.SelectSingleNode("AutoMode") == null) result.AutoMode = false;
                    result.Normalize(); return result;
                }
            }
            catch { return new AppSettings(); }
        }

        public void Save(string path)
        {
            Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp";
            using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false))) new XmlSerializer(typeof(AppSettings)).Serialize(writer, this);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }

    public sealed class CommandResult
    {
        public int ExitCode;
        public string Output;
        public string Error;
    }

    public sealed class SessionPlan
    {
        public bool DesktopMode;
        public bool Automatic;
        public bool Audio;
        public bool PhysicalKeyboard;
        public bool LegacyVideo;

        public static SessionPlan Create(DeviceInfo device, AppSettings settings, bool forcePhone)
        {
            if (device == null || !device.IsReady) throw new InvalidOperationException("Select an authorized USB phone first.");
            if (!device.CanMirror) throw new InvalidOperationException("Phone mirroring needs Android 5.0 or newer.");
            bool desktop = !forcePhone && (settings.AutoMode ? device.IsDesktopCandidate : settings.DesktopMode);
            if (desktop && !device.CanCreateVirtualDisplay) throw new InvalidOperationException("Virtual desktop needs Android 10 or newer. Choose Auto or Phone screen.");
            return new SessionPlan
            {
                DesktopMode = desktop, Automatic = settings.AutoMode,
                Audio = settings.Audio && device.CanCaptureAudio,
                PhysicalKeyboard = settings.PhysicalKeyboard && device.SdkVersion >= 29,
                LegacyVideo = device.SdkVersion > 0 && device.SdkVersion < 29
            };
        }
    }

    public static class ProcessTools
    {
        // Windows CommandLineToArgvW quoting. Arguments never pass through a shell.
        public static string Quote(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length > 0 && !value.Any(c => Char.IsWhiteSpace(c) || c == '"')) return value;
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result.Append('\\', slashes * 2 + 1);
                else result.Append('\\', slashes);
                result.Append(c); slashes = 0;
            }
            result.Append('\\', slashes * 2); result.Append('"');
            return result.ToString();
        }

        public static ProcessStartInfo StartInfo(string file, IEnumerable<string> args, string dataDirectory)
        {
            var info = new ProcessStartInfo(file, String.Join(" ", args.Select(Quote)));
            info.UseShellExecute = false; info.CreateNoWindow = true;
            info.RedirectStandardOutput = true; info.RedirectStandardError = true;
            info.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(file));
            Directory.CreateDirectory(dataDirectory);
            info.EnvironmentVariables["ANDROID_ADB_LOG_PATH"] = Path.Combine(dataDirectory, "adb.log");
            // Windows ADB obtains its authorization directory using the native user-profile API.
            // ANDROID_USER_HOME does not relocate ADB's Windows keys; leave upstream storage intact.
            return info;
        }

        public static async Task<CommandResult> RunAsync(string file, IEnumerable<string> args, string dataDirectory, int timeoutMilliseconds)
        {
            using (var process = new Process { StartInfo = StartInfo(file, args, dataDirectory) })
            {
                process.Start();
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                var finished = await Task.Run(() => process.WaitForExit(timeoutMilliseconds));
                if (!finished)
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    // A spawned daemon may retain pipe handles; never wait on output indefinitely.
                    throw new TimeoutException("The USB connection tool did not respond. Reconnect the phone and try Refresh.");
                }
                var drained = Task.WhenAll(stdout, stderr);
                if (await Task.WhenAny(drained, Task.Delay(2000)) != drained) throw new TimeoutException("The USB connection tool did not finish returning its output.");
                await drained;
                return new CommandResult { ExitCode = process.ExitCode, Output = stdout.Result, Error = stderr.Result };
            }
        }

        public static List<string> BuildSessionArguments(DeviceInfo device, AppSettings settings)
        {
            return BuildSessionArguments(device, settings, SessionPlan.Create(device, settings, false));
        }

        public static List<string> BuildSessionArguments(DeviceInfo device, AppSettings settings, SessionPlan plan)
        {
            if (device == null || !device.IsReady) throw new InvalidOperationException("Select an authorized USB phone first.");
            settings.Normalize();
            var fps = plan.LegacyVideo ? Math.Min(settings.FrameRate, 30) : settings.FrameRate;
            var args = new List<string> { "--serial=" + device.Serial, "--window-title=DeX Bridge", "--video-codec=h264", plan.LegacyVideo ? "--video-bit-rate=8M" : "--video-bit-rate=16M", "--max-fps=" + fps.ToString(CultureInfo.InvariantCulture), "--shortcut-mod=lalt" };
            if (device.SdkVersion >= 29) args.Add("--keep-active");
            if (plan.DesktopMode)
            {
                var dpi = settings.Resolution == "2560x1440" ? 380 : settings.Resolution == "1280x720" ? 190 : 284;
                args.Add("--new-display=" + settings.Resolution + "/" + dpi);
                args.Add("--no-vd-destroy-content");
            }
            else
            {
                var width = Int32.Parse(settings.Resolution.Split('x')[0], CultureInfo.InvariantCulture);
                if (plan.LegacyVideo) width = Math.Min(width, 1280);
                args.Add("--max-size=" + width.ToString(CultureInfo.InvariantCulture));
            }
            if (settings.Fullscreen) args.Add("--fullscreen");
            if (!plan.Audio) args.Add("--no-audio");
            if (!settings.Clipboard) args.Add("--no-clipboard-autosync");
            if (plan.PhysicalKeyboard) args.Add("--keyboard=uhid");
            // Absolute SDK mouse targets the virtual display without capturing the PC cursor.
            args.Add("--mouse=sdk");
            if (plan.DesktopMode) args.Add("--mouse-bind=++++:bhsn");
            return args;
        }
    }

    public sealed class UsbBackend
    {
        public string ToolsDirectory { get; private set; }
        public string DataDirectory { get; private set; }
        public string AdbPath { get { return Path.Combine(ToolsDirectory, "adb.exe"); } }
        public string ScrcpyPath { get { return Path.Combine(ToolsDirectory, "scrcpy.exe"); } }
        public bool IsInstalled { get { return File.Exists(AdbPath) && File.Exists(ScrcpyPath) && File.Exists(Path.Combine(ToolsDirectory, "scrcpy-server")); } }

        public UsbBackend(string toolsDirectory, string dataDirectory)
        { ToolsDirectory = toolsDirectory; DataDirectory = dataDirectory; }

        public async Task<List<DeviceInfo>> ScanAsync()
        {
            if (!IsInstalled) throw new FileNotFoundException("Connection tools are missing. Choose the bundled tools folder in Diagnostics.");
            var result = await ProcessTools.RunAsync(AdbPath, new[] { "devices", "-l" }, DataDirectory, 7000);
            if (result.ExitCode != 0) throw new InvalidOperationException("USB device detection failed. " + result.Error.Trim());
            return DeviceInfo.ParseList(result.Output);
        }

        public async Task ReadPropertiesAsync(DeviceInfo device)
        {
            var result = await ProcessTools.RunAsync(AdbPath, new[] { "-s", device.Serial, "shell", "getprop" }, DataDirectory, 5000);
            if (result.ExitCode == 0) device.ApplyProperties(result.Output);
        }

        public async Task<bool> IsUsbDeviceAsync(DeviceInfo device)
        {
            var result = await ProcessTools.RunAsync(AdbPath, new[] { "-s", device.Serial, "get-devpath" }, DataDirectory, 5000);
            var devpath = result.Output.Trim();
            if (result.ExitCode == 0 && devpath.StartsWith("usb:", StringComparison.OrdinalIgnoreCase)) return true;
            if (devpath.Length > 0 && !devpath.Equals("unknown", StringComparison.OrdinalIgnoreCase)) return false;
            // The Windows ADB USB backend may report "unknown" even for a wired device.
            // -d selects a USB transport exclusively, so matching its serial is independent proof.
            // Multiple USB phones produce an ADB error here; never guess which one was selected.
            var usb = await ProcessTools.RunAsync(AdbPath, new[] { "-d", "get-serialno" }, DataDirectory, 5000);
            return usb.ExitCode == 0 && usb.Output.Trim() == device.Serial;
        }
    }
}
