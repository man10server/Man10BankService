import type { MoneyLog } from '../types/moneyLog'

const bankPathPrefix = '/api/Bank'

function resolveBankLogsUrl(apiBaseUrl: string, uuid: string, limit: number, offset: number): string {
  const encodedUuid = encodeURIComponent(uuid)
  const url = new URL(`${bankPathPrefix}/${encodedUuid}/logs`, `${apiBaseUrl}/`)
  url.searchParams.set('limit', limit.toString())
  url.searchParams.set('offset', offset.toString())
  return url.toString()
}

export async function fetchBankLogs(
  apiBaseUrl: string,
  uuid: string,
  limit = 20,
  offset = 0
): Promise<MoneyLog[]> {
  const safeLimit = Math.min(Math.max(limit, 1), 1000)
  const safeOffset = Math.max(offset, 0)
  const response = await fetch(resolveBankLogsUrl(apiBaseUrl, uuid, safeLimit, safeOffset), {
    headers: {
      Accept: 'application/json'
    }
  })

  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`)
  }

  return (await response.json()) as MoneyLog[]
}
