import { waitForIceGathering } from "./webrtc.js";

const PRESENCE_HEARTBEAT_INTERVAL_MS = 10000;
// How long to tolerate a PC sitting in "disconnected" (often a transient,
// self-recovering blip) before treating it the same as "failed" and
// re-establishing the connection from scratch.
const CONNECTION_RECOVERY_GRACE_MS = 5000;

const DEFAULT_ICE_SERVERS = [
  { urls: "stun:stun.l.google.com:19302" },
  { urls: "stun:stun1.l.google.com:19302" },
];

export class VoiceClient {
  constructor({ config, getIdentity, onParticipantUpsert, onParticipantRemove, onSpeakingState }) {
    this.config = config;
    this.getIdentity = getIdentity;
    this.onParticipantUpsert = onParticipantUpsert || (() => {});
    this.onParticipantRemove = onParticipantRemove || (() => {});
    this.onSpeakingState = onSpeakingState || (() => {});
    this.roomInfo = null;
    this.localStream = null;
    this.publishPc = null;
    this.playbackUrls = new Map();
    this.subscriberPcs = new Map();
    this.audioElements = new Map();
    this.socket = null;
    this.mockSpeakingTimer = null;
    this.activeIdentity = null;
    this.iceServers = DEFAULT_ICE_SERVERS;
    this.heartbeatTimer = null;
    this.publishRecoveryTimer = null;
    this.subscribeRecoveryTimers = new Map();
    this.resyncing = false;
  }

