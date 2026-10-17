mergeInto(LibraryManager.library, {
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
