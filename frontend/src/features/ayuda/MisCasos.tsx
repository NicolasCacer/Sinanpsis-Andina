import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../app/api'
import { Aviso, Boton, Insignia, Tarjeta, Vacio } from '../../ui/kit'
import { IconoAyuda } from '../../ui/iconos'
import { FormularioAyuda } from './BotonAyuda'
import { ESTADOS, SEVERIDADES, TIPOS, type Incidente } from './ayuda'

const fmt = (iso: string) => new Date(iso).toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'short' })
const tonoEstado = { ABIERTO: 'ambar', EN_ATENCION: 'azul', RESUELTO: 'verde', CERRADO: 'gris' } as const
const tonoSev = { ALTA: 'rojo', MEDIA: 'ambar', BAJA: 'gris' } as const

/** 'Mis casos' (HU_011 / RQ_020): el usuario ve cuándo soporte atiende y resuelve su caso. */
export default function MisCasos() {
  const q = useQuery({ queryKey: ['mis-casos'], queryFn: () => api<{ items: Incidente[] }>('/incidentes/mios') })
  const [reportando, setReportando] = useState(false)
  const reportar = <Boton onClick={() => setReportando(true)}><IconoAyuda width={18} height={18} />Reportar un inconveniente</Boton>
  return (
    <div className="max-w-3xl">
      <div className="flex items-start justify-between gap-3 flex-wrap">
        <div>
          <h1 className="text-2xl font-bold text-andino-900">Mis casos</h1>
          <p className="text-sm text-slate-500">Inconvenientes que reportaste con el botón 'Ayuda' y su estado de atención.</p>
        </div>
        {(q.data?.items.length ?? 0) > 0 && reportar}
      </div>
      {reportando && <FormularioAyuda cerrar={() => setReportando(false)} />}
      {q.isError && <Aviso tono="error" className="mt-4">No pudimos cargar tus casos.</Aviso>}
      {q.data && q.data.items.length === 0 && <Tarjeta className="mt-6"><Vacio titulo="Aún no has reportado casos" detalle="Si algo no funciona, pulsa 'Ayuda' en la parte superior de cualquier pantalla o repórtalo desde aquí." accion={reportar} /></Tarjeta>}
      <ul className="mt-6 space-y-3" data-testid="lista-casos">
        {q.data?.items.map((c) => (
          <li key={c.id}>
            <Tarjeta className="p-4">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-mono font-semibold text-andino-900">{c.numero}</span>
                <Insignia tono={tonoEstado[c.estado]}>{ESTADOS[c.estado]}</Insignia>
                <Insignia tono={tonoSev[c.severidad]}>Severidad {SEVERIDADES[c.severidad]}</Insignia>
                <span className="ml-auto text-xs text-slate-500">{fmt(c.creadoEn)}</span>
              </div>
              <p className="mt-2 text-sm text-slate-700">{c.descripcion}</p>
              <p className="mt-1 text-xs text-slate-500">
                {TIPOS.find((t) => t.valor === c.tipo)?.etiqueta}{c.pantalla && <> · Pantalla: {c.pantalla}</>}
                {c.atendidoEn && <> · Atendido: {fmt(c.atendidoEn)}</>}{c.resueltoEn && <> · Resuelto: {fmt(c.resueltoEn)}</>}
              </p>
            </Tarjeta>
          </li>
        ))}
      </ul>
    </div>
  )
}
