import { waitForIceGathering } from "./webrtc.js";

const HEARTBEAT_INTERVAL_MS = 10000;
// Room-wide cap, mirrored client-side purely for UI messaging — the
// server (gameRoom.service.ts MAX_CONCURRENT_VIDEO_SHARES) is authoritative.
const MAX_CONCURRENT_VIDEO_SHARES = 2;

const jsonOrNull = async (res) => {
  try {
    return await res.json();
  } catch {
    return null;
  }
};

export class VideoShareClient {
  constructor({ config, getIdentity, getSocket, onStateChange, onError }) {
    this.config = config;
    this.getIdentity = getIdentity;
    this.getSocket = getSocket;
    this.onStateChange = onStateChange || (() => {});
    this.onError = onError || (() => {});

    // All keyed by shareId. A single participant may now own up to 2
    // shares (both slots), so every one of these can hold more than one
    // entry — unlike the old singular activeShare/localStream/etc. fields.
    this.shares = new Map();        // shareId -> GameRoomVideoShare (mine + others')
    this.localStreams = new Map();  // shareId -> MediaStream (mine)
    this.remoteStreams = new Map(); // shareId -> MediaStream (others')
    this.publishPcs = new Map();    // shareId -> RTCPeerConnection (mine)
    this.subscribePcs = new Map();  // shareId -> RTCPeerConnection (others')
    this.heartbeatTimers = new Map(); // shareId -> interval handle

    this.socket = null;
    this.boundSocketHandlers = null;
    this.stopping = new Set(); // shareIds currently mid-stopShare()
  }

  // True if I own at least one active share.
  get isLocalSharing() {
    return this.myShareIds.length > 0;
  }

  // shareIds of shares owned by the current identity — 0, 1, or 2 entries.
  // There is deliberately no singular "myShareId": callers that need to
  // act on a specific one of my shares (e.g. stopping it) must pick one.
  get myShareIds() {
    const identity = this.getIdentity();
    if (!identity?.participantId) return [];
    return Array.from(this.shares.values())
      .filter((share) => share.participantId === identity.participantId)
      .map((share) => share.shareId);
  }

  attachSocket() {
    const socket = this.getSocket?.();
    if (!socket || socket === this.socket) return;

    this.detachSocket();
    this.socket = socket;
    this.boundSocketHandlers = {
      state: ({ activeVideoShares }) => {
        void this.handleShareState(activeVideoShares || []).catch((error) => {
          this.onError(error.message || "Video share state failed.");
        });
      },
      started: ({ activeVideoShare }) => {
        if (!activeVideoShare) return;
        void this.handleShareStarted(activeVideoShare).catch((error) => {
          this.onError(error.message || "Video share subscribe failed.");
        });
      },
      stopped: ({ shareId }) => {
        if (!shareId) return;
        void this.handleShareStopped(shareId).catch(() => {});
      },
      error: ({ message }) => {
        this.onError(message || "Video share failed.");
        void this.refreshState();
      },
    };

    socket.on("game-room-video-share-state", this.boundSocketHandlers.state);
    socket.on("game-room-video-share-started", this.boundSocketHandlers.started);
    socket.on("game-room-video-share-stopped", this.boundSocketHandlers.stopped);
    socket.on("game-room-video-share-error", this.boundSocketHandlers.error);
  }

  detachSocket() {
    if (!this.socket || !this.boundSocketHandlers) return;

    this.socket.off("game-room-video-share-state", this.boundSocketHandlers.state);
    this.socket.off("game-room-video-share-started", this.boundSocketHandlers.started);
    this.socket.off("game-room-video-share-stopped", this.boundSocketHandlers.stopped);
    this.socket.off("game-room-video-share-error", this.boundSocketHandlers.error);
    this.socket = null;
    this.boundSocketHandlers = null;
  }

  async refreshState() {
    if (this.config.mock) return;

    const res = await fetch(
      `${this.config.backendBaseUrl}/api/v1/game-rooms/${encodeURIComponent(this.config.roomId)}/video-share`,
    );
    const payload = await jsonOrNull(res);
    if (!res.ok) return;

    await this.handleShareState(payload?.data?.activeVideoShares || []);
  }

