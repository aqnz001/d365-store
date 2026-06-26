import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The SPA calls the BFF under /api. In dev, proxy to the BFF (override with VITE_BFF_URL).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env.VITE_BFF_URL ?? 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },
})
