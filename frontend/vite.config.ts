import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'

// https://vite.dev/config/
export default defineConfig({
  plugins: [svelte()],
  server: {
    proxy: {
      '/mojang-api': {
        target: 'https://api.mojang.com',
        changeOrigin: true,
        secure: true,
        rewrite: (path) => path.replace(/^\/mojang-api/, '')
      }
    }
  }
})
