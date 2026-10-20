import { packagePwa } from '../../Tools/TTB/package-pwa.mjs';
import { writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
await writeFile(new URL('../dist/index.html',import.meta.url),'<!doctype html><html lang="th"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><title>MANGOs · Gallery</title><link rel="stylesheet" href="./mango-ttb.css"></head><body><div id="mango-ttb-root"></div><script>window.MANGOS_TTB_CONFIG={pwaEnabled:true};</script><script src="./mango-ttb.js"></script></body></html>');

await packagePwa(fileURLToPath(new URL('../dist/',import.meta.url)));