  async startShare(shareType) {
    if (this.config.mock) return;

    const identity = this.getIdentity();
    if (!identity?.participantId) {
      throw new Error("Join voice before sharing video.");
    }

    // Client-side courtesy gate mirroring the server's single rejection
    // case (no per-participant limit — a room-wide capacity check only).
    // Restarting one of my own shares of the same type is always allowed
    // even when the room is "full", since that's not a new share.
    const ownShareOfType = Array.from(this.shares.values()).find(
      (share) => share.participantId === identity.participantId && share.shareType === shareType,
    );
    if (!ownShareOfType && this.shares.size >= MAX_CONCURRENT_VIDEO_SHARES) {
      throw new Error(`Video share limit reached (${MAX_CONCURRENT_VIDEO_SHARES}/${MAX_CONCURRENT_VIDEO_SHARES}).`);
    }

    const stream =
      shareType === "screen"
        ? await navigator.mediaDevices.getDisplayMedia({ video: true, audio: true })
        : await navigator.mediaDevices.getUserMedia({ video: true, audio: true });

    let activeVideoShare;
    try {
      activeVideoShare = await this.startShareOnBackend(shareType, identity);
      this.localStreams.set(activeVideoShare.shareId, stream);
      this.shares.set(activeVideoShare.shareId, activeVideoShare);
      this.notifyState();

      await this.publishVideo(activeVideoShare, stream);

      stream.getVideoTracks().forEach((track) => {
        track.onended = () => {
          // e.g. the browser's own native "Stop sharing" bar/button, which
          // bypasses our UI entirely — still goes through the same
          // retried stopShare() path so a failed stop is surfaced instead
          // of silently leaving the room locked for everyone else.
          this.stopShare(activeVideoShare.shareId).catch((error) => {
            console.error("[video-share] stop after track ended failed:", error);
          });
        };
      });

      this.socket?.emit("game-room-video-share-started", {
        roomId: this.config.roomId,
        participantId: identity.participantId,
        displayName: identity.displayName,
        userId: identity.userId,
        avatarImageUrl: identity.avatarImageUrl,
        shareType,
        shareId: activeVideoShare.shareId,
      });
      this.startHeartbeat(activeVideoShare.shareId);
    } catch (error) {
      stream.getTracks().forEach((track) => track.stop());
      // Best-effort cleanup — stopShareOnBackend() now throws on exhausted
      // retries, but a failure here must not mask the original `error`
      // we're about to re-throw below.
      if (activeVideoShare) {
        await this.stopShareOnBackend(identity.participantId, activeVideoShare.shareId).catch(() => {});
        this.clearLocalShare(activeVideoShare.shareId);
        this.shares.delete(activeVideoShare.shareId);
        this.notifyState();
      }
      throw error;
    }
  }

  async stopShare(shareId) {
    if (!shareId || this.stopping.has(shareId)) return;

    const identity = this.getIdentity();
    const share = this.shares.get(shareId);
    if (!share || !identity?.participantId || share.participantId !== identity.participantId) return;

    this.stopping.add(shareId);

    try {
      // Best-effort — the socket event is a fast-path notification; the
      // authoritative stop is the retried REST call below, and the
      // backend's periodic stale-share sweep is the fallback if even that
      // is lost (e.g. socket AND fetch both fail).
      this.socket?.emit("game-room-video-share-stopped", {
        roomId: this.config.roomId,
        participantId: identity.participantId,
        shareId,
      });
      await this.stopShareOnBackend(identity.participantId, shareId);
      this.stopping.delete(shareId);
      await this.handleShareStopped(shareId);
    } catch (error) {
      this.stopping.delete(shareId);
      // Don't clear local share state on failure — the server likely still
      // thinks we're sharing (blocking that slot for others), so leave
      // this share's tile/state active and let the user know, rather than
      // quietly resetting to a state that misrepresents what the server has.
      this.onError(
        (error && error.message) ||
          "Failed to stop sharing. It may take a little longer for others to be unblocked.",
      );
      throw error;
    }
  }

