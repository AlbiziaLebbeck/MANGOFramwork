(() => {
  'use strict';
  const ua = navigator.userAgent;
  const ipad = /iPad/i.test(ua) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  const family = ipad || /Tablet|Android(?!.*Mobile)/i.test(ua) ? 'tablet'
    : /iPhone|iPod|Android.*Mobile|Windows Phone/i.test(ua) ? 'phone' : 'desktop';
  let mode = 'auto';
  try { mode = localStorage.getItem('mangos.inputMode') || 'auto'; } catch (_) {}
  if (!['auto', 'touch', 'desktop'].includes(mode)) mode = 'auto';
  let externalInput = false, ready = false, canFly = false, focused = false, lastSignature = '';
  let orientation = innerHeight > innerWidth ? 'portrait' : 'landscape';
  const pointers = new Map();
  let lookId = null, lookX = 0, lookY = 0, lookToken = 0;
  const input = { x: 0, y: 0, lx: 0, ly: 0, jump: false, sprint: false, fly: false, vertical: 0, lookToken: 0, px: 0, py: 0 };
  const touch = () => mode === 'touch' || (mode === 'auto' && family !== 'desktop' && !externalInput);
  const send = (method, data) => {
    if (window.unityInstance) window.unityInstance.SendMessage('MangosBrowserBridge', method, data);
  };
  const reset = () => {
    pointers.clear(); lookId = null;
    Object.assign(input, { x: 0, y: 0, lx: 0, ly: 0, jump: false, sprint: false, fly: false, vertical: 0 });
    document.querySelectorAll('#mangos-controls .is-pressed').forEach(e => e.classList.remove('is-pressed'));
    const knob = document.getElementById('mangos-stick-knob');
    if (knob) knob.style.transform = 'translate(0px,0px)';
    lastSignature = '';
    send('OnInput', JSON.stringify(input));
  };
  const flags = () => (family === 'phone' ? 1 : family === 'tablet' ? 2 : 0) | (orientation === 'portrait' ? 4 : 0) | (touch() ? 8 : 0);
  const update = (rotation = false) => {
    // The software keyboard can resize the viewport without rotating the device.
    const nextOrientation = focused && !rotation ? orientation : innerHeight > innerWidth ? 'portrait' : 'landscape';
    const root = document.documentElement;
    const changed = orientation !== nextOrientation || root.dataset.mangosInput !== (touch() ? 'touch' : 'desktop');
    orientation = nextOrientation;
    Object.assign(root.dataset, { mangosDevice: family, mangosOrientation: orientation, mangosInput: touch() ? 'touch' : 'desktop' });
    if (changed) reset();
    const controls = document.getElementById('mangos-controls');
    if (controls) {
      controls.hidden = !ready;
      controls.dataset.touch = String(touch() && !focused);
      controls.querySelectorAll('[data-flying]').forEach(e => { e.hidden = !canFly; });
    }
    send('OnDeviceChanged', String(flags()));
  };
  window.MangosController = {
    flags,
    getState: () => ({ family, orientation, mode, touch: touch(), ready, canFly, focused }),
    setMode(value) {
      if (!['auto', 'desktop', 'touch'].includes(value)) return;
      mode = value; externalInput = false;
      try { localStorage.setItem('mangos.inputMode', mode); } catch (_) {}
      const select = document.getElementById('mangos-input-mode');
      if (select) select.value = mode;
      reset(); update();
    },
    setPlayer(state) { ready = !!state.ready; canFly = !!state.canFly; reset(); update(); },
    reset,
  };
  const isTyping = element => !!element && (element.matches('input,textarea,select') || element.isContentEditable);
  const refreshFocus = () => {
    focused = isTyping(document.activeElement);
    reset(); update(); send('OnInputFocus', focused ? '1' : '0');
  };
  addEventListener('resize', () => update());
  addEventListener('orientationchange', () => { reset(); update(true); });
  if (screen.orientation) screen.orientation.addEventListener('change', () => update(true));
  addEventListener('blur', reset);
  document.addEventListener('visibilitychange', reset);
  document.addEventListener('focusin', refreshFocus);
  document.addEventListener('focusout', () => queueMicrotask(refreshFocus));
  addEventListener('keydown', event => {
    if (mode === 'auto' && !focused && /^(Key[WASD]|Arrow\w+|Space)$/.test(event.code) && touch()) {
      externalInput = true; update();
    }
  });
  addEventListener('pointerdown', event => {
    if (mode !== 'auto' || family === 'desktop' || isTyping(event.target)) return;
    const next = event.pointerType === 'mouse';
    if (externalInput !== next) { externalInput = next; update(); }
  }, true);
  function initialize() {
    const sheet = document.createElement('link'); sheet.rel = 'stylesheet'; sheet.href = 'mangos-controller.css'; document.head.appendChild(sheet);
    const controls = document.createElement('div'); controls.id = 'mangos-controls'; controls.hidden = true;
    controls.innerHTML = `<label id="mangos-mode-label">Controls <select id="mangos-input-mode" aria-label="Controller mode"><option value="auto">Auto</option><option value="desktop">Keyboard / mouse</option><option value="touch">Touch</option></select></label>
      <div id="mangos-touch-controls"><div id="mangos-stick" role="group" aria-label="Move joystick"><span id="mangos-stick-knob"></span></div>
      <div id="mangos-actions"><button data-action="sprint" aria-label="Run (hold)">Run</button><button data-action="jump">Jump</button>
      <button data-action="fly" data-flying>Fly</button><button data-action="up" data-flying aria-label="Fly up">↑</button><button data-action="down" data-flying aria-label="Fly down">↓</button></div>
      <span id="mangos-look-hint">Drag the right side to look</span></div>`;
    document.body.appendChild(controls);
    const select = document.getElementById('mangos-input-mode'); select.value = mode;
    select.addEventListener('change', () => { window.MangosController.setMode(select.value); select.blur(); });
    const stick = document.getElementById('mangos-stick'), knob = document.getElementById('mangos-stick-knob');
    function moveStick(event) {
      const rect = stick.getBoundingClientRect(), radius = rect.width * .36;
      let x = (event.clientX - rect.left - rect.width / 2) / radius, y = (event.clientY - rect.top - rect.height / 2) / radius;
      const length = Math.max(1, Math.hypot(x, y)); x /= length; y /= length;
      input.x = Math.abs(x) < .08 ? 0 : x; input.y = Math.abs(y) < .08 ? 0 : -y;
      knob.style.transform = `translate(${x * radius}px,${y * radius}px)`;
    }
    function recomputeButtons() {
      const active = new Set(pointers.values());
      input.jump = active.has('jump'); input.sprint = active.has('sprint'); input.fly = active.has('fly');
      input.vertical = Number(active.has('up')) - Number(active.has('down'));
      controls.querySelectorAll('[data-action]').forEach(e => e.classList.toggle('is-pressed', active.has(e.dataset.action)));
      send('OnInput', JSON.stringify(input));
    }
    stick.addEventListener('pointerdown', event => {
      if (!touch() || focused || [...pointers.values()].includes('stick')) return;
      event.preventDefault(); event.stopPropagation(); stick.setPointerCapture(event.pointerId); pointers.set(event.pointerId, 'stick'); moveStick(event);
    });
    stick.addEventListener('pointermove', event => { if (pointers.get(event.pointerId) === 'stick') moveStick(event); });
    controls.querySelectorAll('[data-action]').forEach(button => button.addEventListener('pointerdown', event => {
      if (!touch() || focused) return;
      event.preventDefault(); event.stopPropagation(); button.setPointerCapture(event.pointerId); pointers.set(event.pointerId, button.dataset.action); recomputeButtons();
    }));
    function release(event) {
      if (pointers.get(event.pointerId) === 'stick') { input.x = input.y = 0; knob.style.transform = 'translate(0px,0px)'; }
      pointers.delete(event.pointerId); recomputeButtons();
      if (lookId === event.pointerId) { lookId = null; input.lx = input.ly = 0; }
    }
    addEventListener('pointerup', release); addEventListener('pointercancel', release);
    controls.addEventListener('lostpointercapture', release);
    const canvas = document.getElementById('unity-canvas');
    if (canvas) {
      canvas.style.touchAction = 'none';
      canvas.addEventListener('pointerdown', event => {
        if (!ready || !touch() || focused || lookId !== null || event.clientX < innerWidth * .42) return;
        const rect = canvas.getBoundingClientRect();
        lookId = event.pointerId; lookX = event.clientX; lookY = event.clientY;
        input.lookToken = ++lookToken; input.px = (event.clientX - rect.left) / rect.width; input.py = 1 - (event.clientY - rect.top) / rect.height;
        canvas.setPointerCapture(event.pointerId);
      });
      canvas.addEventListener('pointermove', event => {
        if (lookId !== event.pointerId) return;
        input.lx += event.clientX - lookX; input.ly += lookY - event.clientY; lookX = event.clientX; lookY = event.clientY;
      });
      canvas.addEventListener('lostpointercapture', release);
    }
    update();
    function frame() {
      if (ready && touch() && !focused) {
        const signature = JSON.stringify(input);
        if (signature !== lastSignature || input.lx || input.ly) { send('OnInput', signature); lastSignature = signature; }
      }
      input.lx = input.ly = 0;
      requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
  }
  update();
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize); else initialize();
})();
