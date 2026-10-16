import { VOICE_OVERLAY_VERSION } from "./config.js";

export function createVoiceOverlayUi() {
  const icons = {
    check: `<svg viewBox="0 0 24 24" fill="none" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"></path></svg>`,
    door: `<svg viewBox="0 0 24 24" fill="none" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10 12h10"></path><path d="m17 9 3 3-3 3"></path><path d="M14 21H6a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h8"></path></svg>`,
    mic: `<svg viewBox="0 0 24 24" fill="none" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"></path><path d="M19 10v2a7 7 0 0 1-14 0v-2"></path><path d="M12 19v3"></path></svg>`,
    micOff: `<svg viewBox="0 0 24 24" fill="none" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m2 2 20 20"></path><path d="M9 9v3a3 3 0 0 0 5.1 2.1"></path><path d="M15 9.3V5a3 3 0 0 0-5.1-2.1"></path><path d="M19 10v2a7 7 0 0 1-.7 3"></path><path d="M5 10v2a7 7 0 0 0 10.7 6"></path><path d="M12 19v3"></path></svg>`,
    screen: `<svg viewBox="0 0 24 24" fill="none" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="4" width="18" height="12" rx="2"></rect><path d="M8 20h8"></path><path d="M12 16v4"></path></svg>`,
    camera: `<svg viewBox="0 0 24 24" fill="none" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m16 13 5 3V8l-5 3Z"></path><rect x="3" y="6" width="13" height="12" rx="2"></rect></svg>`,
    envelope: `<svg viewBox="0 0 24 24" fill="none" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="5" width="18" height="14" rx="2"></rect><path d="m3 7 9 6 9-6"></path></svg>`,
    muted: `<svg viewBox="0 0 24 24" fill="none" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m3 3 18 18"></path><path d="M9 9v3a3 3 0 0 0 4.5 2.6"></path><path d="M15 9.3V5a3 3 0 0 0-5.1-2.1"></path></svg>`,
    chevronLeft: `<svg viewBox="0 0 24 24" fill="none" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m15 18-6-6 6-6"></path></svg>`,
    chevronRight: `<svg viewBox="0 0 24 24" fill="none" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 18 6-6-6-6"></path></svg>`,
    info: `<svg viewBox="0 0 24 24" fill="none" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9"></circle><path d="M12 11v5"></path><path d="M12 8h.01"></path></svg>`,
    volumeOn: `<svg viewBox="0 0 24 24" fill="none" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 9v6h4l5 4V5L8 9H4Z"></path><path d="M16.5 8.5a5 5 0 0 1 0 7"></path><path d="M19 6a9 9 0 0 1 0 12"></path></svg>`,
    volumeOff: `<svg viewBox="0 0 24 24" fill="none" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 9v6h4l5 4V5L8 9H4Z"></path><path d="m17 9 4 6"></path><path d="m21 9-4 6"></path></svg>`,
    close: `<svg viewBox="0 0 24 24" fill="none" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m6 6 12 12"></path><path d="m18 6-12 12"></path></svg>`,
  };

  const style = document.createElement("style");
  style.textContent = `
    #voice-badge-container {
      position: fixed;
      top: 18px;
      left: 50%;
      z-index: 99999;
      display: flex;
      flex-wrap: wrap;
      gap: 10px;
      align-items: center;
      justify-content: center;
      width: min(80vw, calc(100vw - 32px));
      transform: translateX(-50%);
      pointer-events: none;
    }

    .voice-user-badge {
      position: relative;
      display: grid;
      place-items: center;
      width: 52px;
      height: 52px;
      flex: 0 0 auto;
      border: 3px solid rgba(255,255,255,0.72);
      border-radius: 999px;
      background: rgba(0,0,0,0.76);
      color: #fff;
      font: 700 14px/1 Arial, sans-serif;
      box-shadow: 0 5px 14px rgba(0,0,0,0.32);
      transition: border-color 120ms ease, box-shadow 120ms ease, transform 120ms ease;
      /* The container above is pointer-events: none so empty space around
         the badges doesn't block clicks/drags on the Unity canvas — opt
         each badge back in individually so hovering it still reaches the
         "title" tooltip (the participant's name) set in renderBadges(). */
      pointer-events: auto;
    }

    .voice-user-badge.is-speaking {
      border-color: #22C55E;
      box-shadow: 0 0 0 3px rgba(34,197,94,0.24), 0 5px 14px rgba(0,0,0,0.32);
      transform: translateY(-1px);
    }

    /* Instant custom tooltip (the participant's name), replacing the
       native "title" attribute's browser-controlled ~1-2s hover delay.
       Mirrors the same attr(data-tooltip) pattern already used for
       #voice-auth-status below, but opens downward since badges sit near
       the top of the screen. */
    .voice-user-badge::after {
      content: attr(data-tooltip);
      position: absolute;
      left: 50%;
      top: calc(100% + 8px);
      transform: translateX(-50%);
      display: none;
      width: max-content;
      max-width: 180px;
      padding: 6px 8px;
      border-radius: 6px;
      background: rgba(17,24,39,0.95);
      color: #fff;
      font: 700 12px/1 Arial, sans-serif;
      box-shadow: 0 8px 20px rgba(0,0,0,0.3);
      white-space: nowrap;
      pointer-events: none;
    }

    .voice-user-badge:hover::after,
    .voice-user-badge:focus-visible::after {
      display: block;
    }

    .voice-user-badge img {
      width: 100%;
      height: 100%;
      overflow: hidden;
      border-radius: 999px;
      object-fit: cover;
    }

    .voice-user-initials {
      display: grid;
      place-items: center;
      width: 100%;
      height: 100%;
      overflow: hidden;
      border-radius: 999px;
      background: #2563EB;
    }

    .voice-muted-indicator {
      position: absolute;
      right: -2px;
      bottom: -2px;
      display: none;
      place-items: center;
      width: 20px;
      height: 20px;
      border: 2px solid rgba(255,255,255,0.9);
      border-radius: 999px;
      background: #EF4444;
      color: #fff;
      font: 700 12px/1 Arial, sans-serif;
    }

    .voice-user-badge.is-muted .voice-muted-indicator {
      display: grid;
    }

    .voice-muted-indicator svg {
      width: 12px;
      height: 12px;
      stroke: currentColor;
    }

    #voice-ui-container {
      position: fixed;
      bottom: 20px;
      left: 20px;
      z-index: 99999;
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 10px;
      padding: 10px;
      border-radius: 8px;
      background: rgba(0, 0, 0, 0.75);
      color: white;
      font-family: Arial, sans-serif;
      box-shadow: 0 4px 10px rgba(0,0,0,0.3);
      backdrop-filter: blur(5px);
      /* Without a width cap, a row of collapsed circular buttons (mobile)
         has no explicit right edge and can run past the visible viewport
         with nothing to wrap it onto a second line — visually
         indistinguishable from the buttons just not being there. */
      max-width: calc(100vw - 40px);
      touch-action: manipulation;
      -webkit-user-select: none;
      user-select: none;
    }

    #voice-ui-container button {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      min-width: 0;
      height: 44px;
      border: 0;
      border-radius: 6px;
      padding: 0 14px;
      color: white;
      cursor: pointer;
      font-weight: 700;
      white-space: nowrap;
      transition: box-shadow 140ms ease, transform 140ms ease, filter 140ms ease;
      touch-action: manipulation;
      -webkit-tap-highlight-color: transparent;
      -webkit-user-select: none;
      user-select: none;
    }

    #voice-ui-container svg,
    #voice-chat-toggle svg,
    #voice-auth-status svg {
      flex: 0 0 auto;
      width: 20px;
      height: 20px;
      stroke: currentColor;
      display: block;
    }

    #voice-ui-container button:not(:disabled):hover,
    #voice-chat-toggle:not(:disabled):hover,
    #voice-chat-close:not(:disabled):hover,
    #voice-chat-send:not(:disabled):hover,
    #voice-video-close:not(:disabled):hover {
      box-shadow: 0 10px 24px rgba(0,0,0,0.34);
      filter: brightness(1.07);
      transform: scale(1.06);
    }

    #voice-ui-container button:not(:disabled):active,
    #voice-chat-toggle:not(:disabled):active,
    #voice-chat-close:not(:disabled):active,
    #voice-chat-send:not(:disabled):active,
    #voice-video-close:not(:disabled):active {
      transform: scale(0.98);
    }

    #voice-ui-container button:disabled {
      cursor: not-allowed;
      opacity: 0.62;
    }

    #voice-share-wrap {
      position: relative;
      display: none;
    }

    #voice-share-menu {
      position: absolute;
      bottom: calc(100% + 8px);
      left: 0;
      display: none;
      min-width: 150px;
      overflow: hidden;
      border-radius: 8px;
      background: rgba(17,24,39,0.96);
      box-shadow: 0 8px 20px rgba(0,0,0,0.36);
    }

    #voice-share-menu.is-open {
      display: grid;
    }

    #voice-share-menu button {
      width: 100%;
      border-radius: 0;
      background: transparent;
      text-align: left;
    }

    #voice-share-menu button:hover {
      background: rgba(255,255,255,0.12);
    }

    #voice-status {
      font-size: 14px;
      font-weight: 700;
      white-space: nowrap;
    }

    #voice-drawer-toggle {
      display: grid;
      place-items: center;
      width: 34px;
      height: 34px;
      padding: 0;
      border-radius: 999px;
      background: rgba(255,255,255,0.0);
      font: 900 18px/1 Arial, sans-serif;
    }

    .voice-control-icon {
      display: none;
    }

    #voice-ui-container.is-collapsed {
      gap: 8px;
      padding: 8px;
      border-radius: 999px;
    }

    #voice-ui-container.is-mobile #voice-drawer-toggle {
      display: none;
    }

    #voice-ui-container.is-collapsed #voice-status {
      display: grid;
      place-items: center;
      width: 44px;
      height: 44px;
      border-radius: 999px;
      background: #10B981;
      color: #fff;
      font-size: 0;
      line-height: 0;
    }

    #voice-status-icon {
      display: none;
      place-items: center;
      width: 22px;
      height: 22px;
      stroke: currentColor;
    }

    #voice-status-icon svg {
      width: 22px;
      height: 22px;
      display: block;
    }

    #voice-ui-container.is-collapsed #voice-status-label {
      display: none;
    }

    #voice-ui-container.is-collapsed #voice-status-icon {
      display: grid;
    }

    #voice-ui-container.is-collapsed #voice-status.is-off {
      background: #6B7280;
    }

    #voice-ui-container.is-collapsed button:not(#voice-drawer-toggle) {
      width: 44px;
      min-width: 44px;
      border-radius: 999px;
      padding: 0;
    }

    #voice-ui-container.is-collapsed .voice-control-label {
      display: none;
    }

    #voice-ui-container.is-collapsed .voice-control-icon {
      display: inline-flex;
    }

    #voice-ui-container.is-collapsed #voice-share-menu button {
      width: 100%;
      min-width: 0;
      border-radius: 0;
      padding: 0 12px;
    }

    @media (max-width: 640px) {
      #voice-badge-container {
        width: calc(100vw - 24px);
      }
    }

    #voice-auth-status {
      position: fixed;
      left: 20px;
      bottom: 88px;
      z-index: 99999;
      display: grid;
      place-items: center;
      width: 24px;
      height: 24px;
      border-radius: 999px;
      background: rgba(17,24,39,0.86);
      color: #fff;
      font: 900 12px/1 Arial, sans-serif;
      box-shadow: 0 8px 20px rgba(0,0,0,0.3);
      pointer-events: auto;
      transition: box-shadow 140ms ease, transform 140ms ease, filter 140ms ease;
    }

    #voice-auth-status svg {
      width: 15px;
      height: 15px;
    }

    #voice-auth-status:hover {
      box-shadow: 0 10px 24px rgba(0,0,0,0.34);
      filter: brightness(1.07);
      transform: scale(1.08);
    }

    #voice-auth-status.is-connected {
      background: rgba(0,0,0,0.82);
      color: #22C55E;
    }

    #voice-auth-status::after {
      content: attr(data-tooltip);
      position: absolute;
      left: 0;
      bottom: calc(100% + 8px);
      display: none;
      width: max-content;
      max-width: 180px;
      padding: 6px 8px;
      border-radius: 6px;
      background: rgba(17,24,39,0.95);
      color: #fff;
      font: 700 12px/1 Arial, sans-serif;
      box-shadow: 0 8px 20px rgba(0,0,0,0.3);
      white-space: nowrap;
    }

    /* Hover tooltip (quick glance) — suppressed while the click panel
       below is open, so the two don't visually stack on top of each other. */
    #voice-auth-status:hover::after,
    #voice-auth-status:focus-visible::after {
      display: block;
    }

    #voice-auth-status.is-panel-open::after {
      display: none;
    }

    /* Click-triggered panel — same status line as the hover tooltip, plus
       the overlay version, and stays open until clicked again (unlike the
       hover tooltip, which only shows on hover/focus). */
    #voice-auth-status-panel {
      position: absolute;
      left: 0;
      bottom: calc(100% + 8px);
      display: none;
      width: max-content;
      max-width: 220px;
      padding: 8px 10px;
      border-radius: 6px;
      background: rgba(17,24,39,0.95);
      box-shadow: 0 8px 20px rgba(0,0,0,0.3);
      white-space: nowrap;
    }

    #voice-auth-status-panel.is-open {
      display: block;
    }

    #voice-auth-status-panel-status {
      color: #fff;
      font: 700 12px/1.3 Arial, sans-serif;
    }

    #voice-auth-status-panel-version {
      margin-top: 3px;
      color: #9CA3AF;
      font: 400 11px/1.3 Arial, sans-serif;
    }

    #voice-chat {
      position: fixed;
      right: 18px;
      bottom: 28px;
      z-index: 99999;
      font-family: Arial, sans-serif;
      color: #fff;
    }

    #voice-chat-toggle {
      position: relative;
      display: grid;
      place-items: center;
      width: 44px;
      height: 44px;
      border: 0;
      border-radius: 999px;
      background: #FBBF24;
      color: #111827;
      cursor: pointer;
      font: 700 20px/1 Arial, sans-serif;
      box-shadow: 0 8px 20px rgba(0,0,0,0.34);
      transition: box-shadow 140ms ease, transform 140ms ease, filter 140ms ease;
    }

    #voice-chat-toggle:disabled {
      cursor: not-allowed;
      opacity: 0.58;
    }

    #voice-chat-unread {
      position: absolute;
      top: -5px;
      right: -5px;
      display: none;
      min-width: 18px;
      height: 18px;
      padding: 0 5px;
      border-radius: 999px;
      background: #EF4444;
      color: #fff;
      font: 700 11px/18px Arial, sans-serif;
      text-align: center;
    }

    #voice-chat-unread.is-visible {
      display: block;
    }

    #voice-chat-panel {
      position: absolute;
      right: 0;
      bottom: 54px;
      display: none;
      width: min(340px, calc(100vw - 36px));
      overflow: hidden;
      border: 1px solid rgba(255,255,255,0.18);
      border-radius: 8px;
      background: rgba(17,24,39,0.94);
      box-shadow: 0 14px 32px rgba(0,0,0,0.4);
      backdrop-filter: blur(7px);
    }

    #voice-chat-panel.is-open {
      display: grid;
      grid-template-rows: auto minmax(160px, 260px) auto auto;
    }

    #voice-chat-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 10px 12px;
      border-bottom: 1px solid rgba(255,255,255,0.12);
      font: 700 14px/1 Arial, sans-serif;
    }

    #voice-chat-close {
      display: grid;
      place-items: center;
      width: 26px;
      height: 26px;
      border: 0;
      border-radius: 999px;
      background: transparent;
      color: #fff;
      cursor: pointer;
      font: 700 18px/1 Arial, sans-serif;
      transition: box-shadow 140ms ease, transform 140ms ease, filter 140ms ease;
    }

    #voice-chat-messages {
      overflow-y: auto;
      padding: 10px 12px;
    }

    .voice-chat-message {
      display: grid;
      gap: 4px;
      margin-bottom: 10px;
    }

    .voice-chat-meta {
      display: flex;
      min-width: 0;
      gap: 7px;
      align-items: baseline;
      color: #D1D5DB;
      font: 700 12px/1.2 Arial, sans-serif;
    }

    .voice-chat-name {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .voice-chat-time {
      flex: 0 0 auto;
      color: #9CA3AF;
      font-weight: 400;
    }

    .voice-chat-body {
      overflow-wrap: anywhere;
      color: #F9FAFB;
      font: 400 13px/1.35 Arial, sans-serif;
      white-space: pre-wrap;
    }

    #voice-chat-empty,
    #voice-chat-error {
      padding: 0 12px 10px;
      color: #9CA3AF;
      font: 700 12px/1.3 Arial, sans-serif;
    }

    #voice-chat-error {
      display: none;
      color: #FCA5A5;
    }

    #voice-chat-error.is-visible {
      display: block;
    }

    #voice-chat-form {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      gap: 8px;
      padding: 10px 12px 12px;
      border-top: 1px solid rgba(255,255,255,0.12);
    }

    #voice-chat-input {
      min-width: 0;
      height: 34px;
      border: 1px solid rgba(255,255,255,0.18);
      border-radius: 6px;
      padding: 0 10px;
      background: rgba(0,0,0,0.28);
      color: #fff;
      font: 400 13px/1 Arial, sans-serif;
    }

    #voice-chat-send {
      height: 34px;
      border: 0;
      border-radius: 6px;
      padding: 0 12px;
      background: #2563EB;
      color: #fff;
      cursor: pointer;
      font: 700 13px/1 Arial, sans-serif;
      transition: box-shadow 140ms ease, transform 140ms ease, filter 140ms ease;
    }

    #voice-chat-input:disabled,
    #voice-chat-send:disabled {
      cursor: not-allowed;
      opacity: 0.58;
    }

    #voice-video-panel-row {
      position: fixed;
      right: 18px;
      top: 36%;
      z-index: 99998;
      display: flex;
      flex-direction: column;
      gap: 10px;
      transform: translateY(-50%);
    }

    .voice-video-panel {
      position: relative;
      width: min(260px, calc(100vw - 36px));
      aspect-ratio: 16 / 9;
      overflow: hidden;
      border: 2px solid rgba(255,255,255,0.72);
      border-radius: 8px;
      background: #05070A;
      box-shadow: 0 12px 28px rgba(0,0,0,0.42);
      cursor: pointer;
    }

    .voice-video-panel video,
    #voice-video-modal video {
      width: 100%;
      height: 100%;
      object-fit: contain;
      background: #05070A;
    }

    .voice-video-label {
      position: absolute;
      left: 8px;
      right: 8px;
      bottom: 8px;
      overflow: hidden;
      padding: 5px 7px;
      border-radius: 6px;
      background: rgba(0,0,0,0.62);
      color: #fff;
      font: 700 12px/1.2 Arial, sans-serif;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .voice-video-mute-btn,
    .voice-video-stop-btn {
      position: absolute;
      top: 8px;
      z-index: 1;
      display: grid;
      place-items: center;
      width: 28px;
      height: 28px;
      border: 0;
      border-radius: 999px;
      color: #fff;
      cursor: pointer;
      box-shadow: 0 6px 16px rgba(0,0,0,0.4);
      transition: box-shadow 140ms ease, transform 140ms ease, filter 140ms ease;
    }

    .voice-video-mute-btn svg,
    .voice-video-stop-btn svg {
      width: 16px;
      height: 16px;
      stroke: currentColor;
    }

    .voice-video-mute-btn:hover,
    .voice-video-stop-btn:hover {
      filter: brightness(1.1);
      transform: scale(1.06);
    }

    .voice-video-mute-btn {
      right: 8px;
      background: #2563EB;
    }

    .voice-video-mute-btn.is-muted {
      background: #6B7280;
    }

    .voice-video-stop-btn {
      right: 8px;
      background: #EF4444;
    }

    #voice-video-modal {
      position: fixed;
      inset: 0;
      z-index: 100000;
      display: none;
      place-items: center;
      background: rgba(0,0,0,0.72);
    }

    #voice-video-modal.is-visible {
      display: grid;
    }

    #voice-video-modal-content {
      position: relative;
      width: min(90vw, 1180px);
      height: min(90vh, 664px);
      overflow: hidden;
      border-radius: 8px;
      background: #05070A;
      box-shadow: 0 20px 46px rgba(0,0,0,0.5);
    }

    #voice-video-close {
      position: absolute;
      top: 10px;
      right: 10px;
      z-index: 1;
      display: grid;
      place-items: center;
      width: 34px;
      height: 34px;
      border: 0;
      border-radius: 999px;
      background: rgba(0,0,0,0.72);
      color: #fff;
      cursor: pointer;
      font: 700 18px/1 Arial, sans-serif;
      transition: box-shadow 140ms ease, transform 140ms ease, filter 140ms ease;
    }
  `;
  document.head.appendChild(style);

  const badgeContainer = document.createElement("div");
  badgeContainer.id = "voice-badge-container";

  const container = document.createElement("div");
  container.id = "voice-ui-container";
  container.innerHTML = `
    <span id="voice-status" class="is-off">
      <span id="voice-status-label">Voice: Off</span>
      <span id="voice-status-icon">${icons.info}</span>
    </span>
    <button id="btn-toggle-voice" style="background-color: #10B981;" title="Turn voice channel on">
      <span class="voice-control-icon">${icons.mic}</span>
      <span class="voice-control-label">Turn Voice Channel On</span>
    </button>
    <button id="btn-mute" style="background-color: #6B7280; display: none;" title="Mute mic">
      <span class="voice-control-icon">${icons.micOff}</span>
      <span class="voice-control-label">Mute Mic</span>
    </button>
    <span id="voice-share-wrap">
      <button id="btn-share-video" style="background-color: #2563EB;" title="Share video">
        <span class="voice-control-icon">${icons.screen}</span>
        <span class="voice-control-label">Share Video</span>
      </button>
      <span id="voice-share-menu">
        <button id="btn-share-screen" type="button">Share Screen</button>
        <button id="btn-share-camera" type="button">Share Camera</button>
      </span>
    </span>
    <button id="voice-drawer-toggle" type="button" aria-label="Collapse voice controls" title="Collapse voice controls">${icons.chevronLeft}</button>
  `;

  const authStatus = document.createElement("div");
  authStatus.id = "voice-auth-status";
  authStatus.title = "Visitor";
  authStatus.dataset.tooltip = "Visitor";
  authStatus.tabIndex = 0;
  authStatus.innerHTML = `
    <span id="voice-auth-status-icon">${icons.info}</span>
    <div id="voice-auth-status-panel">
      <div id="voice-auth-status-panel-status">Visitor</div>
      <div id="voice-auth-status-panel-version">Overlay version ${VOICE_OVERLAY_VERSION}</div>
    </div>
  `;

  authStatus.addEventListener("click", () => {
    const isOpen = authStatus.querySelector("#voice-auth-status-panel").classList.toggle("is-open");
    authStatus.classList.toggle("is-panel-open", isOpen);
  });

  const chat = document.createElement("div");
  chat.id = "voice-chat";
  chat.innerHTML = `
    <button id="voice-chat-toggle" type="button" aria-label="Open chat" title="Chat" style="display: none;" disabled>
      ${icons.envelope}
      <span id="voice-chat-unread"></span>
    </button>
    <div id="voice-chat-panel" aria-label="Room chat">
      <div id="voice-chat-header">
        <span>Room Chat</span>
        <button id="voice-chat-close" type="button" aria-label="Close chat">&times;</button>
      </div>
      <div id="voice-chat-messages"></div>
      <div id="voice-chat-empty">Join voice to chat.</div>
      <div id="voice-chat-error"></div>
      <form id="voice-chat-form">
        <input id="voice-chat-input" type="text" maxlength="1000" autocomplete="off" placeholder="Message" disabled>
        <button id="voice-chat-send" type="submit" disabled>Send</button>
      </form>
    </div>
  `;

  // Holds 0-2 dynamically created .voice-video-panel tiles (see
  // buildVideoTile below) — replaces the old single #voice-video-panel
  // element now that up to 2 concurrent shares can be active at once.
  const videoPanelRow = document.createElement("div");
  videoPanelRow.id = "voice-video-panel-row";

  const videoModal = document.createElement("div");
  videoModal.id = "voice-video-modal";
  videoModal.innerHTML = `
    <div id="voice-video-modal-content">
      <button id="voice-video-close" type="button" aria-label="Close video view">&times;</button>
      <video id="voice-video-large" autoplay playsinline></video>
      <div id="voice-video-modal-label" class="voice-video-label"></div>
    </div>
  `;

  document.body.appendChild(badgeContainer);
  document.body.appendChild(container);
  document.body.appendChild(authStatus);
  document.body.appendChild(chat);
  document.body.appendChild(videoPanelRow);
  document.body.appendChild(videoModal);

  const elements = {
    badgeContainer,
    container,
    btnToggleVoice: document.getElementById("btn-toggle-voice"),
    btnMute: document.getElementById("btn-mute"),
    shareWrap: document.getElementById("voice-share-wrap"),
    btnShareVideo: document.getElementById("btn-share-video"),
    shareMenu: document.getElementById("voice-share-menu"),
    btnShareScreen: document.getElementById("btn-share-screen"),
    btnShareCamera: document.getElementById("btn-share-camera"),
    drawerToggle: document.getElementById("voice-drawer-toggle"),
    statusText: document.getElementById("voice-status"),
    statusLabel: document.getElementById("voice-status-label"),
    statusIcon: document.getElementById("voice-status-icon"),
    authStatus,
    authStatusIcon: document.getElementById("voice-auth-status-icon"),
    authStatusPanelStatus: document.getElementById("voice-auth-status-panel-status"),
    chat,
    chatToggle: document.getElementById("voice-chat-toggle"),
    chatUnread: document.getElementById("voice-chat-unread"),
    chatPanel: document.getElementById("voice-chat-panel"),
    chatClose: document.getElementById("voice-chat-close"),
    chatMessages: document.getElementById("voice-chat-messages"),
    chatEmpty: document.getElementById("voice-chat-empty"),
    chatError: document.getElementById("voice-chat-error"),
    chatForm: document.getElementById("voice-chat-form"),
    chatInput: document.getElementById("voice-chat-input"),
    chatSend: document.getElementById("voice-chat-send"),
    videoPanelRow,
    videoModal,
    videoLarge: document.getElementById("voice-video-large"),
    videoModalLabel: document.getElementById("voice-video-modal-label"),
    videoClose: document.getElementById("voice-video-close"),
  };
  const participants = new Map();
  // Mirrors the server's room-wide cap (gameRoom.service.ts
  // MAX_CONCURRENT_VIDEO_SHARES) purely for UI messaging — the server is
  // authoritative.
  const MAX_VIDEO_SHARES = 2;
  let chatEnabled = false;
  let chatOpen = false;
  let unreadCount = 0;
  const isMobileBrowser =
    navigator.userAgentData?.mobile ||
    /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(
      navigator.userAgent,
    );
  let drawerOpen = !isMobileBrowser;

  if (isMobileBrowser) {
    elements.container.classList.add("is-mobile", "is-collapsed");
  }

  const initialsOf = (name) =>
    (name || "?")
      .trim()
      .split(/\s+/)
      .slice(0, 2)
      .map((part) => part[0])
      .join("")
      .toUpperCase();

  const renderBadges = () => {
    elements.badgeContainer.innerHTML = "";

    participants.forEach((participant) => {
      const badge = document.createElement("div");
      badge.className = `voice-user-badge${participant.speaking && !participant.muted ? " is-speaking" : ""}${participant.muted ? " is-muted" : ""}`;
      // data-tooltip drives the instant custom tooltip (see the
      // .voice-user-badge::after CSS above); tabIndex makes it
      // keyboard-focusable so :focus-visible can show it too.
      badge.dataset.tooltip = participant.displayName || participant.participantId;
      badge.tabIndex = 0;

      if (participant.avatarImageUrl) {
        const img = document.createElement("img");
        img.alt = participant.displayName || participant.participantId;
        img.src = participant.avatarImageUrl;
        img.onerror = () => {
          badge.innerHTML = "";
          const fallback = document.createElement("span");
          fallback.className = "voice-user-initials";
          fallback.textContent = initialsOf(
            participant.displayName || participant.participantId,
          );
          badge.appendChild(fallback);
        };
        badge.appendChild(img);
      } else {
        const fallback = document.createElement("span");
        fallback.className = "voice-user-initials";
        fallback.textContent = initialsOf(
          participant.displayName || participant.participantId,
        );
        badge.appendChild(fallback);
      }

      const muted = document.createElement("span");
      muted.className = "voice-muted-indicator";
      muted.innerHTML = icons.muted;
      badge.appendChild(muted);

      elements.badgeContainer.appendChild(badge);
    });
  };

  const videoLabelOf = (share, isLocal) => {
    if (!share) return "";
    const type = share.shareType === "screen" ? "screen" : "camera";
    const name = isLocal ? "You" : share.displayName || share.participantId;
    return `${name} sharing ${type}`;
  };

  const setVideoElementStream = (video, stream, muted) => {
    video.srcObject = stream || null;
    video.muted = muted;
    if (stream) video.play().catch(() => {});
  };

  // shareId -> tile DOM element, for reconciling setVideoShareState()
  // against the current row without a full rebuild each time.
  const videoTiles = new Map();
  let currentModalShareId = null;
  let onStopShareRequested = () => {};
  // Remembers the last entries passed to setVideoShareState() so
  // setVideoShareBusy(false) can re-derive correct button state instead
  // of blindly re-enabling everything.
  let lastVideoShareEntries = [];

  // Three/four-state share button, derived from lastVideoShareEntries:
  //  - I own >=1 share: "Share Video" stays available if a slot is still
  //    free (I can start a second, different-type share); each owned
  //    tile has its own inline stop control, so this button never means
  //    "stop" once ownCount > 0.
  //  - I own 0 and the room is full: disabled, "Share full".
  //  - Otherwise: enabled, opens the share-type menu.
  // Also grays out whichever type the current viewer already holds, so a
  // participant picking their second share can only pick the other type
  // — enforced UI-only; the server's own "restart my own share of this
  // shareType" rule already makes requesting the same type twice a no-op
  // restart rather than a real second share.
  const applyShareButtonState = () => {
    const entries = lastVideoShareEntries;
    const ownCount = entries.filter((e) => e.isLocal).length;
    const totalCount = entries.length;

    if (totalCount >= MAX_VIDEO_SHARES && ownCount === 0) {
      setButtonContent(elements.btnShareVideo, icons.screen, `Share full (${totalCount}/${MAX_VIDEO_SHARES})`);
      elements.btnShareVideo.disabled = true;
      elements.btnShareVideo.style.backgroundColor = "#6B7280";
      elements.shareMenu.classList.remove("is-open");
    } else if (totalCount >= MAX_VIDEO_SHARES && ownCount > 0) {
      setButtonContent(elements.btnShareVideo, icons.screen, `Sharing (${totalCount}/${MAX_VIDEO_SHARES})`);
      elements.btnShareVideo.disabled = true;
      elements.btnShareVideo.style.backgroundColor = "#6B7280";
      elements.shareMenu.classList.remove("is-open");
    } else {
      setButtonContent(elements.btnShareVideo, icons.screen, "Share Video");
      elements.btnShareVideo.disabled = false;
      elements.btnShareVideo.style.backgroundColor = "#2563EB";
    }

    const ownTypes = new Set(entries.filter((e) => e.isLocal).map((e) => e.share.shareType));
    elements.btnShareScreen.disabled = ownTypes.has("screen");
    elements.btnShareCamera.disabled = ownTypes.has("camera");
  };

  const openVideoModalFor = (shareId) => {
    const tile = videoTiles.get(shareId);
    const video = tile?.querySelector("video");
    if (!tile || !video?.srcObject) return;

    currentModalShareId = shareId;
    const label = tile.querySelector(".voice-video-label")?.textContent || "";
    elements.videoModalLabel.textContent = label;
    setVideoElementStream(elements.videoLarge, video.srcObject, video.muted);
    elements.videoModal.classList.add("is-visible");
  };

  // Builds a tile once per shareId. isLocal is fixed for the tile's
  // lifetime (a share never changes owner), so the mute-vs-stop control it
  // gets is decided here, at creation time.
  const buildVideoTile = (shareId, isLocal) => {
    const tile = document.createElement("div");
    tile.className = "voice-video-panel";
    tile.dataset.shareId = shareId;

    const video = document.createElement("video");
    video.autoplay = true;
    video.playsInline = true;
    tile.appendChild(video);

    const label = document.createElement("div");
    label.className = "voice-video-label";
    tile.appendChild(label);

    if (isLocal) {
      // Own tiles never get a mute control — the video's audio is always
      // hard-muted for the sharer (see setVideoElementStream calls below),
      // so there's nothing to toggle. They get an inline stop control
      // instead, since the single global "Share Video" button can no
      // longer unambiguously mean "stop my one share" once a participant
      // can own 2 concurrently.
      const stopBtn = document.createElement("button");
      stopBtn.type = "button";
      stopBtn.className = "voice-video-stop-btn";
      stopBtn.setAttribute("aria-label", "Stop sharing");
      stopBtn.title = "Stop sharing";
      stopBtn.innerHTML = icons.close;
      stopBtn.addEventListener("click", (event) => {
        event.stopPropagation();
        onStopShareRequested(shareId);
      });
      tile.appendChild(stopBtn);
    } else {
      // Remote tiles start unmuted (audio plays automatically) with a
      // button to mute/carry-sound, independently per tile.
      const muteBtn = document.createElement("button");
      muteBtn.type = "button";
      muteBtn.className = "voice-video-mute-btn";
      muteBtn.addEventListener("click", (event) => {
        event.stopPropagation();
        const isMuted = video.muted;
        video.muted = !isMuted;
        muteBtn.classList.toggle("is-muted", !isMuted);
        muteBtn.innerHTML = !isMuted ? icons.volumeOff : icons.volumeOn;
        muteBtn.title = !isMuted ? "Carry sound" : "Drop sound";
        muteBtn.setAttribute("aria-label", muteBtn.title);
        if (currentModalShareId === shareId) {
          elements.videoLarge.muted = video.muted;
        }
      });
      muteBtn.innerHTML = icons.volumeOn;
      muteBtn.title = "Drop sound";
      muteBtn.setAttribute("aria-label", muteBtn.title);
      tile.appendChild(muteBtn);
    }

    tile.addEventListener("click", () => openVideoModalFor(shareId));

    videoTiles.set(shareId, tile);
    elements.videoPanelRow.appendChild(tile);
    return tile;
  };

  const formatMessageTime = (value) => {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "";
    return date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
  };

  const updateUnread = () => {
    elements.chatUnread.textContent =
      unreadCount > 99 ? "99+" : String(unreadCount);
    elements.chatUnread.classList.toggle("is-visible", unreadCount > 0);
  };

  const scrollChatToBottom = () => {
    elements.chatMessages.scrollTop = elements.chatMessages.scrollHeight;
  };

  const renderChatMessage = (message) => {
    const row = document.createElement("div");
    row.className = "voice-chat-message";

    const meta = document.createElement("div");
    meta.className = "voice-chat-meta";

    const name = document.createElement("span");
    name.className = "voice-chat-name";
    name.textContent =
      message.displayName || message.participantId || "Unknown";

    const time = document.createElement("span");
    time.className = "voice-chat-time";
    time.textContent = formatMessageTime(message.createdAt);

    const body = document.createElement("div");
    body.className = "voice-chat-body";
    body.textContent = message.body || "";

    meta.appendChild(name);
    meta.appendChild(time);
    row.appendChild(meta);
    row.appendChild(body);
    elements.chatMessages.appendChild(row);
  };

  const setChatEmptyText = (text) => {
    elements.chatEmpty.textContent = text;
    elements.chatEmpty.style.display = text ? "block" : "none";
  };

  const setButtonContent = (button, icon, label, title = label) => {
    const iconEl = button.querySelector(".voice-control-icon");
    const labelEl = button.querySelector(".voice-control-label");
    if (iconEl) iconEl.innerHTML = icon;
    if (labelEl) labelEl.textContent = label;
    button.title = title;
  };

  return {
    elements,
    upsertParticipant(participant) {
      if (!participant?.participantId) return;
      const existing = participants.get(participant.participantId) || {};
      participants.set(participant.participantId, {
        speaking: false,
        muted: false,
        audioLevel: 0,
        ...existing,
        ...participant,
        displayName:
          participant.displayName ||
          existing.displayName ||
          participant.participantId,
        avatarImageUrl:
          participant.avatarImageUrl || existing.avatarImageUrl || null,
      });
      renderBadges();
    },
    removeParticipant(participantId) {
      participants.delete(participantId);
      renderBadges();
    },
    setSpeakingState(state) {
      if (!state?.participantId) return;
      const existing = participants.get(state.participantId) || {
        participantId: state.participantId,
        displayName: state.displayName || state.participantId,
        avatarImageUrl: null,
      };
      participants.set(state.participantId, {
        ...existing,
        displayName: state.displayName || existing.displayName,
        speaking: Boolean(state.speaking),
        muted: Boolean(state.muted),
        audioLevel: state.audioLevel || 0,
      });
      renderBadges();
    },
    clearParticipants() {
      participants.clear();
      renderBadges();
    },
    setConnecting() {
      elements.btnToggleVoice.disabled = true;
      elements.statusLabel.textContent = "Connecting...";
    },
    setConnectError() {
      elements.statusLabel.textContent = "Voice: Off";
      elements.statusIcon.innerHTML = icons.info;
      elements.btnToggleVoice.disabled = false;
    },
    setConnected() {
      elements.statusLabel.textContent = "Voice: Connected";
      elements.statusText.classList.remove("is-off");
      elements.statusIcon.innerHTML = icons.check;
      setButtonContent(elements.btnToggleVoice, icons.door, "Leave Voice");
      elements.btnToggleVoice.style.backgroundColor = "#EF4444";
      elements.btnMute.style.display = "inline-block";
      elements.shareWrap.style.display = "inline-block";
      elements.chatToggle.style.display = "grid";
      elements.btnToggleVoice.disabled = false;
    },
    setDisconnected() {
      elements.statusLabel.textContent = "Voice: Off";
      elements.statusText.classList.add("is-off");
      elements.statusIcon.innerHTML = icons.info;
      setButtonContent(
        elements.btnToggleVoice,
        icons.mic,
        "Turn Voice Channel On",
      );
      elements.btnToggleVoice.style.backgroundColor = "#10B981";
      elements.btnMute.style.display = "none";
      elements.shareWrap.style.display = "none";
      elements.chatToggle.style.display = "none";
      elements.shareMenu.classList.remove("is-open");
      elements.btnToggleVoice.disabled = false;
    },
    setChatEnabled(isEnabled) {
      chatEnabled = Boolean(isEnabled);
      elements.chatToggle.disabled = !chatEnabled;
      elements.chatInput.disabled = !chatEnabled;
      elements.chatSend.disabled = !chatEnabled;
      if (!chatEnabled) {
        setChatEmptyText("Join voice to chat.");
        elements.chatError.classList.remove("is-visible");
      } else if (!elements.chatMessages.children.length) {
        setChatEmptyText("No messages yet.");
      }
    },
    toggleChat(forceOpen) {
      chatOpen =
        typeof forceOpen === "boolean"
          ? forceOpen
          : !elements.chatPanel.classList.contains("is-open");
      elements.chatPanel.classList.toggle("is-open", chatOpen);
      elements.chatToggle.setAttribute(
        "aria-label",
        chatOpen ? "Close chat" : "Open chat",
      );
      if (chatOpen) {
        unreadCount = 0;
        updateUnread();
        scrollChatToBottom();
        elements.chatInput.focus();
      }
    },
    setChatMessages(messages) {
      elements.chatMessages.innerHTML = "";
      messages.forEach(renderChatMessage);
      setChatEmptyText(
        messages.length
          ? ""
          : chatEnabled
            ? "No messages yet."
            : "Join voice to chat.",
      );
      window.requestAnimationFrame(scrollChatToBottom);
    },
    appendChatMessage(message) {
      renderChatMessage(message);
      setChatEmptyText("");
      if (chatOpen) {
        window.requestAnimationFrame(scrollChatToBottom);
      } else {
        unreadCount += 1;
        updateUnread();
      }
    },
    clearChat() {
      elements.chatMessages.innerHTML = "";
      elements.chatInput.value = "";
      unreadCount = 0;
      updateUnread();
      this.toggleChat(false);
      this.setChatEnabled(false);
    },
    setChatError(message) {
      elements.chatError.textContent = message || "Chat failed.";
      elements.chatError.classList.add("is-visible");
      window.setTimeout(() => {
        elements.chatError.classList.remove("is-visible");
      }, 4000);
    },
    setMuted(isMuted) {
      setButtonContent(
        elements.btnMute,
        isMuted ? icons.mic : icons.micOff,
        isMuted ? "Unmute Mic" : "Mute Mic",
      );
      elements.btnMute.style.backgroundColor = isMuted ? "#F59E0B" : "#6B7280";
    },
    toggleDrawer(forceOpen) {
      if (isMobileBrowser) return;

      drawerOpen = typeof forceOpen === "boolean" ? forceOpen : !drawerOpen;
      elements.container.classList.toggle("is-collapsed", !drawerOpen);
      elements.drawerToggle.innerHTML = drawerOpen
        ? icons.chevronLeft
        : icons.chevronRight;
      elements.drawerToggle.setAttribute(
        "aria-label",
        drawerOpen ? "Collapse voice controls" : "Expand voice controls",
      );
      elements.drawerToggle.title = drawerOpen
        ? "Collapse voice controls"
        : "Expand voice controls";
    },
    setAuthStatus(isConnected) {
      const label = isConnected ? "Connected to MANGOs" : "Visitor";
      elements.authStatus.classList.toggle(
        "is-connected",
        Boolean(isConnected),
      );
      elements.authStatusIcon.innerHTML = isConnected ? icons.check : icons.info;
      elements.authStatus.title = label;
      elements.authStatus.dataset.tooltip = label;
      elements.authStatusPanelStatus.textContent = label;
    },
    toggleShareMenu(forceOpen) {
      const isOpen =
        typeof forceOpen === "boolean"
          ? forceOpen
          : !elements.shareMenu.classList.contains("is-open");
      elements.shareMenu.classList.toggle("is-open", isOpen);
    },
    // Registers the callback fired when a tile's inline "stop sharing"
    // button is clicked, passing that tile's shareId.
    onStopShare(callback) {
      onStopShareRequested = callback || (() => {});
    },
    // entries: array of { share, stream, isLocal }, one per active video
    // share (0-2). Reconciles the DOM row against this list: creates
    // tiles for new shareIds, updates srcObject/label for existing ones,
    // and removes tiles for shares no longer present.
    setVideoShareState(entries) {
      const seenShareIds = new Set();

      for (const { share, stream, isLocal } of entries) {
        seenShareIds.add(share.shareId);

        let tile = videoTiles.get(share.shareId);
        if (!tile) {
          tile = buildVideoTile(share.shareId, Boolean(isLocal));
        }

        tile.querySelector(".voice-video-label").textContent = videoLabelOf(share, isLocal);

        const video = tile.querySelector("video");
        // Own tiles are always hard-muted (the sharer never hears their
        // own audio, no control to change it); remote tiles keep
        // whatever mute state the user has toggled on this specific
        // tile, starting unmuted the first time the video element gets a
        // stream.
        const hadStream = Boolean(video.srcObject);
        setVideoElementStream(video, stream || null, isLocal ? true : !hadStream ? false : video.muted);

        if (currentModalShareId === share.shareId) {
          elements.videoModalLabel.textContent = tile.querySelector(".voice-video-label").textContent;
          setVideoElementStream(elements.videoLarge, stream || null, video.muted);
        }
      }

      // Remove tiles for shares no longer present.
      for (const [shareId, tile] of Array.from(videoTiles.entries())) {
        if (seenShareIds.has(shareId)) continue;

        if (currentModalShareId === shareId) {
          this.closeVideoModal();
        }
        tile.remove();
        videoTiles.delete(shareId);
      }

      lastVideoShareEntries = entries;
      applyShareButtonState();
    },
    setVideoShareBusy(isBusy) {
      elements.btnShareVideo.disabled = isBusy;
      elements.btnShareScreen.disabled = isBusy;
      elements.btnShareCamera.disabled = isBusy;
      // Re-derive the correct per-button disabled state from the last
      // known share list rather than force-enabling everything — a
      // button that setVideoShareState() disabled (room full, or this
      // viewer already holds that type) must not be blindly re-enabled
      // just because a busy period ended.
      if (!isBusy) applyShareButtonState();
    },
    closeVideoModal() {
      currentModalShareId = null;
      elements.videoModal.classList.remove("is-visible");
    },
  };
}