  // Best-effort stop of every share I own (e.g. on page unload) — a
  // participant can now own up to 2.
  stopShareBestEffort() {
    const identity = this.getIdentity();
    if (!identity?.participantId) return;

    for (const shareId of this.myShareIds) {
      this.socket?.emit("game-room-video-share-stopped", {
        roomId: this.config.roomId,
        participantId: identity.participantId,
        shareId,
      });

      const body = JSON.stringify({ participantId: identity.participantId, shareId });
      const url = `${this.config.backendBaseUrl}/api/v1/game-rooms/${encodeURIComponent(this.config.roomId)}/video-share/stop`;
      if (navigator.sendBeacon) {
        navigator.sendBeacon(url, new Blob([body], { type: "application/json" }));
      } else {
        fetch(url, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body,
          keepalive: true,
        }).catch(() => {});
      }

      this.clearLocalShare(shareId);
    }
  }

  async cleanup({ stopLocal = true } = {}) {
    if (stopLocal) {
      for (const shareId of this.myShareIds) {
        await this.stopShare(shareId).catch(() => {});
      }
    }

    this.detachSocket();

    for (const shareId of Array.from(this.shares.keys())) {
      this.clearLocalShare(shareId);
      this.clearRemoteShare(shareId);
    }
    this.shares.clear();
    this.notifyState();
  }

  async startShareOnBackend(shareType, identity) {
    const res = await fetch(
      `${this.config.backendBaseUrl}/api/v1/game-rooms/${encodeURIComponent(this.config.roomId)}/video-share/start`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          participantId: identity.participantId,
          displayName: identity.displayName,
          userId: identity.userId,
          avatarImageUrl: identity.avatarImageUrl,
          shareType,
        }),
      },
    );
    const payload = await jsonOrNull(res);
    if (!res.ok || !payload?.data?.activeVideoShare) {
      throw new Error(payload?.message || "Failed to start video share.");
    }
    return payload.data.activeVideoShare;
  }

  // Retries the stop request rather than silently swallowing a failed/lost
  // call. Previously this was fire-and-forget (`.catch(() => {})`), which
  // meant a failed request left the sharer's own UI reset while the
  // server's record was never cleared — silently locking that slot for
  // everyone else until the backend's TTL sweep happened to notice.
  // Throwing on exhausted retries lets stopShare() surface the failure
  // instead of pretending it worked.
  async stopShareOnBackend(participantId, shareId, { attempts = 3 } = {}) {
    const url = `${this.config.backendBaseUrl}/api/v1/game-rooms/${encodeURIComponent(this.config.roomId)}/video-share/stop`;
    let lastError;

    for (let attempt = 1; attempt <= attempts; attempt++) {
      try {
        const res = await fetch(url, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ participantId, shareId }),
        });
        if (res.ok) return;
        lastError = new Error(`Stop video share failed with status ${res.status}.`);
      } catch (error) {
        lastError = error;
      }

      if (attempt < attempts) {
        await new Promise((resolve) => window.setTimeout(resolve, attempt * 500));
      }
    }

    throw lastError || new Error("Failed to stop video share.");
  }

  async publishVideo(share, stream) {
    this.publishPcs.get(share.shareId)?.close();
    const pc = new RTCPeerConnection();
    this.publishPcs.set(share.shareId, pc);

    // Add transceivers in a fixed [video, audio] order — NOT
    // stream.getTracks().forEach(addTrack), whose m-line order follows
    // whatever order the browser happens to list tracks in (and is absent
    // entirely for the audio m-line if the stream has no audio track at
    // all, e.g. a screen share where the user didn't check "Share audio").
    // subscribeVideo() below always requests [video, audio] too — SRS's
    // WHEP answer order must match its WHIP offer's, so both sides need
    // the same fixed order regardless of which tracks actually exist,
    // or setRemoteDescription rejects the answer with "order of m-lines
    // in answer doesn't match order of m-lines in offer".
    const videoTrack = stream.getVideoTracks()[0];
    const audioTrack = stream.getAudioTracks()[0];
    pc.addTransceiver(videoTrack || "video", { direction: "sendonly", streams: [stream] });
    pc.addTransceiver(audioTrack || "audio", { direction: "sendonly", streams: [stream] });

    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);
    await waitForIceGathering(pc);

    const whipRes = await fetch(share.publishUrl, {
      method: "POST",
      headers: { "Content-Type": "application/sdp" },
      body: pc.localDescription.sdp,
    });
    if (!whipRes.ok) throw new Error("Video WHIP publish failed.");

    await pc.setRemoteDescription({ type: "answer", sdp: await whipRes.text() });
  }

  // Reconciles the full authoritative list from the server against local
  // state: removes anything local no longer present, adds/updates
  // everything present, and (re)subscribes to any remote share not yet
  // subscribed.
  async handleShareState(activeVideoShares) {
    const incoming = new Map(activeVideoShares.map((share) => [share.shareId, share]));

    for (const shareId of Array.from(this.shares.keys())) {
      if (!incoming.has(shareId)) {
        await this.handleShareStopped(shareId);
      }
    }

    const identity = this.getIdentity();
    for (const [shareId, share] of incoming) {
      this.shares.set(shareId, share);
      const isMine = identity?.participantId && share.participantId === identity.participantId;
      if (isMine) continue; // my own local stream/publishPc already exist from startShare()

      if (!this.remoteStreams.has(shareId) || !this.subscribePcs.has(shareId)) {
        await this.subscribeVideoWithRetry(share).catch((error) => {
          console.error(`[video-share] subscribe(${shareId}) failed:`, error);
        });
      }
    }

    this.notifyState();
  }

  // Merges one newly-started share (from the "started" socket event) into
  // local state without touching any other share.
  async handleShareStarted(share) {
    this.shares.set(share.shareId, share);

    const identity = this.getIdentity();
    const isMine = identity?.participantId && share.participantId === identity.participantId;
    if (!isMine) {
      await this.subscribeVideoWithRetry(share).catch((error) => {
        console.error(`[video-share] subscribe(${share.shareId}) failed:`, error);
      });
    }

    this.notifyState();
  }

  // Removes exactly one share from local state — critically, does NOT
  // touch any other active share (this is the fix for a bug where
  // stopping one share used to clear all video-share state for everyone).
  async handleShareStopped(shareId) {
    this.shares.delete(shareId);
    this.clearLocalShare(shareId);
    this.clearRemoteShare(shareId);
    this.notifyState();
  }

  async subscribeVideoWithRetry(share) {
    this.clearRemoteShare(share.shareId);

    for (let attempt = 1; attempt <= 5; attempt++) {
      try {
        await this.subscribeVideo(share);
        this.notifyState();
        return;
      } catch (error) {
        if (attempt === 5) throw error;
        await new Promise((resolve) => window.setTimeout(resolve, attempt * 600));
      }
    }
  }

  async subscribeVideo(share) {
    const pc = new RTCPeerConnection();
    this.subscribePcs.set(share.shareId, pc);
    const stream = new MediaStream();
    this.remoteStreams.set(share.shareId, stream);

    pc.addTransceiver("video", { direction: "recvonly" });
    pc.addTransceiver("audio", { direction: "recvonly" });
    pc.ontrack = (event) => {
      event.streams[0]?.getTracks().forEach((track) => stream.addTrack(track));
      this.notifyState();
    };

    try {
      const offer = await pc.createOffer();
      await pc.setLocalDescription(offer);
      await waitForIceGathering(pc);

      const whepRes = await fetch(share.playbackUrl, {
        method: "POST",
        headers: { "Content-Type": "application/sdp" },
        body: pc.localDescription.sdp,
      });
      if (!whepRes.ok) throw new Error("Video WHEP subscribe failed.");

      await pc.setRemoteDescription({ type: "answer", sdp: await whepRes.text() });
    } catch (error) {
      // Don't leave a half-negotiated WHEP session (and its SRS-side
      // resource) dangling — subscribeVideoWithRetry() will create a
      // fresh RTCPeerConnection on the next attempt.
      pc.close();
      if (this.subscribePcs.get(share.shareId) === pc) {
        this.subscribePcs.delete(share.shareId);
      }
      throw error;
    }
  }

  startHeartbeat(shareId) {
    this.stopHeartbeat(shareId);
    const timer = window.setInterval(() => {
      const identity = this.getIdentity();
      const share = this.shares.get(shareId);
      if (!identity?.participantId || !share) return;

      this.socket?.emit("game-room-video-share-heartbeat", {
        roomId: this.config.roomId,
        participantId: identity.participantId,
        shareId,
      });
    }, HEARTBEAT_INTERVAL_MS);
    this.heartbeatTimers.set(shareId, timer);
  }

  stopHeartbeat(shareId) {
    const timer = this.heartbeatTimers.get(shareId);
    if (!timer) return;
    window.clearInterval(timer);
    this.heartbeatTimers.delete(shareId);
  }

  clearLocalShare(shareId) {
    if (!shareId) return;
    this.stopHeartbeat(shareId);

    this.publishPcs.get(shareId)?.close();
    this.publishPcs.delete(shareId);

    const stream = this.localStreams.get(shareId);
    stream?.getTracks().forEach((track) => {
      track.onended = null;
      track.stop();
    });
    this.localStreams.delete(shareId);
  }

  clearRemoteShare(shareId) {
    if (!shareId) return;
    this.subscribePcs.get(shareId)?.close();
    this.subscribePcs.delete(shareId);
    this.remoteStreams.delete(shareId);
  }

  // Emits one { share, stream, isLocal } entry per active share — a
  // participant sharing both screen and camera produces 2 entries with
  // isLocal: true.
  notifyState() {
    const identity = this.getIdentity();
    const entries = Array.from(this.shares.values()).map((share) => {
      const isLocal = Boolean(identity?.participantId && share.participantId === identity.participantId);
      return {
        share,
        stream: isLocal ? this.localStreams.get(share.shareId) : this.remoteStreams.get(share.shareId),
        isLocal,
      };
    });
    this.onStateChange(entries);
  }
}
