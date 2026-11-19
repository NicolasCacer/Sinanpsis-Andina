import { NavLink, Outlet } from 'react-router'
import { useAuth } from './auth'
import { useOnline } from './useOnline'
import { usePendientes } from '../offline/usePendientes'

const fmt = (iso: string | null) => (iso ? new Date(iso).toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'short' }) : 'primer ingreso')

export default function Shell() {
  const { perfil, salir, sinSesionLocal, ingresar } = useAuth()
  const online = useOnline()
  const pendientes = usePendientes()
  if (!perfil) return null
  return (
    <div className="min-h-screen flex flex-col md:flex-row">
      <aside className="hidden md:flex md:w-60 flex-col bg-andino-900 text-white p-4 gap-1">
        <div className="flex items-center gap-2 mb-6"><img src="/icon.svg" alt="" className="w-8 h-8" /><span className="font-bold text-lg">SSES</span></div>
        <nav aria-label="Menú principal" className="flex flex-col gap-1">
          {perfil.menu.map((m) => <ItemMenu key={m.clave} m={m} />)}
        </nav>
        <p className="mt-auto text-xs text-andino-50/60">Sinapsis Andina S.A.S.</p>
      </aside>

      <div className="flex-1 flex flex-col min-w-0">
        <header className="bg-white border-b border-slate-200 px-4 py-2 flex items-center gap-3 flex-wrap">
          <div className="min-w-0 flex-1">
            <p className="font-semibold truncate" data-testid="encabezado-nombre">{perfil.nombre}</p>
            <p className="text-xs text-slate-500 truncate"><span data-testid="encabezado-rol">{perfil.rolEtiqueta}</span> · Último ingreso: <span data-testid="encabezado-ultimo">{fmt(perfil.ultimoIngreso)}</span></p>
          </div>
          <span data-testid="indicador-conexion" className={`inline-flex items-center gap-1.5 text-xs rounded-full px-2.5 py-1 ${online ? 'bg-cumple-50 text-cumple' : 'bg-slate-200 text-slate-700'}`}>
            <span className={`w-2 h-2 rounded-full ${online ? 'bg-cumple' : 'bg-slate-500'}`} />{online ? 'En línea' : 'Sin conexión'}
          </span>
          <span data-testid="indicador-pendientes" className={`text-xs rounded-full px-2.5 py-1 ${pendientes > 0 ? 'bg-amber-100 text-amber-800' : 'bg-slate-100 text-slate-500'}`}>
            {pendientes} pendientes
          </span>
          <button onClick={salir} className="text-sm text-andino-700 underline">Salir</button>
        </header>

        {!online && (
          <div role="status" data-testid="aviso-sin-conexion" className="bg-amber-50 border-b border-amber-200 text-amber-900 text-sm px-4 py-2">
            Sin conexión: tus registros se guardarán en este dispositivo
          </div>
        )}
        {sinSesionLocal && online && (
          <div role="alert" className="bg-sky-50 border-b border-sky-200 text-sky-900 text-sm px-4 py-2 flex gap-3 items-center">
            Tu sesión necesita renovarse para sincronizar. Tus registros locales se conservan.
            <button onClick={ingresar} className="underline font-medium">Ingresar de nuevo</button>
          </div>
        )}

        <main className="flex-1 p-4 md:p-6 pb-24 md:pb-6"><Outlet /></main>

        <nav aria-label="Menú móvil" className="md:hidden fixed bottom-0 inset-x-0 bg-andino-900 text-white flex justify-around p-1 z-10">
          {perfil.menu.map((m) => <ItemMenu key={m.clave} m={m} movil />)}
        </nav>
      </div>
    </div>
  )
}

function ItemMenu({ m, movil }: { m: { etiqueta: string; ruta: string; habilitado: boolean; nota?: string | null }; movil?: boolean }) {
  const base = movil ? 'flex-1 text-center text-xs px-2 py-2 rounded' : 'px-3 py-2 rounded text-sm flex items-center justify-between'
  if (!m.habilitado)
    return <span aria-disabled="true" title={m.nota ?? ''} className={`${base} opacity-50 cursor-not-allowed`}>{m.etiqueta}{!movil && m.nota && <small className="text-[10px] uppercase opacity-80">{m.nota}</small>}</span>
  return <NavLink to={m.ruta} className={({ isActive }) => `${base} ${isActive ? 'bg-white/15 font-semibold' : 'hover:bg-white/10'}`}>{m.etiqueta}</NavLink>
}
