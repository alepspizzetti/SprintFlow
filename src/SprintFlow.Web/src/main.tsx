import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import Aplicacao from './App'
import './styles.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Aplicacao />
  </StrictMode>,
)
