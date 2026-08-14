mergeInto(LibraryManager.library, {
  MangosVoiceJoin: function (
    gameObjectNamePtr,
    callbackMethodPtr,
    backendBaseUrlPtr,
    socketUrlPtr,
    socketPathPtr,
    roomIdPtr,
    participantPrefixPtr,
    displayNamePtr,
    requestTimeoutMilliseconds,
    iceGatheringTimeoutMilliseconds,
    subscribeRetryCount
  ) {
    const gameObjectName = UTF8ToString(gameObjectNamePtr);
    const callbackMethod = UTF8ToString(callbackMethodPtr);

    const notifyUnity = function (event) {
      if (window.unityInstance) {
        window.unityInstance.SendMessage(
          gameObjectName,
          callbackMethod,
          JSON.stringify(event)
        );
      }
    };

    if (!window.MangosVoice) {
      notifyUnity({
        state: "failed",
        message: "The browser voice client is unavailable.",
        muted: false
      });
      return;
    }

    window.MangosVoice.join({
      backendBaseUrl: UTF8ToString(backendBaseUrlPtr),
      socketUrl: UTF8ToString(socketUrlPtr),
      socketPath: UTF8ToString(socketPathPtr),
      roomId: UTF8ToString(roomIdPtr),
      participantPrefix: UTF8ToString(participantPrefixPtr),
      displayName: UTF8ToString(displayNamePtr),
      requestTimeoutMilliseconds: requestTimeoutMilliseconds,
      iceGatheringTimeoutMilliseconds: iceGatheringTimeoutMilliseconds,
      subscribeRetryCount: subscribeRetryCount
    }, notifyUnity).catch(function (error) {
      notifyUnity({
        state: "failed",
        message: error && error.message ? error.message : "Voice communication failed.",
        muted: false
      });
    });
  },

  MangosVoiceSetMuted: function (muted) {
    if (window.MangosVoice) {
      window.MangosVoice.setMuted(Boolean(muted));
    }
  },

  MangosVoiceLeave: function () {
    if (window.MangosVoice) {
      window.MangosVoice.leave();
    }
  }
});
