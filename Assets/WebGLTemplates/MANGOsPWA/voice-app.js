(function bootstrapVoiceOverlay() {
  const currentScript = document.currentScript;
  const appUrl = new URL("./voice-overlay/app.js", currentScript?.src || window.location.href);

  window.MangosVoiceReady = import(appUrl.href)
    .then(({ startVoiceOverlay }) => {
      return startVoiceOverlay();
    })
    .catch((error) => {
      console.error("Voice overlay failed to start.", error);
      window.alert?.("Voice overlay failed to start: " + error.message);
    });
})();
