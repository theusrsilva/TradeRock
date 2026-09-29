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

export default function App() {
  useTheme() // aplica o tema salvo em qualquer página
  return (
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
          <Route path="/configurar" element={<SetupPage />} />
          <Route path="*" element={<HomePage />} />
        </Routes>
      </Layout>
    </BrowserRouter>
  )
}
