export function ensureSocketIoClient(config) {
  if (window.io) return Promise.resolve(window.io);

  return new Promise((resolve, reject) => {
    const existingScript = document.querySelector("script[data-voice-socket-io]");
    if (existingScript) {
      existingScript.addEventListener("load", () => resolve(window.io), { once: true });
      existingScript.addEventListener("error", () => reject(new Error("Socket.IO client failed to load.")), {
        once: true,
      });
      return;
    }

    const script = document.createElement("script");
    script.src = config.socketIoClientUrl;
    script.async = true;
    script.dataset.voiceSocketIo = "true";
    script.onload = () => {
      if (window.io) {
        resolve(window.io);
      } else {
        reject(new Error("Socket.IO client loaded, but window.io is unavailable."));
      }
    };
    script.onerror = () => reject(new Error("Socket.IO client failed to load."));
    document.head.appendChild(script);
  });
}
