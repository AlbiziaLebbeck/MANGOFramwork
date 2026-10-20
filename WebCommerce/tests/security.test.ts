import { afterEach, describe, expect, it, vi } from 'vitest';
import { CommerceApi, StaleSessionError, downloadFilename, resolveApiBase } from '../src/api';
import { CommerceBridge, parseHostEvent } from '../src/bridge';
import { hasOpenUnpaidOrder } from '../src/types';
import { pwa } from '../src/pwa';

afterEach(()=>vi.unstubAllGlobals());
describe('payment boundary in browser',()=>{
  it('does not replay a financial network failure and permits an explicit retry with the same operation key',async()=>{
    const fetch=vi.fn().mockRejectedValueOnce(new TypeError('Failed to fetch')).mockResolvedValueOnce(new Response('{"id":"same-operation"}',{status:200}));vi.stubGlobal('fetch',fetch);
    const api=new CommerceApi('https://shop.test/ttb-commerce/v1');const headers={'Idempotency-Key':'stable-key'};
    await expect(api.request('/orders','POST',{quoteId:'q'},true,headers)).rejects.toBeInstanceOf(TypeError);expect(fetch).toHaveBeenCalledTimes(1);expect(pwa.state.offline).toBe(true);
    await api.request('/orders','POST',{quoteId:'q'},true,headers);expect(fetch).toHaveBeenCalledTimes(2);expect(fetch.mock.calls[1][1].headers['Idempotency-Key']).toBe('stable-key');expect(pwa.state.offline).toBe(false);
  });
  it('freezes new financial mutations while the current tab applies an update',async()=>{
    const fetch=vi.fn();vi.stubGlobal('fetch',fetch);const original=pwa.state;
    try{pwa.state={...original,applying:true};await expect(new CommerceApi('https://shop.test/ttb-commerce/v1').request('/orders','POST',{quoteId:'q'})).rejects.toMatchObject({code:'app_updating'});expect(fetch).not.toHaveBeenCalled()}finally{pwa.state=original}
  });
  it('keeps an active unpaid checkout from updating but releases the gate after cancel, expiry or payment',()=>{
    const now=Date.now(),order={state:'Open',paymentState:'Unpaid',expiresAt:new Date(now+60000).toISOString()};expect(hasOpenUnpaidOrder(order,now)).toBe(true);
    expect(hasOpenUnpaidOrder({...order,state:'Cancelled'},now)).toBe(false);expect(hasOpenUnpaidOrder({...order,paymentState:'Paid'},now)).toBe(false);expect(hasOpenUnpaidOrder(order,now+60001)).toBe(false);
  });
  it('pins API hosts and refuses foreign credentials, paths and URL tokens',()=>{
    expect(resolveApiBase('/ttb-commerce/v1','http://127.0.0.1:5176')).toBe('http://127.0.0.1:5176/ttb-commerce/v1');
    expect(resolveApiBase('https://ttb-api-test.mangosgo.com/ttb-commerce-test/v1','https://plus.mangosgo.com')).toContain('ttb-api-test');
    for(const value of ['https://evil.test/ttb-commerce/v1','https://user:secret@ttb-api.mangosgo.com/ttb-commerce/v1','/ttb-commerce/v1?token=private','/api/v1'])expect(()=>resolveApiBase(value,'https://plus.mangosgo.com')).toThrow();
  });
  it('cannot put a previous account response into a new account screen',async()=>{
    let complete!:(response:Response)=>void;vi.stubGlobal('fetch',vi.fn(()=>new Promise<Response>(resolve=>{complete=resolve})));
    const api=new CommerceApi('https://shop.test/ttb-commerce/v1');api.setSession('old-csrf');const pending=api.request('/orders');api.reset();api.setSession('new-csrf');
    complete(new Response(JSON.stringify({items:[{id:'previous-account-order'}]}),{status:200}));
    await expect(pending).rejects.toBeInstanceOf(StaleSessionError);
  });
  it('includes cookies, no-store and current CSRF on mutation, with no automatic financial retry',async()=>{
    const fetch=vi.fn().mockResolvedValue(new Response(JSON.stringify({id:'order'}),{status:200}));vi.stubGlobal('fetch',fetch);
    const api=new CommerceApi('https://shop.test/ttb-commerce/v1');api.setSession('csrf-value');await api.request('/orders','POST',{quoteId:'quote'},true,{'Idempotency-Key':'stable-operation'});
    expect(fetch).toHaveBeenCalledTimes(1);const options=fetch.mock.calls[0][1];expect(options.credentials).toBe('include');expect(options.cache).toBe('no-store');expect(options.headers['X-CSRF-Token']).toBe('csrf-value');expect(options.headers['Idempotency-Key']).toBe('stable-operation');
  });
  it('invalidates authentication on owned 401 without trusting error text as HTML',async()=>{
    vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response('{"code":"login_required","title":"<script>bad</script>"}',{status:401})));
    const api=new CommerceApi('https://shop.test/ttb-commerce/v1');const invalidate=vi.fn();api.onUnauthorized=invalidate;await expect(api.request('/orders')).rejects.toMatchObject({code:'login_required'});expect(invalidate).toHaveBeenCalledOnce();
  });
  it('preserves Thai download extension and confines suggested names to a safe basename',()=>{
    expect(downloadFilename("attachment; filename*=UTF-8''%E0%B9%84%E0%B8%9F%E0%B8%A5%E0%B9%8C.zip")).toBe('ไฟล์.zip');
    expect(downloadFilename('attachment; filename="../../private/order.pdf"')).toBe('order.pdf');expect(downloadFilename("attachment; filename*=UTF-8''bad%0Aname.zip")).toBeNull();expect(downloadFilename("attachment; filename*=UTF-8''%invalid")).toBeNull();
  });
});
describe('Unity bridge is presentation only',()=>{
  it('withholds native calls before scene readiness and after scene unload',()=>{
    const canvas={dataset:{}};vi.stubGlobal('document',{querySelector:()=>canvas});
    const bridge=new CommerceBridge();const SendMessage=vi.fn();bridge.attachUnity({SendMessage});bridge.send('world.tap',{x:.5,y:.5});expect(SendMessage).not.toHaveBeenCalled();
    bridge.receive({version:1,type:'host.ready'});bridge.send('world.tap',{x:.5,y:.5});expect(SendMessage).toHaveBeenCalledTimes(1);expect(canvas.dataset).toMatchObject({commerceReady:'true'});
    bridge.receive({version:1,type:'host.stopped'});bridge.send('game.ticket',{ticket:'never-send-to-missing-receiver'});expect(SendMessage).toHaveBeenCalledTimes(1);expect(canvas.dataset).toMatchObject({commerceReady:'false'});
  });
  it('accepts only bounded native challenges and never imports game identity or a secret',()=>{
    const challenge={version:1,type:'game.challenge',requestId:'a'.repeat(32),challenge:'b'.repeat(64),serverId:'mangos-ttb',entitlementId:'00000000-0000-0000-0000-000000000001'};
    expect(parseHostEvent({...challenge,shopperId:'forged-owner',key:'private-service-key'})).toEqual(challenge);
    for(const bad of [{...challenge,serverId:'https://foreign.test'},{...challenge,challenge:'x'.repeat(500)},{...challenge,entitlementId:'../owner'},{version:1,type:'game.ready',ready:'true'},{version:1,type:'game.result',result:'payment-paid'}])expect(parseHostEvent(bad)).toBeNull();
    expect(parseHostEvent({version:1,type:'game.ready',ready:false})).toEqual({version:1,type:'game.ready',ready:false});
  });
  it('rejects invalid and unbounded messages instead of accepting a browser paid flag',()=>{
    for(const raw of ['{"version":1,"type":"payment.paid"}',{version:1,type:'product.open',modelKey:'../../secret'},{version:2,type:'host.ready'},{version:1,type:'product.open'},'x'.repeat(5000)])expect(parseHostEvent(raw)).toBeNull();
    expect(parseHostEvent({version:1,type:'product.open',modelKey:'orbit-lamp'})).toMatchObject({type:'product.open',modelKey:'orbit-lamp'});
    expect(parseHostEvent('{"version":1,"type":"product.open","modelKey":"orbit-lamp","productId":"","requestId":""}')).toEqual({version:1,type:'product.open',modelKey:'orbit-lamp'});
    expect(parseHostEvent('{"version":1,"type":"host.ready","modelKey":"","productId":"","requestId":""}')).toEqual({version:1,type:'host.ready'});
  });
});
