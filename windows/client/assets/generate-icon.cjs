'use strict';
// Regenerate the multi-resolution Windows ICO from the shared brand.svg (npm package: sharp).
const fs = require('node:fs/promises');
const path = require('node:path');
const sharp = require(process.env.FLOWPIVOT_SHARP || 'sharp');
async function main() {
  const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
  const mark = path.resolve(__dirname, '../../../brand.svg');
  const images = await Promise.all(sizes.map(size => sharp(mark, {density: 384}).resize(size, size).png().toBuffer()));
  const header = Buffer.alloc(6 + 16 * sizes.length);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  for (const [i, size] of sizes.entries()) {
    const entry = 6 + i * 16;
    header[entry] = header[entry + 1] = size % 256;
    header.writeUInt16LE(1, entry + 4);
    header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(images[i].length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += images[i].length;
  }
  await fs.writeFile(path.join(__dirname, 'FlowPivot.ico'), Buffer.concat([header, ...images]));
}
main().catch(error => { console.error(error); process.exitCode = 1; });
