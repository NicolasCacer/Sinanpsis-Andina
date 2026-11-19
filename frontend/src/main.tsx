import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App'

// Cuando una versión nueva del service worker toma el control, se recarga una vez para mostrarla.
// La cola sin conexión vive en IndexedDB y no se pierde con la recarga (RQ_012).
if ('serviceWorker' in navigator && navigator.serviceWorker.controller) {
  let recargado = false
  navigator.serviceWorker.addEventListener('controllerchange', () => { if (!recargado) { recargado = true; window.location.reload() } })
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
