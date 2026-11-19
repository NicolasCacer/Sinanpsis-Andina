import { Link } from 'react-router'
import { useAuth } from './auth'
import { IconoBitacora, IconoFlecha, IconoInicio, IconoMenu } from '../ui/iconos'

const DESCRIPCION: Record<string, string> = {
  estrategias: 'Crea estrategias para tus entidades, vincula estudiantes y controla el bloqueo de edición.',
  acompanamientos: 'Programa el plan mensual y registra el seguimiento semanal, con o sin señal.',
  ayuda: '¿Algo no funciona? Reporta el inconveniente y sigue la atención de tus casos.',
  pilotaje: 'Sesiones del pilotaje, asistencia y encuestas de percepción.',
  'mi-acompanamiento': 'Consulta y confirma el acompañamiento que recibiste.',
  analitica: 'Tablero de cumplimiento y regularidad de los tutores por región y mes.',
  'reportes-ley': 'Genera reportes de ley en PDF con huella SHA-256 verificable.',
}

export default function Inicio() {
  const { perfil } = useAuth()
  if (!perfil) return null
  const hora = new Date().getHours()
  const saludo = hora < 12 ? 'Buenos días' : hora < 19 ? 'Buenas tardes' : 'Buenas noches'
  return (
    <div className="max-w-5xl">
      <section className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-andino-900 via-andino-800 to-andino-700 text-white p-6 md:p-8 shadow-suave">
        <svg aria-hidden="true" viewBox="0 0 400 200" className="absolute -right-10 -bottom-10 w-96 opacity-15">
          <g stroke="#3ddc97" strokeWidth="6" fill="none" strokeLinecap="round"><path d="M40 180 120 60 200 160 280 30 370 150" /><circle cx="120" cy="60" r="12" /><circle cx="200" cy="160" r="12" /><circle cx="280" cy="30" r="12" /></g>
        </svg>
        <p className="text-sm text-andino-100/90">{saludo},</p>
        <h1 className="mt-1 text-2xl md:text-3xl font-bold tracking-tight">{perfil.nombre}</h1>
        <p className="mt-3 inline-flex items-center rounded-full bg-white/10 ring-1 ring-white/20 px-3 py-1 text-xs font-medium">{perfil.rolEtiqueta}</p>
        <p className="mt-4 max-w-xl text-sm text-andino-50/90 flex items-start gap-2">
          <IconoBitacora width={18} height={18} className="shrink-0 mt-0.5 text-cumple-400" />
          Cada consulta o cambio que hagas en el SSES queda registrado a tu nombre.
        </p>
      </section>

      <h2 className="mt-8 mb-3 text-sm font-semibold uppercase tracking-wider text-slate-500">Tus módulos</h2>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {perfil.menu.map((m) => {
          const Icono = IconoMenu[m.clave] ?? IconoInicio
          const cuerpo = (
            <>
              <span className={`grid place-items-center w-11 h-11 rounded-xl ${m.habilitado ? 'bg-andino-50 text-andino-700 group-hover:bg-andino-900 group-hover:text-white' : 'bg-slate-100 text-slate-400'} transition-colors`}><Icono width={22} height={22} /></span>
              <span className="mt-4 flex items-center gap-2 font-semibold text-slate-900">
                {m.etiqueta}
                {!m.habilitado && m.nota && <small className="rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-medium uppercase tracking-wide text-slate-500">{m.nota}</small>}
              </span>
              <span className="mt-1 block text-sm text-slate-500">{DESCRIPCION[m.clave] ?? ''}</span>
              {m.habilitado && <span className="mt-4 inline-flex items-center gap-1 text-sm font-semibold text-andino-700">Abrir <IconoFlecha width={16} height={16} className="transition-transform group-hover:translate-x-1" /></span>}
            </>
          )
          return m.habilitado
            ? <Link key={m.clave} to={m.ruta} className="group flex flex-col rounded-xl bg-white border border-slate-200/80 p-5 shadow-suave hover:border-andino-600 hover:-translate-y-0.5 transition">{cuerpo}</Link>
            : <div key={m.clave} aria-disabled="true" className="flex flex-col rounded-xl bg-white/60 border border-dashed border-slate-300 p-5">{cuerpo}</div>
        })}
        <Link to="/auditoria" className="group flex flex-col rounded-xl bg-white border border-slate-200/80 p-5 shadow-suave hover:border-andino-600 hover:-translate-y-0.5 transition">
          <span className="grid place-items-center w-11 h-11 rounded-xl bg-cumple-50 text-cumple group-hover:bg-cumple group-hover:text-white transition-colors"><IconoBitacora width={22} height={22} /></span>
          <span className="mt-4 font-semibold text-slate-900">Bitácora de actividad</span>
          <span className="mt-1 block text-sm text-slate-500">Lo que se registró a tu nombre y la verificación de la cadena de integridad.</span>
          <span className="mt-4 inline-flex items-center gap-1 text-sm font-semibold text-andino-700">Abrir <IconoFlecha width={16} height={16} className="transition-transform group-hover:translate-x-1" /></span>
        </Link>
      </div>
    </div>
  )
}
