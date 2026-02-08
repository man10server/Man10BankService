<script lang="ts">
  import { onMount } from 'svelte'
  import { fetchHealth, resolveHealthUrl } from '../lib/api/health'
  import type { HealthPayload } from '../lib/types/health'

  const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5035'
  const healthUrl = resolveHealthUrl(apiBaseUrl)

  let loading = false
  let errorMessage = ''
  let healthData: HealthPayload | null = null
  let lastCheckedAt = ''

  function toLocalString(value: string): string {
    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
      return value
    }
    return date.toLocaleString('ja-JP')
  }

  async function checkHealth(): Promise<void> {
    loading = true
    errorMessage = ''
    healthData = null

    try {
      healthData = await fetchHealth(apiBaseUrl)
      lastCheckedAt = new Date().toLocaleString('ja-JP')
    } catch (error) {
      const message = error instanceof Error ? error.message : '不明なエラー'
      if (message.startsWith('HTTP ')) {
        errorMessage = `ヘルスチェックに失敗しました (${message})`
      } else {
        errorMessage = `APIに接続できませんでした: ${message}`
      }
    } finally {
      loading = false
    }
  }

  onMount(() => {
    void checkHealth()
  })
</script>

<section class="card">
  <h1>Man10BankService ヘルスチェック</h1>
  <p class="meta">接続先: {healthUrl}</p>

  <button type="button" on:click={checkHealth} disabled={loading}>
    {#if loading}
      確認中...
    {:else}
      再確認
    {/if}
  </button>

  {#if loading}
    <p class="status">状態を確認しています...</p>
  {:else if errorMessage}
    <p class="status error">{errorMessage}</p>
  {:else if healthData}
    <dl class="health-grid">
      <div>
        <dt>サービス名</dt>
        <dd>{healthData.service}</dd>
      </div>
      <div>
        <dt>DB接続</dt>
        <dd class:ok={healthData.database} class:ng={!healthData.database}>
          {healthData.database ? '正常' : '異常'}
        </dd>
      </div>
      <div>
        <dt>サーバ時刻 (UTC)</dt>
        <dd>{toLocalString(healthData.serverTimeUtc)}</dd>
      </div>
      <div>
        <dt>起動時刻 (UTC)</dt>
        <dd>{toLocalString(healthData.startedAtUtc)}</dd>
      </div>
      <div>
        <dt>稼働秒数</dt>
        <dd>{healthData.uptimeSeconds.toLocaleString('ja-JP')} 秒</dd>
      </div>
      <div>
        <dt>最終確認</dt>
        <dd>{lastCheckedAt}</dd>
      </div>
    </dl>
  {/if}
</section>

<style>
  .card {
    width: min(760px, 100%);
    background: #ffffff;
    border: 1px solid #d9e1ef;
    border-radius: 14px;
    box-shadow: 0 10px 24px rgba(19, 33, 68, 0.08);
    padding: 24px;
  }

  h1 {
    margin: 0;
    font-size: 1.5rem;
    color: #1f2a44;
  }

  .meta {
    margin-top: 8px;
    color: #4e5d7a;
    word-break: break-all;
    font-size: 0.92rem;
  }

  button {
    margin-top: 16px;
    border: none;
    background: #1e4db7;
    color: #fff;
    border-radius: 10px;
    padding: 10px 14px;
    font-size: 0.95rem;
    cursor: pointer;
  }

  button:disabled {
    opacity: 0.6;
    cursor: wait;
  }

  .status {
    margin-top: 16px;
    color: #1f2a44;
  }

  .error {
    color: #b42318;
  }

  .health-grid {
    margin-top: 18px;
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 12px;
  }

  .health-grid div {
    border: 1px solid #e3e9f5;
    border-radius: 10px;
    padding: 12px;
    background: #fbfcfe;
  }

  dt {
    color: #63708c;
    font-size: 0.85rem;
  }

  dd {
    margin: 6px 0 0;
    font-weight: 600;
    color: #1f2a44;
  }

  .ok {
    color: #067647;
  }

  .ng {
    color: #b42318;
  }

  @media (max-width: 640px) {
    .health-grid {
      grid-template-columns: 1fr;
    }
  }
</style>
