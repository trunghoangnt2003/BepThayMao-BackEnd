import { copyFile, mkdir, open, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

// Dữ liệu ODbL do operator chọn; không tải một archive lớn hay gọi tile công cộng lúc runtime.
const archive = process.argv[2];
if (!archive || process.argv.length !== 3) throw new Error('Usage: node scripts/maps-prepare.mjs <archive.pmtiles>');
const input = await open(resolve(archive), 'r');
try {
  const header = Buffer.alloc(8);
  const { bytesRead } = await input.read(header, 0, 8, 0);
  if (bytesRead !== 8 || header.toString('ascii', 0, 7) !== 'PMTiles' || header[7] !== 3)
    throw new Error('Cần archive PMTiles v3 hợp lệ.');
} finally { await input.close(); }
const root = resolve('src/Bepthaymao.Api/maps');
const fonts = resolve(root, 'fonts/Noto Sans Regular');
await mkdir(fonts, { recursive: true });
await copyFile(resolve(archive), resolve(root, 'vietnam.pmtiles'));
for (const range of ['0-255', '256-511', '512-767', '768-1023', '7680-7935']) {
  const response = await fetch(`https://protomaps.github.io/basemaps-assets/fonts/Noto%20Sans%20Regular/${range}.pbf`);
  if (!response.ok) throw new Error(`Không tải được glyph ${range}: HTTP ${response.status}`);
  await writeFile(resolve(fonts, `${range}.pbf`), Buffer.from(await response.arrayBuffer()));
}
const license = await fetch('https://raw.githubusercontent.com/notofonts/noto-fonts/main/LICENSE');
if (!license.ok) throw new Error(`Không tải được giấy phép Noto Sans: HTTP ${license.status}`);
await writeFile(resolve(fonts, 'OFL.txt'), await license.text());
process.stdout.write('Đã chuẩn bị /maps/vietnam.pmtiles và glyph tiếng Việt tự host.\n');
