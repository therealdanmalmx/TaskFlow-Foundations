/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

const apiTarget = process.env.TASKFLOW_API_URL ?? 'http://localhost:8080'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  test: {
    environment: 'jsdom',
    setupFiles: './src/test-setup.ts',
  },
  server: {
    // During local development the Vite dev server proxies API calls to the
    // .NET API. In production there is no proxy: the API serves the built SPA
    // from wwwroot, so everything is same-origin. Application routes use /api,
    // while authentication and health checks use /auth and /health.
    proxy: {
      // Keep the React /auth page local, proxy only its API endpoints.
      '/auth/': apiTarget,
      '/api': apiTarget,
      '/health': apiTarget,
    },
  },
})
