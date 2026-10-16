// Bumped manually on notable overlay changes — shown in the auth-status
// panel (see ui.js) so it's easy to confirm which build is deployed.
export const VOICE_OVERLAY_VERSION = "1.3.0";

export const DEFAULT_VOICE_CONFIG = {
  authBaseUrl: "https://api.mangosgo.com/api/v1",
  authRetryInitialIntervalMs: 10000,
  authRetryLongIntervalMs: 60000,
  authRetryFastWindowMs: 120000,
  socketIoClientUrl: "https://cdn.socket.io/4.7.2/socket.io.min.js",
  backendBaseUrl: "https://apischool.mangosgo.com/video-streaming",
  socketUrl: "https://apischool.mangosgo.com",
  socketPath: "/video-streaming/socket.io",
  roomId: "MEA-voice",
  requireGameRoom: false,
  roomNamespace: "mangos",
  mock: false,
};

export function getVoiceConfig() {
  return {
    ...DEFAULT_VOICE_CONFIG,
    ...(window.VOICE_APP_CONFIG || {}),
  };
}
