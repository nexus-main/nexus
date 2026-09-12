import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'

const nexusEndpoint = process.env.NEXUS_ENDPOINT
const nexusToken = process.env.NEXUS_TOKEN

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  define: {
    __NEXUS_PROXY_ENABLED__: JSON.stringify(Boolean(nexusEndpoint && nexusToken)),
    __NEXUS_ENDPOINT__: JSON.stringify(nexusEndpoint?.replace(/\/$/, '') ?? 'https://nexus.hlb.iwes.fraunhofer.de'),
  },
  server: nexusEndpoint && nexusToken ? {
    proxy: {
      '/api': {
        target: nexusEndpoint,
        changeOrigin: true,
        secure: true,
        configure(proxy) {
          proxy.on('proxyReq', (proxyReq) => {
            proxyReq.setHeader('Authorization', `Bearer ${nexusToken}`)
          })
        },
      },
    },
  } : undefined,
  resolve: {
    alias: {
      '@nexus-api': fileURLToPath(new URL('../clients/typescript/index.ts', import.meta.url)),
      'apache-arrow': fileURLToPath(new URL('./node_modules/apache-arrow/Arrow.dom.mjs', import.meta.url)),
    },
  },
})
