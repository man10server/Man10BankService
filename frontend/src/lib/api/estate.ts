import type { EstateHistory } from '../types/estate'

const estateHistoryPathPrefix = '/api/Estate'

function resolveEstateHistoryUrl(apiBaseUrl: string, uuid: string, limit: number, offset: number): string {
  const encodedUuid = encodeURIComponent(uuid)
  const url = new URL(`${estateHistoryPathPrefix}/${encodedUuid}/history`, `${apiBaseUrl}/`)
  url.searchParams.set('limit', limit.toString())
  url.searchParams.set('offset', offset.toString())
  return url.toString()
}

function toTimestamp(dateString: string): number {
  const timestamp = new Date(dateString).getTime()
  return Number.isNaN(timestamp) ? 0 : timestamp
}

export async function fetchLatestEstateHistory(
  apiBaseUrl: string,
  uuid: string,
  limit = 500
): Promise<EstateHistory[]> {
  const safeLimit = Math.min(Math.max(limit, 1), 1000)
  const response = await fetch(resolveEstateHistoryUrl(apiBaseUrl, uuid, safeLimit, 0), {
    headers: {
      Accept: 'application/json'
    }
  })

  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`)
  }

  const rows = (await response.json()) as EstateHistory[]
  return rows.sort((a, b) => toTimestamp(a.date) - toTimestamp(b.date))
}
