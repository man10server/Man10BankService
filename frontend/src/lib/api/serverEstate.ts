import type { ServerEstateHistory } from '../types/serverEstate'

const historyPath = '/api/ServerEstate/history'

function resolveHistoryUrl(apiBaseUrl: string, limit: number, offset: number): string {
  const url = new URL(historyPath, `${apiBaseUrl}/`)
  url.searchParams.set('limit', limit.toString())
  url.searchParams.set('offset', offset.toString())
  return url.toString()
}

function toTimestamp(dateString: string): number {
  const timestamp = new Date(dateString).getTime()
  return Number.isNaN(timestamp) ? 0 : timestamp
}

export async function fetchLatestServerEstateHistory(
  apiBaseUrl: string,
  limit = 500
): Promise<ServerEstateHistory[]> {
  const safeLimit = Math.min(Math.max(limit, 1), 1000)
  const response = await fetch(resolveHistoryUrl(apiBaseUrl, safeLimit, 0), {
    headers: {
      Accept: 'application/json'
    }
  })

  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`)
  }

  const rows = (await response.json()) as ServerEstateHistory[]
  return rows
    .sort((a, b) => toTimestamp(a.date) - toTimestamp(b.date))
}
