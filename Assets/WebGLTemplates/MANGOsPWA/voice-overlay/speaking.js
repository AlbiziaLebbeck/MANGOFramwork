export function createLocalSpeakingDetector({
  stream,
  onChange,
  threshold = 0.035,
  silenceDelayMs = 450,
  intervalMs = 80,
  initiallyMuted = false,
}) {
  const AudioContextCtor = window.AudioContext || window.webkitAudioContext;
  if (!AudioContextCtor || !stream?.getAudioTracks?.().length) {
    return {
      setMuted: () => {},
      stop: () => {},
    };
  }

  const audioContext = new AudioContextCtor();
  const source = audioContext.createMediaStreamSource(stream);
  const analyser = audioContext.createAnalyser();
  let muted = Boolean(initiallyMuted);
  let speaking = false;
  let lastVoiceAt = 0;
  let stopped = false;

  analyser.fftSize = 1024;
  const samples = new Uint8Array(analyser.fftSize);
  source.connect(analyser);

  // On some mobile browsers (notably iOS Safari), an AudioContext only
  // reliably resumes when resume() is called synchronously within a user
  // gesture's call stack. This detector is typically constructed after
  // several awaited async steps (join room, publish mic, etc.), by which
  // point the original tap's call stack is long gone — resume() can then
  // silently no-op, leaving the context permanently "suspended" with
  // getByteTimeDomainData() reading nothing but silence forever, so
  // "speaking" never flips true even though the actual mic track (a
  // separate pipeline, feeding WebRTC directly) works fine. Retry resume
  // on a short interval and on the next real user interaction until it
  // actually takes, rather than assuming the first attempt worked.
  const stopResumeRetries = () => {
    window.clearInterval(resumeRetryId);
    window.removeEventListener("pointerdown", resumeOnGesture);
    window.removeEventListener("touchstart", resumeOnGesture);
    window.removeEventListener("keydown", resumeOnGesture);
  };

  const tryResume = () => {
    if (stopped) return;
    if (audioContext.state === "running") {
      stopResumeRetries();
      return;
    }
    void audioContext.resume().catch(() => {});
  };

  const resumeOnGesture = () => tryResume();
  window.addEventListener("pointerdown", resumeOnGesture);
  window.addEventListener("touchstart", resumeOnGesture);
  window.addEventListener("keydown", resumeOnGesture);
  const resumeRetryId = window.setInterval(tryResume, 1000);
  tryResume();

  const computeAudioLevel = () => {
    analyser.getByteTimeDomainData(samples);
    let sum = 0;

    samples.forEach((sample) => {
      const value = (sample - 128) / 128;
      sum += value * value;
    });

    return Math.sqrt(sum / samples.length);
  };

  const emitChange = (nextSpeaking, audioLevel) => {
    speaking = nextSpeaking;
    onChange({
      speaking,
      muted,
      audioLevel,
    });
  };

  const intervalId = window.setInterval(() => {
    if (stopped) return;

    const now = performance.now();
    const audioLevel = muted ? 0 : computeAudioLevel();
    if (audioLevel >= threshold) lastVoiceAt = now;

    const nextSpeaking = !muted && now - lastVoiceAt <= silenceDelayMs;
    if (nextSpeaking === speaking) return;

    emitChange(nextSpeaking, audioLevel);
  }, intervalMs);

  return {
    setMuted: (nextMuted) => {
      muted = Boolean(nextMuted);

      if (muted && speaking) {
        emitChange(false, 0);
      }
    },
    stop: () => {
      stopped = true;
      window.clearInterval(intervalId);
      stopResumeRetries();

      if (speaking) {
        emitChange(false, 0);
      }

      source.disconnect();
      analyser.disconnect();
      void audioContext.close();
    },
  };
}

export function createSpeakingStateSender({
  getSocket,
  roomId,
  getIdentity,
  onSpeaking,
}) {
  return ({ speaking, muted = false, audioLevel = 0 }) => {
    const identity = getIdentity();
    const state = {
      roomId,
      participantId: identity.participantId,
      displayName: identity.displayName,
      speaking: Boolean(speaking),
      muted: Boolean(muted),
      audioLevel:
        typeof audioLevel === "number" && Number.isFinite(audioLevel)
          ? Math.max(0, Math.min(1, audioLevel))
          : 0,
      updatedAt: new Date().toISOString(),
    };

    onSpeaking(state);

    const socket = getSocket();
    if (socket?.connected) {
      socket.emit("game-room-speaking-state", state);
    }
  };
}
