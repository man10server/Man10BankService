const mojangProfileApiBaseUrl =
  import.meta.env.VITE_MOJANG_API_BASE_URL ?? '/mojang-api/users/profiles/minecraft'

type MojangProfileResponse = {
  id?: string
  name?: string
}

function resolveMojangProfileUrl(minecraftId: string): string {
  return `${mojangProfileApiBaseUrl}/${encodeURIComponent(minecraftId)}`
}

function normalizeUuid(uuid: string): string {
  const trimmed = uuid.trim()
  const hex = trimmed.replaceAll('-', '').toLowerCase()
  if (!/^[0-9a-f]{32}$/.test(hex)) {
    return trimmed
  }

  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

export async function resolveUuidByMinecraftId(minecraftId: string): Promise<string | null> {
  const input = minecraftId.trim()
  if (!input) {
    return null
  }

  const uuidLikePattern = /^[0-9a-fA-F-]{32,36}$/
  if (uuidLikePattern.test(input)) {
    return normalizeUuid(input)
  }

  const response = await fetch(resolveMojangProfileUrl(input), {
    headers: {
      Accept: 'application/json'
    }
  })

  if (response.status === 404 || response.status === 204) {
    return null
  }

  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`)
  }

  const profile = (await response.json()) as MojangProfileResponse
  const resolvedUuid = profile.id?.trim()
  return resolvedUuid ? normalizeUuid(resolvedUuid) : null
}
