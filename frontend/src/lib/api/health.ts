import type { HealthPayload } from '../types/health'

const healthPath = '/api/Health'

export function resolveHealthUrl(apiBaseUrl: string): string {
  return new URL(healthPath, `${apiBaseUrl}/`).toString()
}

export async function fetchHealth(apiBaseUrl: string): Promise<HealthPayload> {
  const response = await fetch(resolveHealthUrl(apiBaseUrl), {
    headers: {
      Accept: 'application/json'
    }
  })

  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`)
  }

  return (await response.json()) as HealthPayload
}
