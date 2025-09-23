import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { '@': path.resolve(__dirname, './src') }
  },
  server: {
    port: 5173,
    proxy: {
      // Proxy dev vers l’API locale hors Docker
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true,
        rewrite: p => p.replace(/^\/api/, '/api')
      }
    }
  }
})
