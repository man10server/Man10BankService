<script lang="ts">
  import { onMount } from 'svelte'
  import AssetTrendChart from '../lib/components/AssetTrendChart.svelte'
  import { fetchBankLogs } from '../lib/api/bank'
  import { fetchLatestEstateHistory } from '../lib/api/estate'
  import type { AssetTrendDataset } from '../lib/types/assetTrendChart'
  import type { EstateHistory } from '../lib/types/estate'
  import type { MoneyLog } from '../lib/types/moneyLog'

  export let uuid: string

  const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5035'
  const chartLimit = 500
  const logsPageSize = 10

  let loadingHistory = false
  let loadingLogs = false
  let historyErrorMessage = ''
  let logsErrorMessage = ''
  let historyRows: EstateHistory[] = []
  let chartLabels: string[] = []
  let chartDatasets: AssetTrendDataset[] = []
  let bankLogs: MoneyLog[] = []
  let logsPage = 1
  let hasNextLogsPage = false
  let minecraftId = ''
  let lastCheckedAt = ''
  let loadedUuid = ''

  function formatAxisLabel(dateString: string): string {
    const date = new Date(dateString)
    if (Number.isNaN(date.getTime())) {
      return dateString
    }

    const month = `${date.getMonth() + 1}`.padStart(2, '0')
    const day = `${date.getDate()}`.padStart(2, '0')
    const hour = `${date.getHours()}`.padStart(2, '0')
    return `${month}/${day} ${hour}:00`
  }

  function buildChartData(rows: EstateHistory[]): void {
    chartLabels = rows.map((row) => formatAxisLabel(row.date))
    chartDatasets = [
      {
        label: '総資産',
        data: rows.map((row) => Number(row.total)),
        color: '#2563eb'
      },
      {
        label: '銀行',
        data: rows.map((row) => Number(row.bank)),
        color: '#38bdf8'
      },
      {
        label: 'リボ',
        data: rows.map((row) => Number(row.loan)),
        color: '#ef4444'
      },
      {
        label: '電子マネー',
        data: rows.map((row) => Number(row.vault)),
        color: '#eab308'
      },
      {
        label: '現金',
        data: rows.map((row) => Number(row.cash)),
        color: '#16a34a'
      },
      {
        label: 'その他',
        data: rows.map((row) => Number(row.estateAmount) + Number(row.crypto) + Number(row.shop)),
        color: '#6b7280'
      }
    ]
  }

  function formatDateTime(dateString: string): string {
    const date = new Date(dateString)
    if (Number.isNaN(date.getTime())) {
      return dateString
    }
    return date.toLocaleString('ja-JP')
  }

  function formatAmount(value: number): string {
    return new Intl.NumberFormat('ja-JP', {
      maximumFractionDigits: 0
    }).format(value)
  }

  async function loadEstateHistoryAsync(): Promise<void> {
    if (!uuid) {
      return
    }

    loadingHistory = true
    historyErrorMessage = ''

    try {
      const rows = await fetchLatestEstateHistory(apiBaseUrl, uuid, chartLimit)
      historyRows = rows
      minecraftId = rows.length > 0 ? rows[rows.length - 1].player : ''
      lastCheckedAt = new Date().toLocaleString('ja-JP')
    } catch (error) {
      const message = error instanceof Error ? error.message : '不明なエラー'
      if (message.startsWith('HTTP ')) {
        historyErrorMessage = `資産履歴の取得に失敗しました (${message})`
      } else {
        historyErrorMessage = `APIに接続できませんでした: ${message}`
      }
      historyRows = []
      chartLabels = []
      chartDatasets = []
      minecraftId = ''
      lastCheckedAt = ''
    } finally {
      loadingHistory = false
    }
  }

  async function loadBankLogsAsync(page: number): Promise<void> {
    if (!uuid) {
      return
    }

    loadingLogs = true
    logsErrorMessage = ''
    const offset = (page - 1) * logsPageSize

    try {
      const rows = await fetchBankLogs(apiBaseUrl, uuid, logsPageSize, offset)
      if (rows.length === 0 && page > 1) {
        hasNextLogsPage = false
        return
      }

      bankLogs = rows
      logsPage = page
      hasNextLogsPage = rows.length === logsPageSize
      if (!minecraftId && rows.length > 0) {
        minecraftId = rows[0].player
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : '不明なエラー'
      if (message.startsWith('HTTP ')) {
        logsErrorMessage = `取引ログの取得に失敗しました (${message})`
      } else {
        logsErrorMessage = `APIに接続できませんでした: ${message}`
      }
      bankLogs = []
      hasNextLogsPage = false
    } finally {
      loadingLogs = false
    }
  }

  async function reloadAllAsync(): Promise<void> {
    logsPage = 1
    await Promise.all([
      loadEstateHistoryAsync(),
      loadBankLogsAsync(1)
    ])
  }

  function goPrevLogsPage(): void {
    if (logsPage <= 1 || loadingLogs) {
      return
    }
    void loadBankLogsAsync(logsPage - 1)
  }

  function goNextLogsPage(): void {
    if (!hasNextLogsPage || loadingLogs) {
      return
    }
    void loadBankLogsAsync(logsPage + 1)
  }

  onMount(() => {
    loadedUuid = uuid
    void reloadAllAsync()
  })

  $: buildChartData(historyRows)
  $: if (uuid && uuid !== loadedUuid) {
    loadedUuid = uuid
    bankLogs = []
    chartLabels = []
    chartDatasets = []
    historyRows = []
    minecraftId = ''
    lastCheckedAt = ''
    hasNextLogsPage = false
    logsPage = 1
    void reloadAllAsync()
  }
</script>

<section class="card">
  <h1>{minecraftId || 'ユーザー'}の資産推移</h1>
  <p class="meta">MinecraftID: {minecraftId || '-'}</p>
  <p class="meta">UUID: {uuid}</p>

  <div class="actions">
    <button type="button" on:click={reloadAllAsync} disabled={loadingHistory || loadingLogs}>
      {loadingHistory || loadingLogs ? '読み込み中...' : '履歴を再取得'}
    </button>
  </div>

  {#if historyErrorMessage}
    <p class="status error">{historyErrorMessage}</p>
  {:else if loadingHistory}
    <p class="status">資産履歴を取得しています...</p>
  {:else if historyRows.length === 0}
    <p class="status">指定ユーザーの履歴がありません。</p>
  {/if}

  <div class="chart-area">
    <AssetTrendChart labels={chartLabels} datasets={chartDatasets} ariaLabel="ユーザー資産履歴グラフ" />
  </div>

  {#if lastCheckedAt}
    <p class="status">最終更新: {lastCheckedAt}</p>
  {/if}

  <section class="logs">
    <h2>取引ログ</h2>

    {#if logsErrorMessage}
      <p class="status error">{logsErrorMessage}</p>
    {:else if loadingLogs}
      <p class="status">取引ログを取得しています...</p>
    {:else if bankLogs.length === 0}
      <p class="status">取引ログがありません。</p>
    {:else}
      <ul class="log-list">
        {#each bankLogs as log}
          <li class="log-item">
            <div class="log-row muted">
              <span class="log-row-display-note">{log.displayNote || log.note || '-'}</span>
              <span>{log.server}</span>
            </div>
            <div class="log-row">
              <span class="log-date">{formatDateTime(log.date)}</span>
              <span class:deposit={log.deposit} class:withdraw={!log.deposit}>
                {log.deposit ? '入金' : '出金'} {formatAmount(log.amount)}
              </span>
            </div>
          </li>
        {/each}
      </ul>
    {/if}

    <div class="pager">
      <button type="button" class="secondary" on:click={goPrevLogsPage} disabled={loadingLogs || logsPage <= 1}>
        前へ
      </button>
      <span>{logsPage} ページ目</span>
      <button type="button" class="secondary" on:click={goNextLogsPage} disabled={loadingLogs || !hasNextLogsPage}>
        次へ
      </button>
    </div>
  </section>
</section>

<style>
  .card {
    width: min(980px, 100%);
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
    margin-top: 6px;
    color: #4e5d7a;
    word-break: break-all;
    font-size: 0.92rem;
  }

  .actions {
    margin-top: 16px;
    display: flex;
    gap: 8px;
    flex-wrap: wrap;
  }

  button {
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

  .chart-area {
    margin-top: 16px;
  }

  .logs {
    margin-top: 22px;
    border-top: 1px solid #e4ebf7;
    padding-top: 18px;
  }

  h2 {
    margin: 0;
    font-size: 1.2rem;
    color: #1f2a44;
  }

  .log-list {
    list-style: none;
    margin: 14px 0 0;
    padding: 0;
    display: grid;
    gap: 10px;
  }

  .log-item {
    border: 1px solid #dbe2ee;
    border-radius: 10px;
    padding: 10px 12px;
    background: #fbfcff;
  }

  .log-row-display-note {
    color: #000000;
  }

  .log-row {
    display: flex;
    justify-content: space-between;
    gap: 10px;
    align-items: center;
    font-size: 0.9rem;
    color: #1f2a44;
  }

  .log-row + .log-row {
    margin-top: 6px;
  }

  .log-date {
    color: #4e5d7a;
  }

  .muted {
    color: #66758f;
  }

  .deposit {
    color: #067647;
    font-weight: 700;
  }

  .withdraw {
    color: #b42318;
    font-weight: 700;
  }

  .pager {
    margin-top: 12px;
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 12px;
    color: #1f2a44;
  }

  .status {
    margin-top: 12px;
    color: #1f2a44;
    font-size: 0.92rem;
  }

  .error {
    color: #b42318;
  }
</style>
