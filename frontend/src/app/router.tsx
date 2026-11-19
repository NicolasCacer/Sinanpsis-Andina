import { lazy, Suspense } from 'react'
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router'
import { useAuth } from './auth'
import Landing from './Landing'
import Callback from './Callback'
import Shell from './Shell'
import Inicio from './Inicio'
import Auditoria from './Auditoria'
import Debug from './Debug'
import { config } from './config'

const Estrategias = lazy(() => import('../features/estrategias/EstrategiasRoutes'))
const Acompanamientos = lazy(() => import('../features/acompanamiento/AcompanamientoRoutes'))
const MiAcompanamiento = lazy(() => import('../features/acompanamiento/MiAcompanamientoRoutes'))
const ReportesLey = lazy(() => import('../features/reportes/ReportesLeyRoutes'))
const VerificarReporte = lazy(() => import('../features/reportes/VerificarReporte'))
const Analitica = lazy(() => import('../features/analitica/AnaliticaRoutes'))

function Cargando() { return <p className="p-6 text-slate-500">Cargando…</p> }

function Privado({ roles }: { roles?: string[] }) {
  const { estado, perfil } = useAuth()
  if (estado === 'cargando') return <Cargando />
  if (estado === 'anonimo' || !perfil) return <Navigate to="/" replace />
  if (roles && !roles.includes(perfil.rol)) return <Navigate to="/inicio" replace />
  return <Shell />
}
function Raiz() {
  const { estado } = useAuth()
  if (estado === 'cargando') return <Cargando />
  return estado === 'autenticado' ? <Navigate to="/inicio" replace /> : <Landing />
}

const router = createBrowserRouter([
  { path: '/', element: <Raiz /> },
  { path: '/auth/callback', element: <Callback /> },
  { element: <Privado />, children: [{ path: '/inicio', element: <Inicio /> }, { path: '/auditoria', element: <Auditoria /> }] },
  ...(config.demo ? [{ path: '/debug', element: <Debug /> }] : []),
  { element: <Privado roles={['GESTOR_ESTRATEGIAS']} />, children: [{ path: '/estrategias/*', element: <Suspense fallback={<Cargando />}><Estrategias /></Suspense> }] },
  { element: <Privado roles={['TUTOR_LIDER']} />, children: [{ path: '/acompanamientos/*', element: <Suspense fallback={<Cargando />}><Acompanamientos /></Suspense> }] },
  { element: <Privado roles={['TUTOR_DOCENTE']} />, children: [{ path: '/mi-acompanamiento/*', element: <Suspense fallback={<Cargando />}><MiAcompanamiento /></Suspense> }] },
  { element: <Privado roles={['ANALISTA_MEN']} />, children: [{ path: '/analitica/*', element: <Suspense fallback={<Cargando />}><Analitica /></Suspense> }] },
  { element: <Privado roles={['ENTE_CONTROL']} />, children: [{ path: '/reportes-ley/*', element: <Suspense fallback={<Cargando />}><ReportesLey /></Suspense> }] },
  { path: '/verificar/:codigo?', element: <Suspense fallback={<Cargando />}><VerificarReporte /></Suspense> },
  { path: '*', element: <Navigate to="/" replace /> },
])
export default function AppRouter() { return <RouterProvider router={router} /> }