  async joinRoom() {
    const identity = this.getIdentity();
    this.activeIdentity = identity;

    if (this.config.mock) {
      this.roomInfo = {
        roomId: this.config.roomId,
        participantId: identity.participantId,
        displayName: identity.displayName,
        publishUrl: "mock://publish",
        participants: [
          {
            participantId: "Mock_Player_101",
            displayName: "Mock Player 101",
            playbackUrl: "mock://playback/101",
            avatarImageUrl: null,
          },
          {
            participantId: "Mock_Player_204",
            displayName: "Mock Player 204",
            playbackUrl: "mock://playback/204",
            avatarImageUrl: null,
          },
        ],
      };
      this.roomInfo.participants.forEach((p) => this.playbackUrls.set(p.participantId, p.playbackUrl));
      this.upsertParticipant({
        participantId: identity.participantId,
        displayName: identity.displayName,
        userId: identity.userId,
        avatarImageUrl: identity.avatarImageUrl,
      });
      this.roomInfo.participants.forEach((participant) => this.upsertParticipant(participant));
      this.startMockSpeakingStates();
      await new Promise((resolve) => setTimeout(resolve, 350));
      return this.roomInfo;
    }

    this.iceServers = await this.fetchIceServers();

    const { backendBaseUrl, roomId, socketUrl, socketPath } = this.config;
    const { participantId, displayName, userId, avatarImageUrl } = identity;
    const res = await fetch(`${backendBaseUrl}/api/v1/game-rooms/${encodeURIComponent(roomId)}/join`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ participantId, displayName, userId, avatarImageUrl }),
    });
    const payload = await res.json();
    if (!res.ok || !payload.data) throw new Error(payload.message || "Failed to join room.");

    this.roomInfo = payload.data;
    this.roomInfo.participants.forEach((p) => this.playbackUrls.set(p.participantId, p.playbackUrl));
    this.upsertParticipant({
      participantId,
      displayName,
      userId,
      avatarImageUrl: identity.avatarImageUrl,
    });
    this.roomInfo.participants.forEach((participant) => this.upsertParticipant(participant));

    this.socket = io(socketUrl, { path: socketPath, autoConnect: false, withCredentials: true });
    this.attachSocketLifecycleHandlers();
    this.socket.connect();
    this.socket.emit("join-game-room", { roomId, participantId, displayName, userId, avatarImageUrl });

    this.socket.on("game-room-participants", ({ participants }) => {
      this.reconcileParticipants(participants || []);
    });

    this.socket.on("game-room-participant-joined", async ({ participant }) => {
      this.playbackUrls.set(participant.participantId, participant.playbackUrl);
      this.upsertParticipant(participant);
      for (let attempt = 1; attempt <= 5; attempt++) {
        try {
          await this.subscribeRemoteUser(participant.participantId);
          break;
        } catch {
          await new Promise((resolve) => setTimeout(resolve, attempt * 500));
        }
      }
    });

    this.socket.on("game-room-participant-left", ({ participantId: leftParticipantId }) => {
      this.unsubscribeRemoteUser(leftParticipantId);
      this.playbackUrls.delete(leftParticipantId);
      this.onParticipantRemove(leftParticipantId);
    });

    this.socket.on("game-room-participant-speaking", (state) => {
      this.onSpeakingState(state);
    });

    this.startPresenceHeartbeat();

    return this.roomInfo;
  }

  async fetchIceServers() {
    try {
      const res = await fetch(`${this.config.backendBaseUrl}/api/v1/ice-servers`);
      const payload = await res.json();
      if (res.ok && Array.isArray(payload?.iceServers) && payload.iceServers.length) {
        return payload.iceServers;
      }
    } catch {
      // fall through to defaults
    }
    return DEFAULT_ICE_SERVERS;
  }

  // ── Socket connection lifecycle ───────────────────────────────────────────
  //
  // Socket.IO auto-reconnects by default. Without handling "connect" here,
  // a client whose socket drops (network blip, tab backgrounded, etc.) would
  // silently reconnect with a fresh socket id while local state (roster,
  // publish/subscribe peer connections) stays whatever it was before the
  // drop — including participants that left mid-gap and were never removed.
  attachSocketLifecycleHandlers() {
    this.socket.on("connect", () => {
      // socket.io's connectionStateRecovery restored this session
      // transparently (same socket id, buffered room events replayed) —
      // no resync needed.
      if (this.socket.recovered) return;
      void this.resyncAfterReconnect();
    });

    this.socket.on("disconnect", (reason) => {
      console.warn("[voice] socket disconnected:", reason);
      // Don't tear down local publish/subscribe state here — socket.io's
      // default reconnection is active and "connect" above handles catch-up
      // once it's back.
    });

    this.socket.on("connect_error", (err) => {
      console.warn("[voice] socket connect_error:", err?.message || err);
    });
  }

  async resyncAfterReconnect() {
    if (this.resyncing) return;
    this.resyncing = true;

    try {
      const identity = this.activeIdentity;
      if (!identity || !this.socket) return;

      // Re-announce presence — the server-side game room may have pruned us
      // (or we may have missed events for others) during the gap. The
      // server responds with a fresh "game-room-participants", handled by
      // reconcileParticipants() below, which also re-subscribes/re-publishes
      // as needed.
      this.socket.emit("join-game-room", {
        roomId: this.config.roomId,
        participantId: identity.participantId,
        displayName: identity.displayName,
        userId: identity.userId,
        avatarImageUrl: identity.avatarImageUrl,
      });
    } finally {
      this.resyncing = false;
    }
  }

  // Reconciles the local roster/subscriptions against a fresh, authoritative
  // participant list from the server. Unlike a plain upsert, this also
  // removes anyone we're tracking locally that the server no longer lists
  // (covers events missed during a disconnect gap), and re-establishes any
  // WebRTC connection that isn't currently healthy.
  reconcileParticipants(participants) {
    const freshIds = new Set(participants.map((p) => p.participantId));
    const selfId = this.activeIdentity?.participantId;

    for (const knownId of Array.from(this.playbackUrls.keys())) {
      if (knownId === selfId || freshIds.has(knownId)) continue;
      this.unsubscribeRemoteUser(knownId);
      this.playbackUrls.delete(knownId);
      this.onParticipantRemove(knownId);
    }

    participants.forEach((participant) => this.upsertParticipant(participant));

    for (const participant of participants) {
      if (participant.participantId === selfId) continue;
      this.playbackUrls.set(participant.participantId, participant.playbackUrl);

      const existingPc = this.subscriberPcs.get(participant.participantId);
      if (!existingPc || !["connected", "completed"].includes(existingPc.iceConnectionState)) {
        this.subscribeRemoteUser(participant.participantId).catch((err) => {
          console.error(`[voice] resync subscribe(${participant.participantId}) failed:`, err);
        });
      }
    }

    const publishState = this.publishPc?.iceConnectionState;
    if (!this.publishPc || ["failed", "closed", "disconnected"].includes(publishState)) {
      this.publishMicrophone().catch((err) => {
        console.error("[voice] resync re-publish failed:", err);
      });
    }
  }

  async publishMicrophone({ isCurrent = () => true } = {}) {
    if (!this.roomInfo) throw new Error("Call joinRoom() first.");

    if (this.config.mock) {
      const tracks = [{ kind: "audio", enabled: true, stop() {} }];
      this.localStream = {
        getTracks: () => tracks,
        getAudioTracks: () => tracks,
      };
      await new Promise((resolve) => setTimeout(resolve, 250));
      return;
    }

    // Acquire the mic once; recovery re-publishes reuse the same
    // MediaStream/tracks and only rebuild the peer connection below.
    if (!this.localStream) {
      this.localStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
      this.localStream.getAudioTracks().forEach((track) => { track.enabled = !this.config.startMuted; });
    }
    if (!isCurrent()) {
      this.localStream.getTracks().forEach((track) => track.stop());
      this.localStream = null;
      throw new DOMException("The game room changed", "AbortError");
    }

    this.publishPc?.close();
    clearTimeout(this.publishRecoveryTimer);

    this.publishPc = new RTCPeerConnection({ iceServers: this.iceServers });
    this.attachPublishRecoveryHandlers(this.publishPc);
    this.localStream.getAudioTracks().forEach((track) => this.publishPc.addTrack(track, this.localStream));

    const offer = await this.publishPc.createOffer();
    await this.publishPc.setLocalDescription(offer);
    await waitForIceGathering(this.publishPc);

    const whipRes = await fetch(this.roomInfo.publishUrl, {
      method: "POST",
      headers: { "Content-Type": "application/sdp" },
      body: this.publishPc.localDescription.sdp,
    });
    if (!whipRes.ok) throw new Error("WHIP publish failed.");

    await this.publishPc.setRemoteDescription({ type: "answer", sdp: await whipRes.text() });
  }

  attachPublishRecoveryHandlers(pc) {
    pc.oniceconnectionstatechange = () => {
      const state = pc.iceConnectionState;

      if (state === "connected" || state === "completed") {
        clearTimeout(this.publishRecoveryTimer);
        return;
      }

      if (state === "failed") {
        clearTimeout(this.publishRecoveryTimer);
        this.recoverPublish();
        return;
      }

      if (state === "disconnected") {
        // Often transient (brief network hiccup) — give it a grace window
        // to self-recover before tearing down and re-publishing.
        clearTimeout(this.publishRecoveryTimer);
        this.publishRecoveryTimer = setTimeout(() => {
          if (this.publishPc === pc && pc.iceConnectionState === "disconnected") {
            this.recoverPublish();
          }
        }, CONNECTION_RECOVERY_GRACE_MS);
      }
    };
  }

  async recoverPublish() {
    if (!this.localStream || this.config.mock) return;

    console.warn("[voice] publish connection degraded, re-publishing microphone");
    try {
      await this.publishMicrophone();
    } catch (err) {
      console.error("[voice] recoverPublish failed:", err);
    }
  }

  async subscribeRemoteUser(remoteParticipantId) {
    const playbackUrl = this.playbackUrls.get(remoteParticipantId);
    if (!playbackUrl || remoteParticipantId === this.activeIdentity?.participantId) return;

    this.unsubscribeRemoteUser(remoteParticipantId);

    if (this.config.mock) {
      await new Promise((resolve) => setTimeout(resolve, 120));
      return;
    }

    const pc = new RTCPeerConnection({ iceServers: this.iceServers });
    this.subscriberPcs.set(remoteParticipantId, pc);
    this.attachSubscribeRecoveryHandlers(pc, remoteParticipantId);
    const remoteStream = new MediaStream();

    pc.addTransceiver("audio", { direction: "recvonly" });
    pc.ontrack = (event) => {
      event.streams[0]?.getTracks().forEach((track) => remoteStream.addTrack(track));
    };

    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);
    await waitForIceGathering(pc);

    const whepRes = await fetch(playbackUrl, {
      method: "POST",
      headers: { "Content-Type": "application/sdp" },
      body: pc.localDescription.sdp,
    });
    if (!whepRes.ok) throw new Error("WHEP subscribe failed.");

    await pc.setRemoteDescription({ type: "answer", sdp: await whepRes.text() });

    const audio = document.createElement("audio");
    audio.autoplay = true;
    audio.playsInline = true;
    audio.srcObject = remoteStream;
    document.body.appendChild(audio);
    this.audioElements.set(remoteParticipantId, audio);

    await this.playAudioWhenAllowed(audio);
  }

  attachSubscribeRecoveryHandlers(pc, remoteParticipantId) {
    pc.oniceconnectionstatechange = () => {
      const state = pc.iceConnectionState;

      if (state === "connected" || state === "completed") {
        clearTimeout(this.subscribeRecoveryTimers.get(remoteParticipantId));
        this.subscribeRecoveryTimers.delete(remoteParticipantId);
        return;
      }

      if (state === "failed") {
        clearTimeout(this.subscribeRecoveryTimers.get(remoteParticipantId));
        this.subscribeRecoveryTimers.delete(remoteParticipantId);
        this.subscribeRemoteUser(remoteParticipantId).catch((err) => {
          console.error(`[voice] resubscribe(${remoteParticipantId}) failed:`, err);
        });
        return;
      }

      if (state === "disconnected") {
        clearTimeout(this.subscribeRecoveryTimers.get(remoteParticipantId));
        this.subscribeRecoveryTimers.set(
          remoteParticipantId,
          setTimeout(() => {
            if (
              this.subscriberPcs.get(remoteParticipantId) === pc &&
              pc.iceConnectionState === "disconnected"
            ) {
              this.subscribeRemoteUser(remoteParticipantId).catch((err) => {
                console.error(`[voice] resubscribe(${remoteParticipantId}) failed:`, err);
              });
            }
          }, CONNECTION_RECOVERY_GRACE_MS),
        );
      }
    };
  }

  unsubscribeRemoteUser(remoteParticipantId) {
    clearTimeout(this.subscribeRecoveryTimers.get(remoteParticipantId));
    this.subscribeRecoveryTimers.delete(remoteParticipantId);

    this.subscriberPcs.get(remoteParticipantId)?.close();
    this.subscriberPcs.delete(remoteParticipantId);

    const audio = this.audioElements.get(remoteParticipantId);
    if (audio) {
      audio.srcObject = null;
      audio.remove();
      this.audioElements.delete(remoteParticipantId);
    }
  }

  startPresenceHeartbeat() {
    this.stopPresenceHeartbeat();
    this.heartbeatTimer = window.setInterval(() => {
      const identity = this.activeIdentity;
      if (!identity?.participantId || !this.socket) return;

      this.socket.emit("game-room-presence-heartbeat", {
        roomId: this.config.roomId,
        participantId: identity.participantId,
      });
    }, PRESENCE_HEARTBEAT_INTERVAL_MS);
  }

  stopPresenceHeartbeat() {
    if (!this.heartbeatTimer) return;
    window.clearInterval(this.heartbeatTimer);
    this.heartbeatTimer = null;
  }

  async leaveRoom() {
    const leavingIdentity = this.activeIdentity;
    const wasInRoom = Boolean(this.roomInfo);
    this.roomInfo = null;

    this.stopPresenceHeartbeat();
    clearTimeout(this.publishRecoveryTimer);
    this.subscribeRecoveryTimers.forEach((timer) => clearTimeout(timer));
    this.subscribeRecoveryTimers.clear();

    this.publishPc?.close();
    this.publishPc = null;
    this.subscriberPcs.forEach((pc) => pc.close());
    this.subscriberPcs.clear();
    Array.from(this.audioElements.keys()).forEach((participantId) => this.unsubscribeRemoteUser(participantId));
    this.localStream?.getTracks().forEach((track) => track.stop());
    this.localStream = null;

    if (this.socket) {
      this.socket.emit("leave-game-room");
      this.socket.removeAllListeners();
      this.socket.io.reconnection(false);
      this.socket.disconnect();
      this.socket = null;
    }

    if (this.config.mock) {
      this.playbackUrls.clear();
      this.clearMockSpeakingStates();
      this.activeIdentity = null;
      return;
    }

    if (wasInRoom && leavingIdentity) await fetch(`${this.config.backendBaseUrl}/api/v1/game-rooms/${encodeURIComponent(this.config.roomId)}/leave`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ participantId: leavingIdentity?.participantId }),
    }).catch(() => {});

    this.activeIdentity = null;
    this.playbackUrls.clear();
  }

  setMuted(isMuted) {
    if (!this.localStream) return;

    this.localStream.getAudioTracks().forEach((track) => {
      track.enabled = !isMuted;
    });
  }

  async playAudioWhenAllowed(audio) {
    try {
      await audio.play();
    } catch {
      console.log("Audio play blocked. Waiting for user click...");
      const unlock = () => {
        audio.play().catch(() => {});
      };
      window.addEventListener("click", unlock, { once: true });
      window.addEventListener("keydown", unlock, { once: true });
      window.addEventListener("pointerdown", unlock, { once: true });
    }
  }

  getSocket() {
    return this.socket;
  }

  getLocalStream() {
    return this.localStream;
  }

  getActiveIdentity() {
    return this.activeIdentity;
  }

  upsertParticipant(participant) {
    if (!participant?.participantId) return;
    this.onParticipantUpsert({
      participantId: participant.participantId,
      displayName: participant.displayName || participant.participantId,
      userId: participant.userId || null,
      avatarImageUrl: participant.avatarImageUrl || null,
    });
  }

  clearMockSpeakingStates() {
    if (this.mockSpeakingTimer) {
      window.clearInterval(this.mockSpeakingTimer);
      this.mockSpeakingTimer = null;
    }
  }

  startMockSpeakingStates() {
    this.clearMockSpeakingStates();
    const ids = [
      this.activeIdentity?.participantId,
      ...this.roomInfo.participants.map((participant) => participant.participantId),
    ].filter(Boolean);
    let index = 0;

    this.mockSpeakingTimer = window.setInterval(() => {
      ids.forEach((participantId, participantIndex) => {
        this.onSpeakingState({
          roomId: this.config.roomId,
          participantId,
          speaking: participantIndex === index,
          muted: false,
          audioLevel: participantIndex === index ? 0.12 : 0,
          updatedAt: new Date().toISOString(),
        });
      });
      index = (index + 1) % ids.length;
    }, 1800);
  }
}
