import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const assetsDir = path.join(__dirname, '../src/assets');
const publicAssetsDir = path.join(__dirname, '../public/assets');

// Copy asset files to public directory
fs.mkdirSync(publicAssetsDir, { recursive: true });
for (const file of ['logo.png', 'signature_stamp.png', 'stamp2.png']) {
  const src = path.join(assetsDir, file);
  const dest = path.join(publicAssetsDir, file);
  if (fs.existsSync(src)) {
    fs.copyFileSync(src, dest);
  } else {
    console.warn(`Warning: Source asset not found: ${src}`);
  }
}

// Only generate asset-urls.ts if it does not already exist
const outPath = path.join(__dirname, '../src/app/core/constants/asset-urls.ts');
if (!fs.existsSync(outPath)) {
  const content = `export interface AssetUrls {
  logo: string;
  signatureStamp: string;
  seriesStamp: string;
}

export const ASSET_URLS: AssetUrls = {
  logo: '/assets/logo.png',
  signatureStamp: '/assets/signature_stamp.png',
  seriesStamp: '/assets/stamp2.png'
};
`;
  fs.writeFileSync(outPath, content, 'utf8');
  console.log('Created', outPath);
} else {
  console.log('Skipped (already exists):', outPath);
}

console.log('Asset embedding complete.');
