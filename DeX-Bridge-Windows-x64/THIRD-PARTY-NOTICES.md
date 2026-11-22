# Third-party notices

DeX Bridge bundles the official, unmodified Windows x64 release of scrcpy **5.0.1**, downloaded from https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1.

Archive SHA-256: `b12a2c4ee8be317422451fc7dcf8ee20a71b5ea7ef9ad73ddd825a25316227a5`.

- **scrcpy client and Android server**, copyright Genymobile and contributors, Apache License 2.0. The upstream `LICENSE.txt` is included in `tools`. Source: https://github.com/Genymobile/scrcpy/tree/v5.0.1
- **Android Debug Bridge / Android platform tools**, copyright The Android Open Source Project and contributors. Included upstream binary from the scrcpy release. Source and applicable notices: https://android.googlesource.com/platform/packages/modules/adb/ and https://android.googlesource.com/platform/system/core/
- **FFmpeg libraries** (`avcodec`, `avformat`, `avutil`, `swresample`), copyright FFmpeg contributors. LGPL 2.1 or later applies to the upstream scrcpy Windows build. See the FFmpeg license and compliance guidance: https://ffmpeg.org/legal.html. Source: https://ffmpeg.org/download.html. These remain separate dynamically linked DLLs; replacement is not restricted by DeX Bridge.
- **SDL 3**, copyright Sam Lantinga and contributors, zlib license. Source: https://github.com/libsdl-org/SDL
- **libusb**, copyright libusb contributors, LGPL 2.1 or later. Source: https://github.com/libusb/libusb

Additional upstream license texts are included in `tools/licenses`. The bundled software remains subject to its original licenses. DeX Bridge adds no restrictions on replacing or debugging these third-party libraries.

Samsung, Galaxy, and DeX are trademarks of Samsung Electronics. Product names describe compatibility and do not imply endorsement.
