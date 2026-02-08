<script lang="ts">
  import { onMount } from 'svelte'
  import HomePage from './pages/HomePage.svelte'
  import HealthPage from './pages/HealthPage.svelte'
  import NotFoundPage from './pages/NotFoundPage.svelte'
  import UserSearchPage from './pages/UserSearchPage.svelte'
  import UserPage from './pages/UserPage.svelte'

  const homePath = '/'
  const healthPagePath = '/health'
  const userRootPath = '/user'
  const userPathPrefix = `${userRootPath}/`

  let currentPath = normalizePath(window.location.pathname)
  let currentUserUuid: string | null = extractUserUuid(currentPath)

  function normalizePath(pathname: string): string {
    if (!pathname) {
      return homePath
    }

    return pathname.endsWith('/') && pathname.length > 1
      ? pathname.slice(0, -1)
      : pathname
  }

  function navigate(path: string): void {
    const normalizedPath = normalizePath(path)
    if (normalizedPath === currentPath) {
      return
    }

    window.history.pushState({}, '', normalizedPath)
    currentPath = normalizedPath
  }

  function openUserPage(uuid: string): void {
    const encodedUuid = encodeURIComponent(uuid.trim())
    if (!encodedUuid) {
      return
    }
    navigate(`${userPathPrefix}${encodedUuid}`)
  }

  function extractUserUuid(path: string): string | null {
    if (!path.startsWith(userPathPrefix)) {
      return null
    }

    const rest = path.slice(userPathPrefix.length)
    if (!rest || rest.includes('/')) {
      return null
    }

    try {
      return decodeURIComponent(rest)
    } catch {
      return rest
    }
  }

  onMount(() => {
    const handlePopState = () => {
      currentPath = normalizePath(window.location.pathname)
    }

    window.addEventListener('popstate', handlePopState)
    return () => {
      window.removeEventListener('popstate', handlePopState)
    }
  })

  $: currentUserUuid = extractUserUuid(currentPath)
</script>

<main class="page">
  <nav class="nav">
    <a
      href={homePath}
      class:active={currentPath === homePath}
      on:click|preventDefault={() => navigate(homePath)}
    >
      トップ
    </a>
    <a
      href={healthPagePath}
      class:active={currentPath === healthPagePath}
      on:click|preventDefault={() => navigate(healthPagePath)}
    >
      ヘルスチェック
    </a>
    <a
      href={userRootPath}
      class:active={currentPath === userRootPath || !!currentUserUuid}
      on:click|preventDefault={() => navigate(userRootPath)}
    >
      ユーザー
    </a>
  </nav>

  {#if currentPath === homePath}
    <HomePage onOpenHealth={() => navigate(healthPagePath)} />
  {:else if currentPath === healthPagePath}
    <HealthPage />
  {:else if currentPath === userRootPath}
    <UserSearchPage onOpenUser={openUserPage} />
  {:else if currentUserUuid}
    <UserPage uuid={currentUserUuid} />
  {:else}
    <NotFoundPage path={currentPath} onGoHome={() => navigate(homePath)} />
  {/if}
</main>

<style>
  .page {
    min-height: 100vh;
    padding: 24px;
    background: linear-gradient(160deg, #f5f7fb, #dfe9f6);
    display: grid;
    place-items: center;
    gap: 16px;
  }

  .nav {
    width: min(760px, 100%);
    display: flex;
    gap: 10px;
  }

  .nav a {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 110px;
    padding: 8px 12px;
    border-radius: 10px;
    text-decoration: none;
    color: #1f2a44;
    background: #edf2fb;
    border: 1px solid #d0d9e8;
    font-weight: 600;
  }

  .nav a.active {
    color: #ffffff;
    background: #1e4db7;
    border-color: #1e4db7;
  }

  @media (max-width: 640px) {
    .nav {
      flex-wrap: wrap;
    }
  }

  @media (max-width: 430px) {
    .page {
      padding: 12px;
      gap: 12px;
    }

    .nav {
      width: 100%;
      gap: 8px;
    }

    .nav a {
      min-width: 0;
      flex: 1;
      font-size: 0.9rem;
      padding: 8px 10px;
    }
  }
</style>
