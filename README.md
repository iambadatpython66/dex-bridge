# DeX Bridge for Windows

Connect an Android phone to a Windows laptop over a USB data cable, then use the laptop's display, keyboard, and mouse. **Auto** chooses a compatible session and falls back to phone mirroring if a virtual desktop fails.

[Download the Windows app](https://github.com/iambadatpython66/dex-bridge/releases/tag/v1.1.0) · [Compatibility](COMPATIBILITY.md) · [Validation](TESTING.md)

## Run

1. Download the portable Windows x64 ZIP from Releases and extract it into a writable folder. Keep `tools` beside `DeXBridge.exe`.
2. Run **DeXBridge.exe**. Windows 10/11 x64 and .NET Framework 4.8 are required.
3. Enable developer options on the phone: find **Build number** under About phone, sometimes inside Software information, and tap it seven times. Find Developer options in Settings and enable **USB debugging**.
4. Connect a USB data cable, unlock the phone, and tap **Allow** on the debugging prompt.
5. Leave **Auto** selected and click **Connect phone**.

The app has no installer, Samsung account, subscription, or phone app installation. Your PC remains powered on and runs Windows during the session. A laptop's USB-C port is used for USB data; it is not converted into a DisplayPort input.

When upgrading from v1.0 in the same folder, your previous manual mode is preserved. Select **Auto** once to enable the new capability selection and fallback behavior.

## Device support

Phone-screen mirroring supports Android 5.0+ devices that permit ADB debugging and screen capture, without a Samsung model whitelist. That includes Samsung Galaxy S, Note, Z, A, M, F and tablet families, as well as Android phones from other manufacturers; actual availability depends on the device's Android implementation and debugging permissions.

**Samsung DeX is not available on every model.** Samsung's firmware must provide it. Auto tries a virtual desktop on devices reporting One UI 8+ and Android 10+, then switches to mirroring if the desktop exits with an error or produces no video frame for 12 seconds. This is a capability candidate, not proof that a model supports DeX. A working video stream with an unsuitable launcher still needs a manual switch to **Phone screen**.

| Device software | Default behavior |
| --- | --- |
| Android 5–9 | Phone mirroring; basic keyboard; up to 1280 pixels and 30 fps; audio off |
| Android 10 | Phone mirroring; physical keyboard option; audio off |
| Android 11+ | Phone mirroring, keyboard/mouse and optional audio |
| One UI 8+ on Android 10+ | Try virtual desktop; automatically fall back to mirroring |
| Older One UI / unrecognized desktop capability | Phone mirroring |

**DeX / desktop** remains a manual option for Android 10+ devices with a usable launcher on a virtual display. It does not install desktop firmware. iPhones and non-Android devices are outside this ADB-based connection method.

## Controls

| Action | Shortcut |
| --- | --- |
| Toggle fullscreen | F11 |
| Close display | Alt + Q |
| Phone Home / Back | Alt + H / Alt + B |
| Configure physical keyboard layout | Alt + K |
| Copy / paste in compatible apps | Ctrl + C / Ctrl + V |
| Desktop Back with mouse | Shift + right-click |

The mouse uses absolute input, so the PC cursor remains available. Physical keyboard input is used on Android 10+ when enabled; older phones use basic Android input. **Close display** ends the owned session, and **Show display** brings it forward.

## Troubleshooting

- **No phone:** use a data cable, try another USB port, unlock the phone, and select Transferring files if needed. Install your manufacturer's USB driver if Windows cannot detect it: https://developer.android.com/studio/run/oem-usb
- **Unauthorized:** allow the debugging prompt. If it is missing, revoke USB debugging authorizations and reconnect. Samsung Auto Blocker or managed-device settings can restrict debugging.
- **Keyboard/mouse blocked:** some brands, especially Xiaomi, require a separate **USB debugging (Security settings)** permission and a reboot. If physical keyboard input fails, disable that option and reconnect.
- **Blank or unusable desktop:** Auto handles startup errors and a missing first frame. If an unsuitable desktop launcher still produces video, close it and choose Phone screen.
- **Audio failure:** audio requires Android 11+, but individual apps can refuse capture. Turn off Play phone audio and reconnect.
- **Poor video:** lower the resolution and frame rate. Protected content may appear black. Frame rate is a limit, not a performance promise.
- **Windows unknown USB path:** the app verifies the selected phone with ADB's USB-only serial query. Connect only one USB phone if that check is ambiguous.
- **ADB cannot create `.android`:** run normally from your Windows desktop with a standard user profile. Managed sandboxes may restrict the native profile lookup that ADB requires.

Diagnostics contains the actual ADB and scrcpy output. The app's log stays in memory unless saved. Preferences are stored in `Data/settings.xml`, and the ADB server log goes to `Data/adb.log`. Windows ADB manages authorization keys under your user profile's `.android` folder. Do not upload runtime settings, logs, or authorization keys.

## Build and test

Windows provides the .NET Framework compiler used by this project. Node is needed only to download the verified build dependencies.

```powershell
node scripts\download-tools.mjs
Expand-Archive -LiteralPath build-cache\scrcpy-win64-v5.0.1.zip -DestinationPath build-cache\scrcpy -Force
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-tests.ps1
```

The download script verifies scrcpy's published SHA-256 and includes third-party license notices. `dist/DeX-Bridge/DeXBridge.exe` is the executable, and `dist/DeX-Bridge-Windows-x64.zip` is the portable package. The package excludes runtime preferences, logs, and ADB keys.

GitHub Actions builds and tests the Windows app. A `v*` tag publishes its verified ZIP as a prerelease. The original uploaded v1.0 ZIP and extracted folder are retained for history; use Releases for the updated build.

## Credits

Powered by the official [scrcpy](https://github.com/Genymobile/scrcpy) project and Android Debug Bridge. This independent app is unaffiliated with Samsung or Genymobile. See [third-party notices](THIRD-PARTY-NOTICES.md).
