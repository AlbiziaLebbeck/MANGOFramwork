import type { Page } from '@playwright/test';
import { inflateSync } from 'node:zlib';
// Launcher CanvasScaler uses matchWidthOrHeight=.5. Preserve native center anchors when DOM chrome reserves space.
export async function launcherPoint(page:Page,x:number,y:number){const canvas=await page.locator('#unity-canvas').boundingBox();if(!canvas)throw new Error('Native canvas missing');const scale=Math.sqrt(canvas.width*canvas.height/(1280*720));return{x:Math.round(canvas.x+canvas.width/2+(x-640)*scale),y:Math.round(canvas.y+canvas.height/2+(y-360)*scale)}}
export async function launcherPixel(page:Page,x:number,y:number){const point=await launcherPoint(page,x,y);return nativePixel(page,point.x,point.y)}
export async function launcherClick(page:Page,x:number,y:number){const point=await launcherPoint(page,x,y);await page.mouse.click(point.x,point.y,{delay:120})}
// Read a rendered one-pixel PNG for legacy native Unity UI that has no DOM accessibility node.
// A one-pixel first row has zero left/up predictors for every PNG filter; no image mutation is involved.
export async function nativePixel(page:Page,x:number,y:number):Promise<number[]>{
  const png=await page.screenshot({clip:{x,y,width:1,height:1}});const chunks:Buffer[]=[];let kind=2,depth=8,palette:Buffer|undefined;
  for(let offset=8;offset<png.length;){const size=png.readUInt32BE(offset);const tag=png.toString('ascii',offset+4,offset+8);const bytes=png.subarray(offset+8,offset+8+size);if(tag==='IHDR'){if(bytes.readUInt32BE(0)!==1||bytes.readUInt32BE(4)!==1)throw new Error('Expected a single rendered pixel');depth=bytes[8];kind=bytes[9]}if(tag==='IDAT')chunks.push(bytes);if(tag==='PLTE')palette=bytes;offset+=12+size}
  if(depth!==8)throw new Error('Unexpected screenshot PNG bit depth');const row=inflateSync(Buffer.concat(chunks));
  if(kind===0||kind===4)return [row[1],row[1],row[1]];if(kind===3&&palette)return [...palette.subarray(row[1]*3,row[1]*3+3)];if(kind===2||kind===6)return [row[1],row[2],row[3]];throw new Error('Unexpected screenshot color type');
}
