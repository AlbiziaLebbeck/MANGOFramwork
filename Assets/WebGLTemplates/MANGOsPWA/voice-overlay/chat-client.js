export class ChatClient {
  constructor({ config, getIdentity, getSocket, onHistory, onMessage, onError }) {
    this.config = config;
    this.getIdentity = getIdentity;
    this.getSocket = getSocket;
    this.onHistory = onHistory || (() => {});
    this.onMessage = onMessage || (() => {});
    this.onError = onError || (() => {});
    this.socket = null;
    this.boundSocketHandlers = null;
  }

  attachSocket() {
    const socket = this.getSocket?.();
    if (!socket || socket === this.socket) return;

    this.detachSocket();
    this.socket = socket;
    this.boundSocketHandlers = {
      history: ({ messages }) => {
        this.onHistory(Array.isArray(messages) ? messages : []);
      },
      created: ({ message }) => {
        if (message) this.onMessage(message);
      },
      error: ({ message }) => {
        this.onError(message || "Chat failed.");
      },
    };

    socket.on("game-room-chat-history", this.boundSocketHandlers.history);
    socket.on("game-room-chat-message-created", this.boundSocketHandlers.created);
    socket.on("game-room-chat-error", this.boundSocketHandlers.error);
  }

  detachSocket() {
    if (!this.socket || !this.boundSocketHandlers) return;

    this.socket.off("game-room-chat-history", this.boundSocketHandlers.history);
    this.socket.off("game-room-chat-message-created", this.boundSocketHandlers.created);
    this.socket.off("game-room-chat-error", this.boundSocketHandlers.error);
    this.socket = null;
    this.boundSocketHandlers = null;
  }

  sendMessage(body) {
    const text = body?.trim() || "";
    const identity = this.getIdentity?.();

    if (!text) return false;
    if (!identity?.participantId) {
      this.onError("Join voice before sending chat.");
      return false;
    }

    if (this.config.mock) {
      this.onMessage({
        id: `mock-${Date.now()}`,
        roomId: this.config.roomId,
        participantId: identity.participantId,
        displayName: identity.displayName,
        userId: identity.userId || null,
        avatarImageUrl: identity.avatarImageUrl || null,
        body: text,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      });
      return true;
    }

    if (!this.socket?.connected) {
      this.onError("Chat is not connected.");
      return false;
    }

    this.socket.emit("game-room-chat-message", {
      roomId: this.config.roomId,
      participantId: identity.participantId,
      body: text,
    });
    return true;
  }

  async refreshHistory() {
    if (this.config.mock) {
      this.onHistory([]);
      return;
    }

    const res = await fetch(
      `${this.config.backendBaseUrl}/api/v1/game-rooms/${encodeURIComponent(this.config.roomId)}/chat?limit=100`,
    );
    const payload = await res.json().catch(() => null);
    if (!res.ok || !payload?.data?.messages) {
      throw new Error(payload?.message || "Failed to load chat.");
    }

    this.onHistory(payload.data.messages);
  }

  cleanup() {
    this.detachSocket();
  }
}
