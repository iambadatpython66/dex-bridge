# Validation

103 checks passed for v1.1 using the built x64 app assembly, process-level ADB/scrcpy fixtures, and the production WPF visual tree.

Covered behaviors:

- USB device parsing; wireless and emulator exclusion; unauthorized and offline states.
- Samsung model, Android, and One UI 9 property parsing.
- Direct USB transport verification, plus Windows' unknown-device-path fallback using a matching USB-only serial.
- Desktop and phone-screen launch arguments, display density, audio, clipboard, fullscreen, and keyboard settings.
- Windows argument quoting with spaces, empty arguments, quotes, trailing backslashes, and shell metacharacters.
- Corrupt settings recovery, configuration persistence, external XML entity rejection, and invalid value normalization.
- Child process timeouts, starting, closing, reconnecting, failed startup, and clearing stale devices after a scan error.
- Native WPF connection, permission, missing-tools, running, error, Help, and Diagnostics states at 1100×790; the connection view also renders at the 900×640 minimum window size.
- Manufacturer capability fixtures for Samsung, Google, Xiaomi, OnePlus, Motorola, Sony, ASUS, Nokia, Nothing, and OPPO, without a model whitelist.
- Android SDK 21, 23, 26, 28, 29, 30, 33, and 37 feature selection; old Android video limits; safe unknown-version defaults.
- Actual child-process profiles for Pixel, Galaxy Android 5, older One UI, Xiaomi, and unsupported Android 4.4.
- Failed and frameless Auto desktop sessions fall back once to phone mirroring, with the active mode shown in the interface.
- Older settings preserve their manual desktop/phone preference.

The v1.0 interface received an independent review; its disabled-primary-button contrast finding was fixed and scored resolved. The v1.1 extension adds native renders for other manufacturers, legacy Android, unsupported Android, and automatic fallback. The primary action stays visible while connection content scrolls. Screenshots contain synthetic phone fixtures and are retained under `.impeccable/review`.

The independent v1.1 review inspected all 13 native renders and returned a ship disposition with no material fixes. This validates the presented UI states, not a physical phone compatibility matrix.

The actual bundled scrcpy 5.0.1 executable was run with `--version` and `--help` to verify the used options. Its downloaded ZIP matched the published SHA-256. A native startup smoke check opened the built app, reached an idle Windows message loop, and confirmed the responsive window titled **DeX Bridge**. Real ADB device enumeration could not run in the managed tool environment: its Windows user-profile lookup is restricted and ADB reports that it cannot create `\\.android`. This is recorded as a hardware-test limitation; launch the portable app normally on a Windows desktop to test the actual connection.

The portable archive was inspected and excludes runtime preferences, ADB authorization keys, test fixtures, and caches. Upstream tools and license notices are bundled.

Not verified: a physical Galaxy S24 Ultra connection, One UI 9 DeX launcher behavior, real phone keyboard/mouse/audio forwarding, protected content behavior, other Windows hardware or DPI settings. These require testing with the user's phone and laptop. The app presents the One UI version limit and offers Phone screen mode.
