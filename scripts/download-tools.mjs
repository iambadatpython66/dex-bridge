import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const version = 'v5.0.1';
const expected = 'b12a2c4ee8be317422451fc7dcf8ee20a71b5ea7ef9ad73ddd825a25316227a5';
const destination = path.join(root, 'build-cache', `scrcpy-win64-${version}.zip`);
await fs.mkdir(path.dirname(destination), { recursive: true });
const response = await fetch(`https://github.com/Genymobile/scrcpy/releases/download/${version}/scrcpy-win64-${version}.zip`);
if (!response.ok) throw new Error(`Download failed: HTTP ${response.status}`);
const data = Buffer.from(await response.arrayBuffer());
const actual = crypto.createHash('sha256').update(data).digest('hex');
if (actual !== expected) throw new Error(`SHA-256 mismatch: ${actual}`);
await fs.writeFile(destination, data);
console.log(`Verified scrcpy ${version}: ${destination}`);
const licenses = [
  ['SDL-LICENSE.txt', 'https://raw.githubusercontent.com/libsdl-org/SDL/main/LICENSE.txt', false],
  ['libusb-LICENSE.txt', 'https://raw.githubusercontent.com/libusb/libusb/master/COPYING', false],
  ['FFmpeg-LGPL-2.1.txt', 'https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/COPYING.LGPLv2.1', false],
  ['ADB-NOTICE.txt', 'https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/NOTICE?format=TEXT', true],
];
const licenseDirectory = path.join(root, 'build-cache', 'licenses');
await fs.mkdir(licenseDirectory, { recursive: true });
for (const [name, url, base64] of licenses) {
  const licenseResponse = await fetch(url);
  if (!licenseResponse.ok) throw new Error(`License download failed: ${name}, HTTP ${licenseResponse.status}`);
  const text = await licenseResponse.text();
  await fs.writeFile(path.join(licenseDirectory, name), base64 ? Buffer.from(text, 'base64') : text);
  console.log(`Included license: ${name}`);
}
