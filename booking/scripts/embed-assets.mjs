import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const assetsDir = path.join(__dirname, '../src/assets');
const publicAssetsDir = path.join(__dirname, '../public/assets');

fs.mkdirSync(publicAssetsDir, { recursive: true });
for (const file of ['logo.png', 'signature_stamp.png', 'stamp2.png']) {
  fs.copyFileSync(path.join(assetsDir, file), path.join(publicAssetsDir, file));
}

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

const outPath = path.join(__dirname, '../src/app/core/constants/asset-urls.ts');
fs.writeFileSync(outPath, content, 'utf8');
console.log('Copied print assets to public/assets and wrote', outPath);
