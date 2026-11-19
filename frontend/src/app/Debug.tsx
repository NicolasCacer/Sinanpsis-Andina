import { useState } from 'react'
import { db } from '../offline/db'
import { Aviso, Boton, Tarjeta } from '../ui/kit'

/** Solo build de demo (VITE_DEMO=1): herramientas para ensayar la demostración. */
export default function Debug() {
  const [msg, setMsg] = useState<string | null>(null)
  async function borrar() {
    await db.delete(); localStorage.clear(); sessionStorage.clear()
    const regs = await navigator.serviceWorker?.getRegistrations(); await Promise.all((regs ?? []).map((r) => r.unregister()))
    for (const k of await caches.keys()) await caches.delete(k)
    setMsg('Datos locales, caché y service worker borrados. Recarga la página.')
  }
  return (
    <div className="max-w-xl p-6">
      <h1 className="text-xl font-bold text-andino-900">Herramientas de demo</h1>
      <Tarjeta className="mt-4 p-4 grid gap-3">
        <p className="text-sm text-slate-600">Borra IndexedDB (plan, cola, claves), caché de la PWA y sesión de este navegador.</p>
        <Boton variante="peligro" onClick={() => void borrar()} data-testid="borrar-local">Borrar datos locales</Boton>
        {msg && <Aviso tono="ok">{msg}</Aviso>}
      </Tarjeta>
    </div>
  )
}
