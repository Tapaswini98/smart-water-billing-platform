import { fileURLToPath, URL } from 'node:url'

import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API base path is ALWAYS the relative '/api' — never an absolute URL from an
// env var. Vite inlines import.meta.env at build time, so a baked-in host would be
// wrong in every environment except the one the image was built for. In development
// the proxy below forwards to the API; in production nginx does the same job. One
// image, correct everywhere.
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env['VITE_DEV_API_TARGET'] ?? 'http://localhost:8080',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
})
