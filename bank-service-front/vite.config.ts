import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Backend ASP.NET Core (Development) default HTTPS port
      '/api': {
        target: 'https://localhost:7254',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
