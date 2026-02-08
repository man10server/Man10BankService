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
  let chartCanvas: HTMLCanvasElement | null = null
  let mounted = false

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

  onMount(() => {
    mounted = true
    renderChart(labels, datasets)
  })

  $: if (mounted) {
    renderChart(labels, datasets)
  }

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

<div class="chart-wrap" style:height={`${height}px`}>
  <canvas bind:this={chartCanvas} aria-label={ariaLabel}></canvas>
</div>

<style>
  .chart-wrap {
    width: 100%;
  }
</style>
