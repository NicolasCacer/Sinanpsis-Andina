/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      strategies: 'injectManifest',
      srcDir: 'src',
      filename: 'sw.ts',
      registerType: 'autoUpdate',
      injectRegister: 'auto',
      manifest: {
        name: 'SSES — Sinapsis Andina',
        short_name: 'SSES',
        description: 'Sistema de Seguimiento de Actividades Extracurriculares',
        theme_color: '#0b3a63',
        background_color: '#f4f7fa',
        display: 'standalone',
        start_url: '/',
        icons: [
          { src: 'icon.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'any' },
          { src: 'icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: 'icon-512.png', sizes: '512x512', type: 'image/png' },
        ],
      },
      injectManifest: { globPatterns: ['**/*.{js,css,html,svg,png,webp,woff2}'] },
      devOptions: { enabled: false },
    }),
  ],
  server: { port: 5173, host: true, proxy: { '/api': 'http://localhost:5080', '/health': 'http://localhost:5080' } },
  test: { environment: 'jsdom', globals: true, include: ['src/**/*.test.ts?(x)'] },
})
