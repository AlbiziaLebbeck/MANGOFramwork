const fullNameOf = (user) => {
  const fullName = [user.firstName, user.lastName].filter(Boolean).join(" ").trim();
  return fullName || user.email || user.id;
};

const normalizeAvatarUrl = (avatarImageUrl, authBaseUrl) => {
  if (!avatarImageUrl) return null;
  return new URL(avatarImageUrl, authBaseUrl).href;
};

const normalizeUser = (payload, authBaseUrl) => {
  const user = payload?.data?.User;
  if (!user?.id) return null;

  return {
    id: user.id,
    firstName: user.firstName || "",
    lastName: user.lastName || "",
    email: user.email || "",
    displayName: fullNameOf(user),
    avatarImageUrl: normalizeAvatarUrl(user.avatarImageUrl, authBaseUrl),
  };
};

const normalizePublicProfile = (profile, authBaseUrl) => {
  if (!profile?.id) return null;

  return {
    id: profile.id,
    displayName: profile.displayName || profile.id,
    avatarImageUrl: normalizeAvatarUrl(profile.avatarImageUrl, authBaseUrl),
  };
};

export async function fetchCurrentMangosUser(config) {
  const res = await fetch(`${config.authBaseUrl}/users/me`, {
    credentials: "include",
  });

  if (res.status === 401 || res.status === 403) return null;
  if (!res.ok) throw new Error(`Account service returned ${res.status}`);

  return normalizeUser(await res.json(), config.authBaseUrl);
}

export async function fetchPublicProfiles(config, userIds) {
  const uniqueIds = Array.from(new Set(userIds.map((id) => id?.trim()).filter(Boolean)));
  if (!uniqueIds.length) return [];

  const url = new URL(`${config.authBaseUrl}/users/public-profiles`);
  url.searchParams.set("userIds", uniqueIds.join(","));

  const res = await fetch(url.href, {
    credentials: "include",
  });

  if (!res.ok) return [];

  const payload = await res.json();
  return (payload?.data?.Users || [])
    .map((profile) => normalizePublicProfile(profile, config.authBaseUrl))
    .filter(Boolean);
}

export function createAuthPoller({ config, onUser }) {
  let timer = null;
  let startedAt = 0;
  let stopped = false;
  let currentUserId = null;
  let inFlight = null;

  const clearTimer = () => {
    if (timer) {
      window.clearTimeout(timer);
      timer = null;
    }
  };

  const schedule = () => {
    if (stopped) return;

    const elapsed = Date.now() - startedAt;
    const delay =
      !currentUserId && elapsed < config.authRetryFastWindowMs
        ? config.authRetryInitialIntervalMs
        : config.authRetryLongIntervalMs;

    timer = window.setTimeout(check, delay);
  };

  const check = async () => {
    if (stopped) return null;
    if (inFlight) return inFlight;
    inFlight = (async () => {
      try {
        const user = await fetchCurrentMangosUser(config);
        if (stopped) return null;
        const nextUserId = user?.id || null;
        if (nextUserId !== currentUserId) {
          currentUserId = nextUserId;
          await onUser(user);
        }
        return user;
      } catch {
        // A network/server failure is not evidence of logout.
        return null;
      } finally {
        inFlight = null;
        clearTimer();
        schedule();
      }
    })();
    return inFlight;
  };

  const start = () => {
    stopped = false;
    startedAt = Date.now();
    clearTimer();
    void check();
  };

  const retryNow = async () => {
    clearTimer();
    return check();
  };

  const stop = () => {
    stopped = true;
    clearTimer();
  };

  return {
    start,
    retryNow,
    stop,
  };
}
