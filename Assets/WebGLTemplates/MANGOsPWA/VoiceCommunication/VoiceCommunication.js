(function (global) {
  "use strict";

  const DEFAULT_ICE_SERVERS = [
    { urls: "stun:stun.l.google.com:19302" },
    { urls: "stun:stun1.l.google.com:19302" }
  ];

  let config = null;
  let callback = null;
  let roomInfo = null;
  let localStream = null;
  let publishPeer = null;
  let socket = null;
  let participantId = null;
  let state = "idle";
  let muted = false;
  let leaving = false;
  let iceServers = DEFAULT_ICE_SERVERS;

  const playbackUrls = new Map();
  const subscriberPeers = new Map();
  const audioElements = new Map();
  const subscriptionTasks = new Map();

  function notify(nextState, message) {
    state = nextState;
    if (typeof callback === "function") {
      callback({
        state: nextState,
        message: message || "",
        muted: muted
      });
    }
  }

  function randomId() {
    if (global.crypto && typeof global.crypto.randomUUID === "function") {
      return global.crypto.randomUUID();
    }

    return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, function (character) {
      const random = Math.random() * 16 | 0;
      const value = character === "x" ? random : (random & 3 | 8);
      return value.toString(16);
    });
  }

  function getStoredId(storage, key) {
    try {
      let value = storage.getItem(key);
      if (!value) {
        value = randomId();
        storage.setItem(key, value);
      }
      return value;
    } catch (_) {
      return randomId();
    }
  }

  function normalizeParticipantPart(value, fallback) {
    const normalized = String(value || fallback)
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9_-]/g, "-")
      .replace(/-+/g, "-")
      .replace(/^-|-$/g, "");
    return normalized || fallback;
  }

  function createParticipantId(prefix) {
    const deviceId = getStoredId(global.localStorage, "mangos.voice.deviceId");
    const tabId = getStoredId(global.sessionStorage, "mangos.voice.tabId");
    return [normalizeParticipantPart(prefix, "player"), deviceId, tabId].join("-");
  }

  function trimTrailingSlashes(value) {
    return String(value || "").replace(/\/+$/, "");
  }

  function withTimeout(promiseFactory, timeoutMilliseconds, timeoutMessage) {
    const controller = new AbortController();
    const timeoutId = global.setTimeout(function () {
      controller.abort();
    }, Math.max(1000, timeoutMilliseconds));

    return promiseFactory(controller.signal)
      .catch(function (error) {
        if (error && error.name === "AbortError") {
          throw new Error(timeoutMessage);
        }
        throw error;
      })
      .finally(function () {
        global.clearTimeout(timeoutId);
      });
  }

  async function readResponseBody(response) {
    const body = await response.text();
    if (!body) {
      return {};
    }

    try {
      return JSON.parse(body);
    } catch (_) {
      throw new Error("The voice server returned invalid JSON.");
    }
  }

  async function fetchIceServers() {
    try {
      const response = await withTimeout(
        function (signal) {
          return fetch(config.backendBaseUrl + "/api/v1/ice-servers", {
            method: "GET",
            credentials: "omit",
            cache: "no-store",
            signal: signal
          });
        },
        config.requestTimeoutMilliseconds,
        "The ICE server request timed out."
      );

      if (!response.ok) {
        return DEFAULT_ICE_SERVERS;
      }

      const payload = await readResponseBody(response);
      return Array.isArray(payload.iceServers) && payload.iceServers.length > 0
        ? payload.iceServers
        : DEFAULT_ICE_SERVERS;
    } catch (_) {
      return DEFAULT_ICE_SERVERS;
    }
  }

  function createPeerConnection() {
    return new RTCPeerConnection({ iceServers: iceServers });
  }

  async function waitForIceGathering(peer) {
    if (peer.iceGatheringState === "complete") {
      return;
    }

    await new Promise(function (resolve) {
      const timeoutId = global.setTimeout(function () {
        peer.removeEventListener("icegatheringstatechange", onStateChange);
        resolve();
      }, config.iceGatheringTimeoutMilliseconds);

      function onStateChange() {
        if (peer.iceGatheringState !== "complete") {
          return;
        }

        global.clearTimeout(timeoutId);
        peer.removeEventListener("icegatheringstatechange", onStateChange);
        resolve();
      }

      peer.addEventListener("icegatheringstatechange", onStateChange);
    });
  }

  async function joinRoomRest() {
    const joinUrl = config.backendBaseUrl
      + "/api/v1/game-rooms/"
      + encodeURIComponent(config.roomId)
      + "/join";

    const response = await withTimeout(
      function (signal) {
        return fetch(joinUrl, {
          method: "POST",
          credentials: "omit",
          cache: "no-store",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            participantId: participantId,
            displayName: config.displayName
          }),
          signal: signal
        });
      },
      config.requestTimeoutMilliseconds,
      "Joining the voice room timed out."
    );

    const payload = await readResponseBody(response);
    if (!response.ok || !payload.data) {
      throw new Error(payload.message || "Failed to join the voice room.");
    }

    roomInfo = payload.data;
    playbackUrls.clear();
    (roomInfo.participants || []).forEach(function (participant) {
      playbackUrls.set(participant.participantId, participant.playbackUrl);
    });
  }

  function createSocket() {
    if (typeof global.io !== "function") {
      throw new Error("The Socket.IO client is unavailable.");
    }

    socket = global.io(config.socketUrl, {
      path: config.socketPath,
      autoConnect: false,
      withCredentials: false,
      transports: ["websocket", "polling"],
      reconnection: true,
      reconnectionAttempts: 5,
      timeout: config.requestTimeoutMilliseconds
    });

    socket.on("game-room-participants", function (payload) {
      (payload && payload.participants || []).forEach(function (participant) {
        playbackUrls.set(participant.participantId, participant.playbackUrl);
        scheduleSubscription(participant.participantId);
      });
    });

    socket.on("game-room-participant-joined", function (payload) {
      const participant = payload && payload.participant;
      if (!participant || participant.participantId === participantId) {
        return;
      }

      playbackUrls.set(participant.participantId, participant.playbackUrl);
      scheduleSubscription(participant.participantId);
    });

    socket.on("game-room-participant-left", function (payload) {
      const remoteParticipantId = payload && payload.participantId;
      if (!remoteParticipantId) {
        return;
      }

      unsubscribe(remoteParticipantId);
      playbackUrls.delete(remoteParticipantId);
    });

    socket.on("game-room-join-error", function (payload) {
      const message = payload && payload.message
        ? payload.message
        : "The voice socket could not join the room.";
      cleanup({ notifyIdle: false }).then(function () {
        notify("failed", message);
      });
    });

    socket.on("disconnect", function () {
      if (!leaving && (state === "connected" || state === "muted")) {
        notify("connecting", "Reconnecting voice communication...");
      }
    });

    socket.io.on("reconnect", function () {
      if (leaving) {
        return;
      }

      emitSocketJoin();
      notify(muted ? "muted" : "connected", "Voice communication reconnected.");
    });
  }

  async function connectSocket() {
    await new Promise(function (resolve, reject) {
      let settled = false;
      const timeoutId = global.setTimeout(function () {
        finish(new Error("Connecting to the voice socket timed out."));
      }, config.requestTimeoutMilliseconds);

      function finish(error) {
        if (settled) {
          return;
        }

        settled = true;
        global.clearTimeout(timeoutId);
        socket.off("connect", onConnect);
        socket.off("connect_error", onError);
        if (error) {
          reject(error);
        } else {
          resolve();
        }
      }

      function onConnect() {
        finish();
      }

      function onError(error) {
        finish(new Error(error && error.message ? error.message : "The voice socket connection failed."));
      }

      socket.once("connect", onConnect);
      socket.once("connect_error", onError);
      socket.connect();
    });
  }

  function emitSocketJoin() {
    if (!socket || !socket.connected) {
      return;
    }

    socket.emit("join-game-room", {
      roomId: config.roomId,
      participantId: participantId,
      displayName: config.displayName
    });
  }

  async function publishMicrophone() {
    localStream = await navigator.mediaDevices.getUserMedia({
      audio: {
        echoCancellation: true,
        noiseSuppression: true,
        autoGainControl: true
      },
      video: false
    });

    publishPeer = createPeerConnection();
    localStream.getAudioTracks().forEach(function (track) {
      publishPeer.addTrack(track, localStream);
    });

    const offer = await publishPeer.createOffer();
    await publishPeer.setLocalDescription(offer);
    await waitForIceGathering(publishPeer);

    const response = await withTimeout(
      function (signal) {
        return fetch(roomInfo.publishUrl, {
          method: "POST",
          credentials: "omit",
          cache: "no-store",
          headers: { "Content-Type": "application/sdp" },
          body: publishPeer.localDescription.sdp,
          signal: signal
        });
      },
      config.requestTimeoutMilliseconds,
      "Publishing the microphone timed out."
    );

    if (!response.ok) {
      const message = await response.text();
      throw new Error(message || "WHIP microphone publishing failed.");
    }

    await publishPeer.setRemoteDescription({
      type: "answer",
      sdp: await response.text()
    });
  }

  async function subscribe(remoteParticipantId) {
    const playbackUrl = playbackUrls.get(remoteParticipantId);
    if (!playbackUrl || remoteParticipantId === participantId || leaving) {
      return;
    }

    unsubscribe(remoteParticipantId);

    const peer = createPeerConnection();
    const remoteStream = new MediaStream();
    subscriberPeers.set(remoteParticipantId, peer);
    peer.addTransceiver("audio", { direction: "recvonly" });
    peer.ontrack = function (event) {
      const tracks = event.streams && event.streams[0]
        ? event.streams[0].getTracks()
        : [event.track];
      tracks.forEach(function (track) {
        if (!remoteStream.getTracks().includes(track)) {
          remoteStream.addTrack(track);
        }
      });
    };

    const offer = await peer.createOffer();
    await peer.setLocalDescription(offer);
    await waitForIceGathering(peer);

    const response = await withTimeout(
      function (signal) {
        return fetch(playbackUrl, {
          method: "POST",
          credentials: "omit",
          cache: "no-store",
          headers: { "Content-Type": "application/sdp" },
          body: peer.localDescription.sdp,
          signal: signal
        });
      },
      config.requestTimeoutMilliseconds,
      "Subscribing to a remote voice timed out."
    );

    if (!response.ok) {
      const message = await response.text();
      unsubscribe(remoteParticipantId);
      throw new Error(message || "WHEP voice subscription failed.");
    }

    await peer.setRemoteDescription({
      type: "answer",
      sdp: await response.text()
    });

    const audio = document.createElement("audio");
    audio.autoplay = true;
    audio.playsInline = true;
    audio.style.display = "none";
    audio.srcObject = remoteStream;
    document.body.appendChild(audio);
    audioElements.set(remoteParticipantId, audio);

    try {
      await audio.play();
    } catch (_) {
      const unlock = function () {
        audio.play().catch(function () {});
      };
      global.addEventListener("pointerdown", unlock, { once: true });
      global.addEventListener("keydown", unlock, { once: true });
    }
  }

  function scheduleSubscription(remoteParticipantId) {
    if (!remoteParticipantId || remoteParticipantId === participantId || subscriptionTasks.has(remoteParticipantId)) {
      return;
    }

    const task = (async function () {
      for (let attempt = 1; attempt <= config.subscribeRetryCount; attempt += 1) {
        if (leaving || !playbackUrls.has(remoteParticipantId)) {
          return;
        }

        try {
          await subscribe(remoteParticipantId);
          return;
        } catch (_) {
          if (attempt < config.subscribeRetryCount) {
            await new Promise(function (resolve) {
              global.setTimeout(resolve, attempt * 500);
            });
          }
        }
      }
    })().finally(function () {
      subscriptionTasks.delete(remoteParticipantId);
    });

    subscriptionTasks.set(remoteParticipantId, task);
  }

  function unsubscribe(remoteParticipantId) {
    const peer = subscriberPeers.get(remoteParticipantId);
    if (peer) {
      peer.close();
      subscriberPeers.delete(remoteParticipantId);
    }

    const audio = audioElements.get(remoteParticipantId);
    if (audio) {
      audio.pause();
      audio.srcObject = null;
      audio.remove();
      audioElements.delete(remoteParticipantId);
    }
  }

  function closeLocalResources() {
    if (publishPeer) {
      publishPeer.close();
      publishPeer = null;
    }

    subscriberPeers.forEach(function (peer) {
      peer.close();
    });
    subscriberPeers.clear();

    Array.from(audioElements.keys()).forEach(unsubscribe);
    playbackUrls.clear();
    subscriptionTasks.clear();

    if (localStream) {
      localStream.getTracks().forEach(function (track) {
        track.stop();
      });
      localStream = null;
    }
  }

  async function leaveRoomRest(keepalive) {
    if (!config || !participantId) {
      return;
    }

    const leaveUrl = config.backendBaseUrl
      + "/api/v1/game-rooms/"
      + encodeURIComponent(config.roomId)
      + "/leave";

    try {
      await fetch(leaveUrl, {
        method: "POST",
        credentials: "omit",
        cache: "no-store",
        keepalive: Boolean(keepalive),
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ participantId: participantId })
      });
    } catch (_) {
    }
  }

  async function cleanup(options) {
    const notifyIdle = !options || options.notifyIdle !== false;
    const keepalive = Boolean(options && options.keepalive);
    leaving = true;

    if (socket) {
      if (socket.connected) {
        socket.emit("leave-game-room");
      }
      socket.removeAllListeners();
      socket.io.removeAllListeners();
      socket.disconnect();
      socket = null;
    }

    closeLocalResources();
    await leaveRoomRest(keepalive);

    roomInfo = null;
    participantId = null;
    muted = false;
    leaving = false;
    if (notifyIdle) {
      notify("idle", "Voice communication disconnected.");
    }
  }

  async function join(options, eventCallback) {
    if (state === "connecting" || state === "connected" || state === "muted") {
      return;
    }

    if (!navigator.mediaDevices || typeof navigator.mediaDevices.getUserMedia !== "function") {
      throw new Error("This browser does not support microphone capture.");
    }

    callback = eventCallback;
    config = {
      backendBaseUrl: trimTrailingSlashes(options.backendBaseUrl),
      socketUrl: trimTrailingSlashes(options.socketUrl),
      socketPath: options.socketPath,
      roomId: options.roomId,
      participantPrefix: options.participantPrefix,
      displayName: options.displayName,
      requestTimeoutMilliseconds: Math.max(1000, options.requestTimeoutMilliseconds || 15000),
      iceGatheringTimeoutMilliseconds: Math.max(1000, options.iceGatheringTimeoutMilliseconds || 5000),
      subscribeRetryCount: Math.max(1, options.subscribeRetryCount || 5)
    };

    participantId = createParticipantId(config.participantPrefix);
    muted = false;
    leaving = false;
    notify("connecting", "Connecting voice communication...");

    try {
      iceServers = await fetchIceServers();
      await joinRoomRest();
      createSocket();
      await connectSocket();
      await publishMicrophone();
      emitSocketJoin();
      Array.from(playbackUrls.keys()).forEach(scheduleSubscription);
      notify("connected", "Voice communication connected.");
    } catch (error) {
      const message = error && error.message ? error.message : "Voice communication failed.";
      await cleanup({ notifyIdle: false });
      notify("failed", message);
    }
  }

  function setMuted(value) {
    if (!localStream || (state !== "connected" && state !== "muted")) {
      return;
    }

    muted = Boolean(value);
    localStream.getAudioTracks().forEach(function (track) {
      track.enabled = !muted;
    });
    notify(muted ? "muted" : "connected", muted ? "Microphone muted." : "Microphone enabled.");
  }

  async function leave() {
    if (state === "idle" || state === "disconnecting") {
      return;
    }

    notify("disconnecting", "Disconnecting voice communication...");
    await cleanup({ notifyIdle: true });
  }

  global.addEventListener("beforeunload", function () {
    cleanup({ notifyIdle: false, keepalive: true });
  });

  global.MangosVoice = {
    join: join,
    setMuted: setMuted,
    leave: leave,
    getState: function () {
      return { state: state, muted: muted };
    }
  };
})(window);
