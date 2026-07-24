import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { tanstackRouter } from '@tanstack/router-plugin/vite'
import { fileURLToPath, URL } from 'node:url'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    // Must precede the React plugin — it generates src/routeTree.gen.ts from the src/routes/ tree,
    // and React's transform needs to see the generated file.
    tanstackRouter({ target: 'react', autoCodeSplitting: true }),
    react(),
    tailwindcss(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    proxy: {
      // Same-origin in dev (UI_PLAN.md §5): the SPA always calls /api/*, and this proxy forwards to
      // the local API, stripping the /api prefix since the API currently serves its routes at root
      // (/clients, not /api/clients). Keeping the prefix in ONE place (here + VITE_API_BASE_URL)
      // means the eventual CloudFront /api/* → App Runner mapping (DEPLOY_PLAN.md §2) is the only
      // other place that has to agree, not every fetch call. Target is the API's committed
      // launchSettings HTTP port.
      '/api': {
        target: 'http://localhost:53534',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
})
