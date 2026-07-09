import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const assetsDir = path.join(__dirname, '../src/assets');
const publicAssetsDir = path.join(__dirname, '../public/assets');

fs.mkdirSync(publicAssetsDir, { recursive: true });
for (const file of ['logo.jpeg', 'signature_stamp.jpeg', 'stamp2.jpeg']) {
  fs.copyFileSync(path.join(assetsDir, file), path.join(publicAssetsDir, file));
}

const content = `export interface AssetUrls {
  logo: string;
  signatureStamp: string;
  seriesStamp: string;
}

export const ASSET_URLS: AssetUrls = {
  logo: '/assets/logo.jpeg',
  signatureStamp: '/assets/signature_stamp.jpeg',
  seriesStamp: '/assets/stamp2.jpeg'
};
`;

const outPath = path.join(__dirname, '../src/app/core/constants/asset-urls.ts');
fs.writeFileSync(outPath, content, 'utf8');
console.log('Copied print assets to public/assets and wrote', outPath);
