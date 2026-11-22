# DeX Bridge for Windows

A portable Windows app that uses a USB data cable to show a Galaxy phone's virtual desktop on your laptop, with keyboard, mouse, and audio. A separate **Phone screen** mode mirrors the phone's existing display.

## Run

1. Extract **DeX-Bridge-Windows-x64.zip** into a normal writable folder, keeping the `tools` folder beside the executable.
2. Double-click **DeXBridge.exe**. Windows 10/11 x64 and .NET Framework 4.8 are required (included on current Windows 10/11 installations).
3. On the phone: Settings → About phone → Software information → tap **Build number** seven times. Return to Settings → Developer options → enable **USB debugging**.
4. Connect a USB-C data cable. Unlock the phone and tap **Allow** on the USB debugging prompt.
5. Select the phone and click **Open desktop**. If the screen is blank or is not DeX, close it, choose **Phone screen**, and click **Mirror phone**.

There is no installer, admin requirement, Samsung account, Node requirement, or subscription. The app keeps its preferences in `Data/settings.xml` beside the executable. Windows ADB manages authorization keys in your normal Windows user profile's `.android` folder. ADB's server log is directed to `Data/adb.log`; the app's displayed diagnostic log remains in memory until you choose to save one. Neither authorization keys nor runtime settings are included in the release ZIP.

## Galaxy S24 Ultra / One UI 9

This app requests a virtual display using scrcpy 5.0.1. DeX launching on a virtual display is documented for One UI 8; **One UI 9 support has not been verified on a physical phone**. Samsung's software decides whether a DeX launcher appears. This app does not restore Samsung's discontinued DeX for PC service or convert a laptop's USB-C video output into a video input. Your laptop stays powered on and runs Windows throughout.

The desktop setting uses `--new-display=1920x1080/284` by default. Other choices adjust both resolution and display density. Phone-screen mode uses `--max-size` to limit the mirrored size while preserving the phone's aspect ratio. The FPS choice is an upper limit, not a promised frame rate. Protected media or apps that block capture can remain black, and some apps exclude their audio from capture.

## Controls

| Action | Shortcut |
| --- | --- |
| Toggle fullscreen | F11 |
| Close the display | Alt + Q |
| Phone Home / Back | Alt + H / Alt + B |
| Configure physical keyboard language | Alt + K |
| Copy / paste in compatible apps | Ctrl + C / Ctrl + V |
| Desktop Back with the mouse | Shift + right-click |

Physical keyboard mode supports the phone's configured layout. If input fails, disable it and reconnect. The mouse uses absolute input, so the PC cursor remains available. **Close display** ends the session gracefully, and **Show display** brings its window forward.

## Troubleshooting

- **No phone:** check the cable supports data, try another port, and select Transferring files on the phone if needed. A Samsung USB driver may be needed: https://developer.samsung.com/android-usb-driver
- **Unauthorized:** unlock and allow the debugging prompt. If it never appears, revoke USB debugging authorizations and reconnect. Review Samsung Auto Blocker if the phone blocks debugging.
- **Blank desktop:** close it and use Phone screen. Samsung's virtual-display launcher may have changed in your One UI version.
- **Video errors:** lower the resolution and frame rate. Disable audio if its capture fails.
- **Tools missing:** select `scrcpy.exe` in Diagnostics from a complete extracted scrcpy Windows bundle containing `adb.exe` and `scrcpy-server`.
- **Connection error:** Diagnostics contains the actual scrcpy / ADB output. Saved logs can include your device identifier; share them deliberately.
- **ADB cannot create `.android`:** launch the app normally from your Windows desktop using a standard user account. ADB requires access to the Windows user-profile API and its authorization folder; some managed sandboxes restrict that API.

The app filters wireless ADB endpoints and verifies USB transport before opening a session. When Windows reports an unknown USB device path, ADB's USB-only selection verifies the phone's serial; connect only one USB phone if that check is ambiguous. It does not install Android apps or change experimental desktop settings. The virtual display is released when the session ends. Apps are requested to move back to the main phone display; their actual behavior depends on Android and the app.

## Build from source

The .NET Framework compiler shipped with Windows is used; a .NET SDK is not required. Node is needed only to download dependencies during development.

```powershell
node scripts\download-tools.mjs
Expand-Archive -LiteralPath build-cache\scrcpy-win64-v5.0.1.zip -DestinationPath build-cache\scrcpy -Force
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-tests.ps1
```

Source files live in `src`. The native interface is embedded XAML. `dist/DeX-Bridge/DeXBridge.exe` is the built app and `dist/DeX-Bridge-Windows-x64.zip` is the portable package. The build explicitly excludes runtime preferences and ADB keys.

## Third-party software

Powered by [scrcpy](https://github.com/Genymobile/scrcpy) and Android Debug Bridge. This is an independent application, unaffiliated with Samsung or Genymobile. See `THIRD-PARTY-NOTICES.md` and the licenses in `tools`.
