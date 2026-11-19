import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { userManager } from './auth-manager'
import { api, ApiError } from './api'

export type ItemMenu = { clave: string; etiqueta: string; ruta: string; habilitado: boolean; nota?: string | null }
export type Perfil = { nombre: string; rol: string; rolEtiqueta: string; ultimoIngreso: string | null; menu: ItemMenu[] }

type Estado = 'cargando' | 'anonimo' | 'autenticado'
type Ctx = {
  estado: Estado
  perfil: Perfil | null
  sinSesionLocal: boolean // perfil restaurado sin red/sin token (modo sin conexión)
  ingresar: () => void
  salir: () => void
  completarLogin: () => Promise<void>
  aviso: string | null
}
const AuthCtx = createContext<Ctx | null>(null)
const PERFIL_KEY = 'sses.perfil'

function leerPerfilLocal(): Perfil | null {
  try { const s = localStorage.getItem(PERFIL_KEY); return s ? (JSON.parse(s) as Perfil) : null } catch { return null }
}
function guardarPerfilLocal(p: Perfil | null) {
  try { if (p) localStorage.setItem(PERFIL_KEY, JSON.stringify(p)); else localStorage.removeItem(PERFIL_KEY) } catch { /* sin almacenamiento */ }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [estado, setEstado] = useState<Estado>('cargando')
  const [perfil, setPerfil] = useState<Perfil | null>(null)
  const [sinSesionLocal, setSinSesionLocal] = useState(false)
  const [aviso, setAviso] = useState<string | null>(null)

  useEffect(() => {
    let vivo = true
    ;(async () => {
      const u = await userManager.getUser()
      if (u && !u.expired) {
        try {
          const p = await api<Perfil>('/me')
          if (!vivo) return
          guardarPerfilLocal(p); setPerfil(p); setEstado('autenticado'); return
        } catch (e) {
          if (e instanceof ApiError && e.status === 403) { setAviso(e.message) }
          else if (!(e instanceof ApiError)) {
            // sin red: se usa el perfil guardado
            const loc = leerPerfilLocal()
            if (loc && vivo) { setPerfil(loc); setSinSesionLocal(true); setEstado('autenticado'); return }
          }
        }
      } else if (!navigator.onLine) {
        const loc = leerPerfilLocal()
        if (loc && vivo) { setPerfil(loc); setSinSesionLocal(true); setEstado('autenticado'); return }
      }
      if (vivo) setEstado('anonimo')
    })()
    return () => { vivo = false }
  }, [])

  // Al volver la red con un perfil local sin token: renovar sesión (SSO) sin borrar nada local
  useEffect(() => {
    if (!sinSesionLocal) return
    const h = async () => {
      const u = await userManager.getUser()
      if (u && !u.expired) { setSinSesionLocal(false); return }
      try { const r = await userManager.signinSilent(); if (r) setSinSesionLocal(false) }
      catch { /* se pedirá ingresar de nuevo desde el aviso; la cola local se conserva */ }
    }
    window.addEventListener('online', h)
    return () => window.removeEventListener('online', h)
  }, [sinSesionLocal])

  const ingresar = useCallback(() => { void userManager.signinRedirect() }, [])
  const salir = useCallback(() => {
    void (async () => {
      const { contarPendientes } = await import('../offline/outbox')
      const n = await contarPendientes().catch(() => 0)
      if (n > 0 && !window.confirm(`Tienes ${n} registro(s) pendientes de sincronizar en este dispositivo. Si sales ahora se perderán. ¿Salir de todos modos?`)) return
      guardarPerfilLocal(null)
      await import('../offline/crypto').then((m) => m.olvidarClave()).catch(() => undefined)
      void userManager.signoutRedirect()
    })()
  }, [])
  const completarLogin = useCallback(async () => {
    await userManager.signinRedirectCallback()
    const p = await api<Perfil>('/sesion/inicio', { method: 'POST' }) // registra LOGIN (RQ_005)
    guardarPerfilLocal(p); setPerfil(p); setSinSesionLocal(false); setEstado('autenticado')
  }, [])

  const v = useMemo(() => ({ estado, perfil, sinSesionLocal, ingresar, salir, completarLogin, aviso }), [estado, perfil, sinSesionLocal, ingresar, salir, completarLogin, aviso])
  return <AuthCtx.Provider value={v}>{children}</AuthCtx.Provider>
}

// El hook comparte archivo con el proveedor a propósito: separarlos tocaría a todos los importadores.
// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const c = useContext(AuthCtx)
  if (!c) throw new Error('useAuth fuera de AuthProvider')
  return c
}
