mergeInto(LibraryManager.library, {
  MangosBrowserDiagnostics: function (json) {
    window.MANGOS_DIAGNOSTICS = JSON.parse(UTF8ToString(json));
  },
  MangosBrowserSetUiPanel: function (open) {
    if (window.MangosController) window.MangosController.setUiPanel(!!open);
  },
  MangosBrowserDeviceFlags: function () {
    return window.MangosController ? window.MangosController.flags() : 0;
  },
  MangosBrowserSetPlayer: function (ready, canFly) {
    if (window.MangosController) window.MangosController.setPlayer({ready: !!ready, canFly: !!canFly});
  },
  MangosBrowserSetSession: function (json) {
    var session = JSON.parse(UTF8ToString(json));
    window.MANGOS_SESSION = session;
    if (window.MangosVoiceOverlay) {
      window.MangosVoiceOverlay.setSession(session).catch(function (error) {
        console.error("Voice room transition failed", error);
      });
    }
  }
});
