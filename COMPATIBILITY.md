# Compatibility

DeX Bridge supports the screen-mirroring path of Android 5.0+ devices with working USB ADB, video encoding, and input injection. No Galaxy model ID is required. Firmware, managed-device rules, USB drivers, and additional manufacturer permissions can still restrict a device.

## Auto mode

1. Read Android SDK and One UI version from the connected phone.
2. Use phone mirroring for unknown desktop capability, older One UI, and other Android manufacturers.
3. On One UI 8+ / Android 10+, try a virtual display as a candidate for Samsung DeX.
4. If that session exits with an error or produces no video frame for 12 seconds, close it and try phone mirroring once.
5. Report the active session without claiming that its video proves a DeX launcher is present.

Manual close, changing the selected mode, switching devices, and closing the app cancel a queued automatic fallback. Mirroring failure produces diagnostics rather than an endless retry loop.

## Feature requirements

| Feature | Requirement |
| --- | --- |
| Mirror existing phone screen | Android 5.0+, USB debugging, device permits capture |
| Virtual display | Android 10+, device supplies a usable launcher |
| Samsung DeX | Compatible Samsung model and firmware; not created by this app |
| Phone audio | Android 11+, app permits audio capture |
| Physical keyboard mode | Enabled on Android 10+; kernel/OEM permissions still apply |
| Basic keyboard/mouse input | ADB input injection permitted; some OEMs require an extra security option |
| Windows receiver | Windows 10/11 x64, .NET Framework 4.8 |

Android 5–9 uses H.264 at up to 1280 pixels, 30 fps and 8 Mbps; audio and physical keyboard simulation are disabled. The phone's aspect ratio is preserved. Android 10 disables audio but otherwise uses the selected video settings. Unknown software details conservatively use mirroring with audio and physical keyboard simulation off.

All-model support means this general Android connection path, not a promise that every phone has Samsung DeX. iOS, non-Android operating systems, devices below Android 5, and firmware that blocks USB debugging are not supported by scrcpy's ADB transport.

## Evidence and limits

The implementation is checked with manufacturer/Android capability fixtures and child-process tests. The upstream scrcpy 5.0.1 documentation specifies Android API 21+ for mirroring and API 30+ for audio. Real hardware compatibility, including the user's S24 Ultra on One UI 9, must still be confirmed on the relevant device. No physical device matrix is claimed.
