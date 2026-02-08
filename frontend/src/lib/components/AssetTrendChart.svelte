<script lang="ts">
  import { onDestroy, onMount } from 'svelte'
  import {
    Chart,
    CategoryScale,
    Legend,
    LineController,
    LineElement,
    LinearScale,
    PointElement,
    Tooltip,
    type ChartConfiguration
  } from 'chart.js'
  import type { AssetTrendDataset } from '../types/assetTrendChart'

  Chart.register(CategoryScale, LinearScale, LineController, LineElement, PointElement, Tooltip, Legend)

  export let labels: string[] = []
  export let datasets: AssetTrendDataset[] = []
  export let ariaLabel = '資産推移グラフ'
  export let height = 320
  export let mobileHeight = 220
  export let showLatestAmounts = true

  type LatestAmount = {
    label: string
    value: number
    color: string
  }

  let chartCanvas: HTMLCanvasElement | null = null
  let mounted = false
  let latestAmounts: LatestAmount[] = []

  function formatNumber(value: number): string {
    return new Intl.NumberFormat('ja-JP', {
      maximumFractionDigits: 0
    }).format(value)
  }

  function getMaxValue(currentDatasets: AssetTrendDataset[]): number {
    let maxValue = 0
    for (const dataset of currentDatasets) {
      for (const value of dataset.data) {
        if (value > maxValue) {
          maxValue = value
        }
      }
    }
    return maxValue
  }

  function buildConfig(currentLabels: string[], currentDatasets: AssetTrendDataset[]): ChartConfiguration<'line'> {
    const maxValue = getMaxValue(currentDatasets)
    const suggestedMax = maxValue > 0 ? maxValue + maxValue / 4 : undefined

    return {
      type: 'line',
      data: {
        labels: currentLabels,
        datasets: currentDatasets.map((dataset) => ({
          label: dataset.label,
          data: dataset.data,
          borderColor: dataset.color,
          borderWidth: 2,
          tension: 0.2,
          pointRadius: 0,
          pointHoverRadius: 3,
          fill: false
        }))
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        interaction: {
          mode: 'index',
          intersect: false
        },
        plugins: {
          legend: {
            display: true
          },
          tooltip: {
            callbacks: {
              label: (context) => `${context.dataset.label}: ${formatNumber(Number(context.parsed.y))}`
            }
          }
        },
        scales: {
          x: {
            ticks: {
              maxTicksLimit: 12
            }
          },
          y: {
            suggestedMax,
            ticks: {
              callback: (value) => formatNumber(Number(value))
            }
          }
        }
      }
    }
  }

  function renderChart(currentLabels: string[], currentDatasets: AssetTrendDataset[]): void {
    if (!chartCanvas) {
      return
    }

    const existingChart = Chart.getChart(chartCanvas)
    if (existingChart) {
      existingChart.destroy()
    }

    if (currentLabels.length === 0 || currentDatasets.length === 0) {
      return
    }

    new Chart(chartCanvas, buildConfig(currentLabels, currentDatasets))
  }

  function buildLatestAmounts(currentDatasets: AssetTrendDataset[]): void {
    latestAmounts = currentDatasets
      .map((dataset): LatestAmount | null => {
        if (dataset.data.length === 0) {
          return null
        }

        const value = Number(dataset.data[dataset.data.length - 1])
        if (!Number.isFinite(value)) {
          return null
        }

        return {
          label: dataset.label,
          value,
          color: dataset.color
        }
      })
      .filter((dataset): dataset is LatestAmount => dataset !== null)
  }

  onMount(() => {
    mounted = true
    renderChart(labels, datasets)
  })

  $: if (mounted) {
    renderChart(labels, datasets)
  }
  $: buildLatestAmounts(datasets)

  onDestroy(() => {
    if (!chartCanvas) {
      return
    }
    const existingChart = Chart.getChart(chartCanvas)
    if (existingChart) {
      existingChart.destroy()
    }
  })
</script>

{#if showLatestAmounts && latestAmounts.length > 0}
  <div class="latest-amounts">
    {#each latestAmounts as item}
      <span class="latest-item" style:color={item.color}>
        {item.label}:{formatNumber(item.value)}
      </span>
    {/each}
  </div>
{/if}

<div class="chart-wrap" style={`--chart-height:${height}px; --chart-height-mobile:${mobileHeight}px;`}>
  <canvas bind:this={chartCanvas} aria-label={ariaLabel}></canvas>
</div>

<style>
  .latest-amounts {
    margin-top: 14px;
    display: flex;
    flex-wrap: wrap;
    gap: 10px 14px;
    font-size: 0.95rem;
    font-weight: 600;
  }

  .latest-item {
    background: #f8fafc;
    border: 1px solid #dbe2ee;
    border-radius: 999px;
    padding: 4px 10px;
  }

  .chart-wrap {
    margin-top: 16px;
    width: 100%;
    height: var(--chart-height);
  }

  @media (max-width: 430px) {
    .latest-amounts {
      gap: 8px;
      font-size: 0.88rem;
    }

    .latest-item {
      padding: 3px 8px;
    }

    .chart-wrap {
      margin-top: 12px;
      height: var(--chart-height-mobile);
    }
  }
</style>
