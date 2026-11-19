import { Link } from 'react-router'
import { useAuth } from './auth'

export default function Inicio() {
  const { perfil } = useAuth()
  if (!perfil) return null
  return (
    <div className="max-w-2xl">
      <h1 className="text-2xl font-bold text-andino-900">Hola, {perfil.nombre.split(' ')[0]}</h1>
      <p className="mt-2 text-slate-600">Cada consulta o cambio que hagas en el SSES queda registrado a tu nombre.</p>
      <div className="mt-6 grid gap-3 sm:grid-cols-2">
        {perfil.menu.filter((m) => m.habilitado).map((m) => (
          <Link key={m.clave} to={m.ruta} className="rounded-xl bg-white border border-slate-200 p-5 shadow-sm hover:border-andino-700">
            <span className="font-semibold text-andino-900">{m.etiqueta}</span>
          </Link>
        ))}
        <Link to="/auditoria" className="rounded-xl bg-white border border-slate-200 p-5 shadow-sm hover:border-andino-700">
          <span className="font-semibold text-andino-900">Bitácora de actividad</span>
          <span className="block text-xs text-slate-500 mt-1">Qué se registró a tu nombre y verificación de la cadena</span>
        </Link>
      </div>
    </div>
  )
}
