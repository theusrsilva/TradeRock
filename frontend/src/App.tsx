import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import { Layout } from './components/Layout'
import BuilderPage from './pages/builder/BuilderPage'
import ActivitiesPage from './pages/ActivitiesPage'
import HistoryPage from './pages/HistoryPage'
import HomePage from './pages/HomePage'
import VaultPage from './pages/vault/VaultPage'
import SettingsPage from './pages/SettingsPage'
import TradePage from './pages/trade/TradePage'
import { useTheme } from './theme'
import AboutPage from './pages/AboutPage'
import LoginPage from './pages/LoginPage'
import { AuthProvider } from './lib/auth'
import { ConnectionProvider } from './lib/connection'
import { ToastProvider } from './components/ui'

export default function App() {
  useTheme() // aplica o tema salvo em qualquer página
  return (
    <AuthProvider fallback={(reload) => <LoginPage onLogin={reload} />}>
      <ConnectionProvider>
        <ToastProvider>
          <BrowserRouter>
            <Layout>
              <Routes>
                <Route path="/" element={<HomePage />} />
                <Route path="/trocar" element={<TradePage />} />
                <Route path="/pokemon" element={<VaultPage />} />
                <Route path="/criar" element={<BuilderPage />} />
                <Route path="/criar/:id" element={<BuilderPage />} />
                <Route path="/historico" element={<HistoryPage />} />
                <Route path="/atividades" element={<ActivitiesPage />} />
                <Route path="/configuracoes" element={<SettingsPage />} />
                {/* O antigo assistente de 7 etapas virou a lista de Configurações → Conexão. */}
                <Route path="/configurar" element={<Navigate to="/configuracoes?aba=conexao" replace />} />
                <Route path="/sobre" element={<AboutPage />} />
                <Route path="*" element={<HomePage />} />
              </Routes>
            </Layout>
          </BrowserRouter>
        </ToastProvider>
      </ConnectionProvider>
    </AuthProvider>
  )
}
