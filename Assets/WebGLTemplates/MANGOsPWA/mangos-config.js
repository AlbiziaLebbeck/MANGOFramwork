// Build Center writes deployment-specific public settings into the output copy.
window.VOICE_APP_CONFIG = {
  ...(window.VOICE_APP_CONFIG || {}),
  requireGameRoom: true,
  roomId: "",
  roomNamespace: "mangos",
};
