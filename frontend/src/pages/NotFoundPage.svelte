<script lang="ts">
  export let path: string
  export let onGoHome: () => void
</script>

<section class="card">
  <h1>ページが見つかりません</h1>
  <p class="meta">指定したパス: {path}</p>
  <button type="button" on:click={onGoHome}>トップへ戻る</button>
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
</style>
