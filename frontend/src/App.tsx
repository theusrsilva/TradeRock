import { BrowserRouter, Route, Routes } from 'react-router'
import { Layout } from './components/Layout'
import BuilderPage from './pages/builder/BuilderPage'
import HistoryPage from './pages/HistoryPage'
import HomePage from './pages/HomePage'
import VaultPage from './pages/vault/VaultPage'
import SettingsPage from './pages/SettingsPage'
import SetupPage from './pages/setup/SetupPage'
import TradePage from './pages/trade/TradePage'
import { useTheme } from './theme'
import AboutPage from './pages/AboutPage'
import LoginPage from './pages/LoginPage'
import { AuthProvider, useIsRemote } from './lib/auth'
import { useTranslation } from 'react-i18next'
import { Card } from './components/ui'
import type { ReactNode } from 'react'

export default function App() {
  useTheme() // aplica o tema salvo em qualquer página
  return (
    <AuthProvider fallback={(reload) => <LoginPage onLogin={reload} />}>
      <BrowserRouter>
        <Layout>
          <Routes>
            <Route path="/" element={<HomePage />} />
            <Route path="/trocar" element={<TradePage />} />
            <Route path="/pokemon" element={<VaultPage />} />
            <Route path="/criar" element={<BuilderPage />} />
            <Route path="/criar/:id" element={<BuilderPage />} />
            <Route path="/historico" element={<HistoryPage />} />
            <Route path="/configuracoes" element={<SettingsPage />} />
            <Route path="/configurar" element={<LocalOnly><SetupPage /></LocalOnly>} />
            <Route path="/sobre" element={<AboutPage />} />
            <Route path="*" element={<HomePage />} />
          </Routes>
        </Layout>
      </BrowserRouter>
    </AuthProvider>
  )
}

/** Telas que mexem no PC (assistente: chaves, placa, firmware) não abrem pela rede. */
function LocalOnly({ children }: { children: ReactNode }) {
  const { t } = useTranslation()
  if (useIsRemote()) return <Card>{t('server.localOnly')}</Card>
  return <>{children}</>
}
