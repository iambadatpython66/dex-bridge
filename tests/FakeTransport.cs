using System;
using System.IO;
using System.Threading;
using System.Linq;

// Process-level USB fixtures. These binaries are used only by tests and never shipped.
internal static class FakeTransport
{
    private static int Main(string[] args)
    {
        if (Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]).Equals("adb", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 0 && args[0] == "devices")
            {
                Console.WriteLine("List of devices attached");
                var state = Environment.GetEnvironmentVariable("DEXBRIDGE_TEST_STATE") ?? "device";
                if (state == "error") { Console.Error.WriteLine("fixture transport error"); return 1; }
                if (state != "none") Console.WriteLine("R58T24ULTRA\t" + state + " usb:1-2 product:test model:Galaxy_S24_Ultra transport_id:1");
                Console.WriteLine("192.168.1.20:5555 device product:wireless model:Wireless_Phone transport_id:2");
                Console.WriteLine("adb-test._adb-tls-connect._tcp. device transport_id:3");
                return 0;
            }
            if (Array.IndexOf(args, "get-devpath") >= 0)
            {
                var mode = Environment.GetEnvironmentVariable("DEXBRIDGE_TEST_USB");
                Console.WriteLine(mode == "false" ? "local:wireless" : mode == "unknown" ? "unknown" : "usb:1-2"); return 0;
            }
            if (args.Length == 2 && args[0] == "-d" && args[1] == "get-serialno")
            {
                Console.WriteLine(Environment.GetEnvironmentVariable("DEXBRIDGE_TEST_USB_SERIAL") ?? "R58T24ULTRA"); return 0;
            }
            if (Array.IndexOf(args, "getprop") >= 0)
            {
                var profile = Environment.GetEnvironmentVariable("DEXBRIDGE_TEST_PROFILE") ?? "galaxy";
                string model = "SM-S928B", maker = "samsung", sdk = "37", android = "17", oneui = "90000";
                if (profile == "legacy") { model = "SM-J200F"; sdk = "21"; android = "5.0"; oneui = ""; }
                if (profile == "old-galaxy") { model = "SM-S901B"; sdk = "34"; android = "14"; oneui = "60100"; }
                if (profile == "pixel") { model = "Pixel 9"; maker = "Google"; oneui = ""; }
                if (profile == "xiaomi") { model = "Xiaomi 14"; maker = "Xiaomi"; sdk = "35"; android = "15"; oneui = ""; }
                if (profile == "too-old") { model = "GT-I9300"; sdk = "19"; android = "4.4"; oneui = ""; }
                Console.WriteLine("[ro.product.model]: [" + model + "]");
                Console.WriteLine("[ro.product.manufacturer]: [" + maker + "]");
                Console.WriteLine("[ro.build.version.release]: [" + android + "]");
                Console.WriteLine("[ro.build.version.sdk]: [" + sdk + "]");
                Console.WriteLine("[ro.build.version.oneui]: [" + oneui + "]");
                return 0;
            }
            if (Array.IndexOf(args, "roundtrip") >= 0)
            {
                for (int i = 3; i < args.Length; i++) Console.WriteLine(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(args[i])));
                return 0;
            }
            if (Array.IndexOf(args, "timeout") >= 0) { Thread.Sleep(10000); return 0; }
            return 0;
        }
        var output = Environment.GetEnvironmentVariable("DEXBRIDGE_TEST_ARGUMENTS");
        if (!String.IsNullOrEmpty(output)) File.WriteAllLines(output, args);
        if (Environment.GetEnvironmentVariable("DEXBRIDGE_TEST_FAIL") == "true") { Console.Error.WriteLine("ERROR: virtual display fixture failure"); return 3; }
        bool desktop = args.Any(x => x.StartsWith("--new-display="));
        if (desktop && Environment.GetEnvironmentVariable("DEXBRIDGE_TEST_FAIL_DESKTOP") == "true") { Console.Error.WriteLine("ERROR: this fixture has no desktop launcher"); return 3; }
        if (desktop && Environment.GetEnvironmentVariable("DEXBRIDGE_TEST_BLANK_DESKTOP") == "true") { Thread.Sleep(30000); return 0; }
        Console.WriteLine("INFO: Texture: 1920x1080");
        Console.Out.Flush();
        Thread.Sleep(30000);
        return 0;
    }
}
