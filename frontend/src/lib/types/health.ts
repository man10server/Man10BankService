export type HealthPayload = {
  service: string
  serverTimeUtc: string
  startedAtUtc: string
  uptimeSeconds: number
  database: boolean
}
