import { BrowserRouter, Route, Routes } from 'react-router'
import { Layout } from './components/Layout'
import HistoryPage from './pages/HistoryPage'
import HomePage from './pages/HomePage'
import PokemonPage from './pages/PokemonPage'
import SettingsPage from './pages/SettingsPage'
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
          <Route path="/pokemon" element={<PokemonPage />} />
          <Route path="/historico" element={<HistoryPage />} />
          <Route path="/configuracoes" element={<SettingsPage />} />
          <Route path="*" element={<HomePage />} />
        </Routes>
      </Layout>
    </BrowserRouter>
  )
}
