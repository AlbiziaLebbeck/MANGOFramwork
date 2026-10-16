import { createAuthPoller, fetchPublicProfiles } from "./auth.js";
import { getVoiceConfig } from "./config.js";
import { createParticipantIdentity } from "./identity.js";
import { createLocalSpeakingDetector, createSpeakingStateSender } from "./speaking.js";
import { ensureSocketIoClient } from "./socket-loader.js";
import { ChatClient } from "./chat-client.js";
import { createVoiceOverlayUi } from "./ui.js";
import { VideoShareClient } from "./video-share.js";
import { VoiceClient } from "./voice-client.js";

export function startVoiceOverlay() {
  if (window.MangosVoiceOverlay) return Promise.resolve(window.MangosVoiceOverlay);
  return new Promise((resolve) => {
  const init = () => {
    const config = getVoiceConfig();
    const identityStore = createParticipantIdentity();
    const ui = createVoiceOverlayUi();
    ui.setAuthStatus(false);
    const pendingProfileUserIds = new Set();
    const profileCache = new Map();
    const participantIdsByUserId = new Map();
    let profileFetchTimer = null;

    const resolveProfileForParticipant = (participant) => {
      if (!participant?.userId || participant.avatarImageUrl) return;

      const cachedProfile = profileCache.get(participant.userId);
      if (cachedProfile) {
        ui.upsertParticipant({
          ...participant,
          displayName: participant.displayName || cachedProfile.displayName,
          avatarImageUrl: cachedProfile.avatarImageUrl,
        });
        return;
      }

      pendingProfileUserIds.add(participant.userId);
      if (profileFetchTimer) return;

      profileFetchTimer = window.setTimeout(async () => {
        profileFetchTimer = null;
        const userIds = Array.from(pendingProfileUserIds);
        pendingProfileUserIds.clear();

        try {
          const profiles = await fetchPublicProfiles(config, userIds);
          profiles.forEach((profile) => profileCache.set(profile.id, profile));
          profiles.forEach((profile) => {
            participantIdsByUserId.get(profile.id)?.forEach((participantId) => {
              ui.upsertParticipant({
                participantId,
                userId: profile.id,
                displayName: profile.displayName,
                avatarImageUrl: profile.avatarImageUrl,
              });
            });
          });
        } catch {
          // Remote avatars are optional. Keep initials if profile lookup fails.
        }
      }, 100);
    };

    const upsertParticipant = (participant) => {
      if (participant?.userId && participant?.participantId) {
        const participantIds = participantIdsByUserId.get(participant.userId) || new Set();
        participantIds.add(participant.participantId);
        participantIdsByUserId.set(participant.userId, participantIds);
      }

      ui.upsertParticipant(participant);
      resolveProfileForParticipant(participant);
    };

    const voiceClient = new VoiceClient({
      config,
      getIdentity: () => identityStore.current,
      onParticipantUpsert: upsertParticipant,
      onParticipantRemove: (participantId) => {
        participantIdsByUserId.forEach((participantIds, userId) => {
          participantIds.delete(participantId);
          if (!participantIds.size) participantIdsByUserId.delete(userId);
        });
        ui.removeParticipant(participantId);
      },
      onSpeakingState: (state) => ui.setSpeakingState(state),
    });

    const videoShareClient = new VideoShareClient({
      config,
      getIdentity: () => voiceClient.getActiveIdentity() || identityStore.current,
      getSocket: () => voiceClient.getSocket(),
      onStateChange: (entries) => ui.setVideoShareState(entries),
      onError: (message) => {
        console.warn(message);
      },
    });

    // A tile's inline "stop sharing" control (see ui.js buildVideoTile)
    // needs to stop that specific share — a participant can now own up
    // to 2, so this can't be inferred from a single global button anymore.
    ui.onStopShare((shareId) => {
      videoShareClient.stopShare(shareId).catch((err) => {
        console.error(err);
        alert("Video share error: " + err.message);
      });
    });

    const chatClient = new ChatClient({
      config,
      getIdentity: () => voiceClient.getActiveIdentity() || identityStore.current,
      getSocket: () => voiceClient.getSocket(),
      onHistory: (messages) => ui.setChatMessages(messages),
      onMessage: (message) => ui.appendChatMessage(message),
      onError: (message) => ui.setChatError(message),
    });

    let isJoined = false;
    let isMuted = false;
    let isConnecting = false;
    let speakingDetector = null;
    let hasGameRoom = !config.requireGameRoom;
    let requestedRoom = hasGameRoom ? config.roomId : "";
    let sessionRevision = 0;
    let disposed = false;
    let operation = Promise.resolve();
    const enqueue = (action) => {
      operation = operation.then(action, action);
      return operation;
    };
    const publishState = () => {
      const state = { connected: isJoined, muted: !isJoined || isMuted, roomId: config.roomId };
      window.unityInstance?.SendMessage("MangosBrowserBridge", "OnVoiceState", JSON.stringify(state));
      window.dispatchEvent(new CustomEvent("mangos:voice-state", { detail: state }));
    };

    const makeSpeakingSender = () => createSpeakingStateSender({
      getSocket: () => voiceClient.getSocket(),
      roomId: config.roomId,
      getIdentity: () => voiceClient.getActiveIdentity() || identityStore.current,
      onSpeaking: (state) => ui.setSpeakingState(state),
    });
    let speakingSender = makeSpeakingSender();

    const stopSpeakingDetector = () => {
      speakingDetector?.stop();
      speakingDetector = null;
    };

    const startSpeakingDetector = () => {
      if (config.mock) return;

      stopSpeakingDetector();
      speakingDetector = createLocalSpeakingDetector({
        stream: voiceClient.getLocalStream(),
        initiallyMuted: isMuted,
        onChange: speakingSender,
      });
    };

    const connectVoiceImpl = async () => {
      if (disposed || isConnecting || isJoined || !hasGameRoom) return;
      const revision = sessionRevision;
      const checkSession = () => {
        if (revision !== sessionRevision || disposed) {
          const error = new Error("The game room changed while connecting.");
          error.name = "AbortError";
          throw error;
        }
      };

      isConnecting = true;
      try {
        ui.setConnecting();

        if (!config.mock) await ensureSocketIoClient(config);
        checkSession();
        const info = await voiceClient.joinRoom();
        checkSession();
        videoShareClient.attachSocket();
        chatClient.attachSocket();
        config.startMuted = isMuted;
        await voiceClient.publishMicrophone({ isCurrent: () => revision === sessionRevision && !disposed });
        checkSession();
        voiceClient.setMuted(isMuted);
        startSpeakingDetector();

        // "Connected" only requires my own mic to be published and the
        // room joined — it must not wait on every other participant's
        // WHEP subscribe to finish first. On a slow/high-latency
        // connection (mobile networks especially), sequentially awaiting
        // one subscribe at a time here could keep the UI stuck on
        // "Connecting..." for many seconds even though the user is
        // already fully joined; each remote subscribe's audio/video
        // still arrives as soon as it's ready, via its own ontrack.
        isJoined = true;
        ui.setConnected();
        ui.setChatEnabled(true);
        ui.setMuted(isMuted);
        publishState();

        void Promise.all(
          info.participants.map((participant) =>
            voiceClient.subscribeRemoteUser(participant.participantId).catch((error) => {
              console.warn(`Subscribe to ${participant.participantId} failed.`, error);
            }),
          ),
        );

        await chatClient.refreshHistory().catch((error) => {
          console.warn("Chat history refresh failed.", error);
        });
        await videoShareClient.refreshState().catch((error) => {
          console.warn("Video share state refresh failed.", error);
        });
      } catch (err) {
        await disconnectVoiceImpl();
        if (err.name !== "AbortError") {
          console.error(err);
          alert("Connect error: " + err.message);
          ui.setConnectError();
        }
      } finally {
        isConnecting = false;
      }
    };

    const disconnectVoiceImpl = async () => {
      stopSpeakingDetector();
      chatClient.cleanup();
      try { await videoShareClient.cleanup({ stopLocal: true }); }
      catch (error) { console.warn("Video cleanup failed", error); }
      await voiceClient.leaveRoom();
      participantIdsByUserId.clear();
      pendingProfileUserIds.clear();
      if (profileFetchTimer) {
        window.clearTimeout(profileFetchTimer);
        profileFetchTimer = null;
      }
      ui.clearParticipants();
      isJoined = false;
      ui.clearChat();
      ui.setDisconnected();
      ui.setMuted(isMuted);
      ui.elements.btnToggleVoice.disabled = disposed || !hasGameRoom;
      publishState();
    };
    const connectVoice = () => enqueue(connectVoiceImpl);
    const disconnectVoice = () => enqueue(disconnectVoiceImpl);

    const reconnectAsAuthenticatedUser = async () => {
      return enqueue(async () => {
        if (!isJoined || disposed) return;
        await disconnectVoiceImpl();
        await connectVoiceImpl();
      });
    };

    const authPoller = createAuthPoller({
      config,
      onUser: async (user) => {
        if (user) identityStore.applyUser(user);
        else identityStore.reset();
        ui.setAuthStatus(Boolean(user));
        if (user) await reconnectAsAuthenticatedUser();
        else await disconnectVoice();
      },
    });
    if (!config.mock) authPoller.start();

    const bindControlPress = (element, handler) => {
      let lastTouchHandledAt = 0;

      const shouldIgnoreClick = () =>
        lastTouchHandledAt && performance.now() - lastTouchHandledAt < 700;

      element.addEventListener(
        "touchstart",
        (event) => {
          event.stopPropagation();
        },
        { passive: true },
      );

      element.addEventListener(
        "touchend",
        (event) => {
          if (element.disabled) return;

          // iOS browsers all use WebKit. After media capture/playback starts,
          // WebKit can fail to deliver the follow-up synthetic click for fixed
          // bottom controls, leaving the visible tray feeling frozen.
          event.preventDefault();
          event.stopPropagation();
          lastTouchHandledAt = performance.now();
          handler(event);
        },
        { passive: false },
      );

      element.addEventListener("click", (event) => {
        if (shouldIgnoreClick()) {
          event.preventDefault();
          event.stopPropagation();
          return;
        }

        handler(event);
      });
    };

    bindControlPress(ui.elements.btnToggleVoice, async () => {
      if (!isJoined) {
        if (!config.mock) await authPoller.retryNow();
        await connectVoice();
      } else {
        await disconnectVoice();
      }
    });

    bindControlPress(ui.elements.btnMute, () => {
      isMuted = !isMuted;
      voiceClient.setMuted(isMuted);
      speakingDetector?.setMuted(isMuted);
      speakingSender({
        speaking: false,
        muted: isMuted,
        audioLevel: 0,
      });
      publishState();
      ui.setMuted(isMuted);
    });

    bindControlPress(ui.elements.drawerToggle, () => {
      ui.toggleDrawer();
    });

    // Stopping a specific share now happens via each tile's inline
    // control (wired above through ui.onStopShare) — this button only
    // opens the share-type menu to start a new one. It's disabled by
    // setVideoShareState() whenever there's no free slot for this viewer.
    bindControlPress(ui.elements.btnShareVideo, () => {
      if (!isJoined) return;
      ui.toggleShareMenu();
    });

    const startVideoShare = async (shareType) => {
      ui.toggleShareMenu(false);
      ui.setVideoShareBusy(true);
      try {
        await videoShareClient.startShare(shareType);
      } catch (err) {
        console.error(err);
        alert("Video share error: " + err.message);
      } finally {
        ui.setVideoShareBusy(false);
      }
    };

    bindControlPress(ui.elements.btnShareScreen, () => {
      void startVideoShare("screen");
    });

    bindControlPress(ui.elements.btnShareCamera, () => {
      void startVideoShare("camera");
    });

    ui.elements.videoClose.addEventListener("click", (event) => {
      event.stopPropagation();
      ui.closeVideoModal();
    });

    ui.elements.chatToggle.addEventListener("click", () => {
      ui.toggleChat();
    });

    ui.elements.chatClose.addEventListener("click", () => {
      ui.toggleChat(false);
    });

    ui.elements.chatForm.addEventListener("submit", (event) => {
      event.preventDefault();
      const sent = chatClient.sendMessage(ui.elements.chatInput.value);
      if (sent) ui.elements.chatInput.value = "";
    });

    const onPageHide = () => {
      videoShareClient.stopShareBestEffort();
      voiceClient.getLocalStream()?.getTracks().forEach((track) => track.stop());
      void disconnectVoice();
    };
    window.addEventListener("pagehide", onPageHide);

    const api = {
      join: connectVoice,
      leave: disconnectVoice,
      getState: () => ({ roomId: config.roomId, connected: isJoined, muted: isMuted, ready: hasGameRoom }),
      setSession(session) {
        const room = session?.roomId ? `${config.roomNamespace}:${session.roomId}` : "";
        if (room === requestedRoom && hasGameRoom === Boolean(room)) return operation;
        requestedRoom = room;
        sessionRevision++;
        return enqueue(async () => {
          if (room !== requestedRoom || disposed) return;
          const rejoin = isJoined;
          await disconnectVoiceImpl();
          config.roomId = room;
          hasGameRoom = Boolean(room);
          speakingSender = makeSpeakingSender();
          ui.elements.btnToggleVoice.disabled = !hasGameRoom;
          ui.elements.btnToggleVoice.title = hasGameRoom ? "Join this game's voice room" : "Enter a game room to use voice";
          publishState();
          if (rejoin && hasGameRoom) await connectVoiceImpl();
        });
      },
      async dispose() {
        disposed = true;
        sessionRevision++;
        authPoller.stop();
        window.removeEventListener("pagehide", onPageHide);
        await disconnectVoice();
      },
    };
    window.MangosVoiceOverlay = api;
    ui.elements.btnToggleVoice.disabled = !hasGameRoom;
    if (!navigator.mediaDevices?.getDisplayMedia) ui.elements.btnShareScreen.disabled = true;
    if (window.MANGOS_SESSION) void api.setSession(window.MANGOS_SESSION);
    resolve(api);
  };

  if (document.readyState === "loading") {
    window.addEventListener("DOMContentLoaded", init, { once: true });
  } else {
    init();
  }
  });
}
