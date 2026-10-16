export function createParticipantIdentity() {
  const deviceId = localStorage.getItem("voice.deviceId") || crypto.randomUUID();
  localStorage.setItem("voice.deviceId", deviceId);

  const tabId = sessionStorage.getItem("voice.tabId") || crypto.randomUUID();
  sessionStorage.setItem("voice.tabId", tabId);

  const anonymousIdentity = {
    participantId: `${deviceId}-${tabId}-${Date.now()}`,
    displayName: "Player_" + Math.floor(Math.random() * 1000),
    userId: null,
    avatarImageUrl: null,
    isAuthenticated: false,
    user: null,
  };

  let currentIdentity = anonymousIdentity;

  return {
    get tabId() {
      return tabId;
    },
    get current() {
      return currentIdentity;
    },
    get anonymous() {
      return anonymousIdentity;
    },
    reset() {
      currentIdentity = anonymousIdentity;
      return currentIdentity;
    },
    applyUser(user) {
      currentIdentity = {
        participantId: `${user.id}-${tabId}`,
        displayName: user.displayName,
        userId: user.id,
        avatarImageUrl: user.avatarImageUrl,
        isAuthenticated: true,
        user,
      };
      return currentIdentity;
    },
  };
}
