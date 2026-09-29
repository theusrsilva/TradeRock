import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Em dev, o Vite serve o frontend e repassa /api ao backend (dotnet --no-window --port 5080).
// O proxy injeta o token fixo de desenvolvimento; o navegador nunca precisa dele.
const backend = process.env.TRADEROCK_BACKEND ?? 'http://127.0.0.1:5080'
const devToken = process.env.TRADEROCK_DEV_TOKEN ?? 'dev-token-traderock-0000000000000000'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    // Embutido no executável pelo TradeRock.App.csproj.
    outDir: '../app/TradeRock.App/wwwroot',
    emptyOutDir: true,
  },
  server: {
    host: process.env.VITE_HOST ?? '127.0.0.1',
    port: 5173,
    strictPort: true,
    // Os textos explicativos ficam em ../content (fora da pasta do frontend).
    fs: { allow: ['..'] },
    proxy: {
      '/api': {
        target: backend,
        // Mantém o Host do navegador (localhost/127.0.0.1), aceito pelo filtro de hosts do backend.
        changeOrigin: false,
        headers: { 'X-TradeRock-Token': devToken },
      },
    },
  },
})
