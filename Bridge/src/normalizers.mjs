export function normalizeHostId(hostId) {
  return String(hostId || "").trim().replace(/^@/, "").toLowerCase();
}

function pickFirstUrl(...candidates) {
  const flattened = candidates.flat().filter(Boolean).map(value => String(value).trim()).filter(Boolean);
  if (flattened.length === 0) {
    return "";
  }

  const nonWebp = flattened.find(url => !url.toLowerCase().includes(".webp"));
  return nonWebp || flattened[0];
}

function extractAvatarUrl(source) {
  if (!source) {
    return "";
  }

  return pickFirstUrl(
    source.profilePictureUrl,
    source.avatarUrl,
    source.profilePicture?.urlList,
    source.avatarThumb?.urlList,
    source.avatarMedium?.urlList,
    source.avatarLarge?.urlList,
    source.avatarLarger?.urlList,
    source.user?.profilePictureUrl,
    source.user?.profilePicture?.urlList,
    source.user?.avatarThumb?.urlList,
    source.user?.avatarMedium?.urlList,
    source.user?.avatarLarge?.urlList
  );
}

export function normalizeUser(user) {
  if (!user) {
    return {
      userId: "",
      displayName: "",
      avatarUrl: ""
    };
  }

  const userId = String(user.uniqueId || "").trim();
  const displayName = String(user.nickname || user.uniqueId || "").trim();
  const avatarUrl = extractAvatarUrl(user);

  return {
    userId,
    displayName,
    avatarUrl
  };
}

export function normalizeEventUser(eventPayload) {
  const nestedUser = normalizeUser(eventPayload?.user);
  if (nestedUser.userId) {
    return nestedUser;
  }

  const topLevelUserId = String(
    eventPayload?.uniqueId || eventPayload?.userId || eventPayload?.senderId || ""
  ).trim();

  const topLevelDisplayName = String(
    eventPayload?.nickname || eventPayload?.displayName || topLevelUserId || ""
  ).trim();

  const topLevelAvatarUrl = String(
    extractAvatarUrl(eventPayload)
  ).trim();

  return {
    userId: topLevelUserId,
    displayName: topLevelDisplayName,
    avatarUrl: topLevelAvatarUrl
  };
}
