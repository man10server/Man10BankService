<script lang="ts">
  import { onMount } from 'svelte'
  import AssetTrendChart from '../lib/components/AssetTrendChart.svelte'
  import { fetchLatestServerEstateHistory } from '../lib/api/serverEstate'
  import type { AssetTrendDataset } from '../lib/types/assetTrendChart'
  import type { ServerEstateHistory } from '../lib/types/serverEstate'

  export let onOpenHealth: () => void

  const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5035'
  const chartLimit = 500

  let loading = false
  let errorMessage = ''
  let historyRows: ServerEstateHistory[] = []
  let chartLabels: string[] = []
  let chartDatasets: AssetTrendDataset[] = []
  let lastCheckedAt = ''

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

  function buildChartData(rows: ServerEstateHistory[]): void {
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

  async function loadEstateHistory(): Promise<void> {
    loading = true
    errorMessage = ''

    try {
      const rows = await fetchLatestServerEstateHistory(apiBaseUrl, chartLimit)
      historyRows = rows
      lastCheckedAt = new Date().toLocaleString('ja-JP')
    } catch (error) {
      const message = error instanceof Error ? error.message : '不明なエラー'
      if (message.startsWith('HTTP ')) {
        errorMessage = `資産履歴の取得に失敗しました (${message})`
      } else {
        errorMessage = `APIに接続できませんでした: ${message}`
      }
      historyRows = []
      chartLabels = []
      chartDatasets = []
      lastCheckedAt = ''
    } finally {
      loading = false
    }
  }

  onMount(() => {
    void loadEstateHistory()
  })

  $: buildChartData(historyRows)
</script>

<section class="card">
  <h1>Man10Bank</h1>

  {#if errorMessage}
    <p class="status error">{errorMessage}</p>
  {:else if loading}
    <p class="status">資産履歴を取得しています...</p>
  {:else if historyRows.length === 0}
    <p class="status">直近500件のデータがありません。</p>
  {/if}

  <div class="chart-area">
    <AssetTrendChart
      labels={chartLabels}
      datasets={chartDatasets}
      ariaLabel="サーバー資産履歴グラフ"
      height={320}
      mobileHeight={210}
    />
  </div>

  {#if lastCheckedAt}
    <p class="status">最終更新: {lastCheckedAt}</p>
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

  .endpoint {
    margin-top: 4px;
    color: #63708c;
    font-size: 0.85rem;
    word-break: break-all;
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

  .secondary {
    background: #edf2fb;
    color: #1f2a44;
    border: 1px solid #c6d3ea;
  }

  button:disabled {
    opacity: 0.6;
    cursor: wait;
  }

  .chart-area {
    margin-top: 16px;
  }

  .status {
    margin-top: 12px;
    color: #1f2a44;
    font-size: 0.92rem;
  }

  .error {
    color: #b42318;
  }

  @media (max-width: 430px) {
    .card {
      width: 100%;
      padding: 14px;
      border-radius: 12px;
    }

    h1 {
      font-size: 1.25rem;
      line-height: 1.35;
    }

    .meta {
      font-size: 0.88rem;
    }

    .endpoint {
      font-size: 0.8rem;
    }

    .actions {
      margin-top: 12px;
      gap: 6px;
    }

    button {
      width: 100%;
      font-size: 0.9rem;
      padding: 10px 12px;
    }

    .chart-area {
      margin-top: 12px;
    }

    .status {
      font-size: 0.88rem;
    }
  }
</style>
