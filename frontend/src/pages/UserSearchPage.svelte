<script lang="ts">
  import { resolveUuidByMinecraftId } from '../lib/api/minecraftProfile'

  export let onOpenUser: (uuid: string) => void

  let minecraftId = ''
  let searching = false
  let errorMessage = ''

  async function handleSubmit(): Promise<void> {
    const input = minecraftId.trim()
    if (!input) {
      errorMessage = 'MinecraftIDを入力してください。'
      return
    }

    searching = true
    errorMessage = ''

    try {
      const resolvedUuid = await resolveUuidByMinecraftId(input)
      if (!resolvedUuid) {
        errorMessage = '指定したMinecraftIDのユーザーが見つかりませんでした。'
        return
      }

      onOpenUser(resolvedUuid)
    } catch (error) {
      const message = error instanceof Error ? error.message : '不明なエラー'
      if (message.startsWith('HTTP ')) {
        errorMessage = `ユーザー検索に失敗しました (${message})`
      } else {
        errorMessage = `APIに接続できませんでした: ${message}`
      }
    } finally {
      searching = false
    }
  }
</script>

<section class="card">
  <h1>ユーザー検索</h1>
  <p class="meta">MinecraftIDを入力すると、対応するユーザー資産ページを開きます。</p>

  <form class="form" on:submit|preventDefault={handleSubmit}>
    <label for="minecraft-id">MinecraftID</label>
    <input
      id="minecraft-id"
      type="text"
      bind:value={minecraftId}
      placeholder="例: Alice"
      autocomplete="off"
      maxlength={16}
    />
    <button type="submit" disabled={searching}>
      {searching ? '検索中...' : 'ユーザーページを開く'}
    </button>
  </form>

  {#if errorMessage}
    <p class="status error">{errorMessage}</p>
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
    font-size: 0.92rem;
  }

  .form {
    margin-top: 16px;
    display: grid;
    gap: 8px;
  }

  label {
    font-size: 0.88rem;
    color: #34435f;
    font-weight: 600;
  }

  input {
    width: 100%;
    border: 1px solid #c8d3e8;
    border-radius: 10px;
    padding: 10px 12px;
    font-size: 0.95rem;
    outline: none;
    transition: border-color 0.15s;
  }

  input:focus {
    border-color: #1e4db7;
  }

  button {
    margin-top: 6px;
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
    }

    .meta {
      font-size: 0.88rem;
      line-height: 1.4;
    }

    input,
    button {
      font-size: 0.9rem;
    }
  }
</style>
