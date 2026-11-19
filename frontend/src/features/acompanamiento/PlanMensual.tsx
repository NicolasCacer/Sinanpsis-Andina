import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiRaw, ApiError } from '../../app/api'
import { useOnline } from '../../app/useOnline'
import { AreaTexto, Aviso, Boton, Campo, Insignia, Tarjeta } from '../../ui/kit'
import { mesActual, rangoSemana, type DocentesPorEstrategia, type Hito, type PlanMes } from './tipos'

export function EstadoHito({ h }: { h: Hito }) {
  return h.estado === 'PROGRAMADA'
    ? <Insignia tono="azul" data-testid="estado-hito">Programada</Insignia>
    : <Insignia tono="verde" data-testid="estado-hito">Registrada</Insignia>
}

export default function PlanMensual() {
  const [sp, setSp] = useSearchParams()
  const mes = sp.get('mes') ?? mesActual()
  const plan = useQuery({ queryKey: ['plan', mes], queryFn: async () => (await apiRaw<PlanMes>(`/tutores/me/plan?mes=${mes}`)).data })
  const p = plan.data
  return (
    <div className="max-w-3xl mx-auto md:mx-0">
      <div className="flex items-end justify-between gap-3 flex-wrap">
        <div><h1 className="text-2xl font-bold text-andino-900">Plan mensual</h1><p className="text-sm text-slate-500">Acompañamientos del mes y su seguimiento semanal</p></div>
        <label className="text-sm">
          <span className="block text-slate-600 mb-1">Mes</span>
          <input type="month" value={mes} onChange={(e) => e.target.value && setSp({ mes: e.target.value })} className="rounded-lg border border-slate-300 px-3 py-2 bg-white" data-testid="selector-mes" />
        </label>
      </div>

      {plan.isLoading && <p className="mt-6 text-slate-500">Cargando plan…</p>}
      {plan.isError && <Aviso tono="error" className="mt-4">No pudimos cargar el plan. <button className="underline" onClick={() => void plan.refetch()}>Reintentar</button></Aviso>}

      {p && (
        <>
          <Tarjeta className="mt-4 p-4" >
            <div className="flex items-baseline justify-between"><h2 className="font-semibold text-slate-800">Cumplimiento del mes</h2>
              <span className="text-2xl font-bold text-andino-900" data-testid="cumplimiento-porcentaje">{Math.round(p.cumplimiento.porcentaje)} %</span></div>
            <div className="mt-2 h-3 rounded-full bg-slate-100 overflow-hidden" role="progressbar" aria-valuenow={Math.round(p.cumplimiento.porcentaje)} aria-valuemin={0} aria-valuemax={100}>
              <div className="h-full bg-cumple transition-all" style={{ width: `${p.cumplimiento.porcentaje}%` }} />
            </div>
            <p className="mt-1.5 text-xs text-slate-500" data-testid="cumplimiento-detalle">{p.cumplimiento.registradas} de {p.cumplimiento.programadas} semanas registradas</p>
          </Tarjeta>

          <h2 className="mt-6 mb-2 font-semibold text-slate-800">Calendario del mes</h2>
          {p.planes.length === 0 && <Tarjeta className="p-6 text-center text-slate-500">Aún no hay acompañamientos programados para este mes. Usa el formulario de abajo.</Tarjeta>}
          <div className="grid gap-3" data-testid="calendario">
            {p.planes.map((pl) => (
              <Tarjeta key={pl.id} className="p-4">
                <div className="flex items-start justify-between gap-2 flex-wrap">
                  <div className="min-w-0"><p className="font-medium">{pl.estrategia.nombre}</p>
                    <p className="text-xs text-slate-500">{pl.modalidad === 'GRUPAL' ? 'Grupal' : 'Individual'} · {pl.docentes.map((d) => d.nombres).join(', ')}</p></div>
                </div>
                <p className="mt-1 text-xs text-slate-500">Temas: {pl.temas}</p>
                <ul className="mt-3 grid gap-2 sm:grid-cols-2">
                  {pl.hitos.map((h) => (
                    <li key={h.id} data-testid="hito" data-estado={h.estado} className={`rounded-lg border p-3 ${h.estado === 'PROGRAMADA' ? 'border-slate-200 bg-slate-50' : 'border-green-200 bg-cumple-50'}`}>
                      <div className="flex items-center justify-between gap-2"><span className="font-medium text-sm">Semana {h.numeroSemana}</span><EstadoHito h={h} /></div>
                      <p className="text-xs text-slate-500">{rangoSemana(h.fechaInicio, h.fechaFin)} · {h.semanaIso}</p>
                      {h.estado === 'PROGRAMADA'
                        ? <Link to={`registrar/${h.id}`} className="mt-2 inline-block"><Boton className="!py-1.5">Registrar seguimiento</Boton></Link>
                        : <p className="mt-2 text-xs text-green-800">{h.seguimiento?.asistencia.filter((a) => a.asistio).length ?? 0} de {pl.docentes.length} asistieron</p>}
                    </li>
                  ))}
                </ul>
              </Tarjeta>
            ))}
          </div>
          <Programar mes={mes} semanas={p.semanas} />
        </>
      )}
    </div>
  )
}

function Programar({ mes, semanas }: { mes: string; semanas: PlanMes['semanas'] }) {
  const qc = useQueryClient(); const online = useOnline()
  const grupos = useQuery({ queryKey: ['docentes'], queryFn: () => api<DocentesPorEstrategia>('/tutores/me/docentes') })
  const [estrategia, setEstrategia] = useState<string | null>(null)
  const [docs, setDocs] = useState<string[]>([])
  const [modalidad, setModalidad] = useState<'INDIVIDUAL' | 'GRUPAL'>('GRUPAL')
  const [temas, setTemas] = useState('')
  const [sem, setSem] = useState<number[]>([])
  const [msg, setMsg] = useState<{ tono: 'ok' | 'error'; texto: string } | null>(null)
  const [enviando, setEnviando] = useState(false)

  const alternar = <T,>(l: T[], v: T) => (l.includes(v) ? l.filter((x) => x !== v) : [...l, v])
  const marcar = (est: string, id: string) => {
    if (estrategia !== est) { setEstrategia(est); setDocs([id]) } else setDocs((d) => alternar(d, id))
  }
  const motivo = docs.length === 0 ? 'Marca al menos un docente.' : modalidad === 'GRUPAL' && docs.length < 2 ? 'Un plan grupal necesita al menos 2 docentes.'
    : modalidad === 'GRUPAL' && docs.length > 30 ? 'Máximo 30 docentes.' : !temas.trim() ? 'Escribe los temas.' : sem.length === 0 ? 'Selecciona al menos una semana.' : !online ? 'Necesitas conexión para programar.' : null

  async function programar() {
    setMsg(null); setEnviando(true)
    try {
      await apiRaw('/acompanamientos', { method: 'POST', body: { mes, modalidad, estrategiaId: estrategia, docenteIds: docs, temas, semanas: sem } })
      await qc.invalidateQueries({ queryKey: ['plan'] })
      setMsg({ tono: 'ok', texto: 'Plan programado. Ya aparece en el calendario del mes.' }); setDocs([]); setTemas(''); setSem([])
    } catch (x) {
      setMsg({ tono: 'error', texto: x instanceof ApiError ? x.message : 'No hay conexión con el servidor.' })
    } finally { setEnviando(false) }
  }

  return (
    <>
      <h2 className="mt-8 mb-2 font-semibold text-slate-800">Programar acompañamientos</h2>
      <Tarjeta className="p-4 grid gap-5" data-testid="form-programar">
        <fieldset>
          <legend className="text-sm font-medium text-slate-700 mb-2">Tus tutores docentes, por estrategia</legend>
          {grupos.isLoading && <p className="text-sm text-slate-500">Cargando…</p>}
          {grupos.data?.estrategias.length === 0 && <p className="text-sm text-slate-500">Aún no tienes docentes asignados.</p>}
          <div className="grid gap-3">
            {grupos.data?.estrategias.map((g) => (
              <div key={g.estrategia.id} className={`rounded-lg border p-3 ${estrategia === g.estrategia.id ? 'border-andino-700 bg-andino-50/50' : 'border-slate-200'}`}>
                <p className="font-medium text-sm">{g.estrategia.nombre}</p><p className="text-xs text-slate-500 mb-2">{g.estrategia.entidad}</p>
                <div className="grid sm:grid-cols-2 gap-1.5">
                  {g.docentes.map((d) => (
                    <label key={d.id} className="flex items-center gap-2 text-sm py-1.5 min-h-10">
                      <input type="checkbox" className="w-5 h-5" checked={estrategia === g.estrategia.id && docs.includes(d.id)} onChange={() => marcar(g.estrategia.id, d.id)} /> {d.nombres}
                    </label>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </fieldset>

        <fieldset>
          <legend className="text-sm font-medium text-slate-700 mb-2">Modalidad</legend>
          <div className="flex gap-4">
            {(['INDIVIDUAL', 'GRUPAL'] as const).map((m) => (
              <label key={m} className="flex items-center gap-2 text-sm min-h-10"><input type="radio" name="modalidad" className="w-5 h-5" checked={modalidad === m} onChange={() => setModalidad(m)} /> {m === 'GRUPAL' ? 'Grupal' : 'Individual'}</label>
            ))}
          </div>
        </fieldset>

        <Campo etiqueta="Temas" htmlFor="temas" ayuda="Máximo 2.000 caracteres."><AreaTexto id="temas" rows={3} maxLength={2000} value={temas} onChange={(e) => setTemas(e.target.value)} /></Campo>

        <fieldset>
          <legend className="text-sm font-medium text-slate-700 mb-2">Semanas del mes</legend>
          <div className="flex gap-2 flex-wrap">
            {semanas.map((s) => (
              <label key={s.numero} className={`rounded-lg border px-3 py-2 text-sm cursor-pointer min-h-11 ${sem.includes(s.numero) ? 'border-andino-700 bg-andino-50 font-medium' : 'border-slate-300'}`}>
                <input type="checkbox" className="sr-only" checked={sem.includes(s.numero)} onChange={() => setSem((l) => alternar(l, s.numero))} aria-label={`Semana ${s.numero}`} />
                Semana {s.numero}<span className="block text-xs text-slate-500">{rangoSemana(s.fechaInicio, s.fechaFin)}</span>
              </label>
            ))}
          </div>
        </fieldset>

        {msg && <Aviso tono={msg.tono}>{msg.texto}</Aviso>}
        <div className="flex items-center gap-3 justify-end flex-wrap">
          {motivo && <span className="text-xs text-slate-500">{motivo}</span>}
          <Boton onClick={() => void programar()} disabled={!!motivo || enviando}>Programar</Boton>
        </div>
      </Tarjeta>
    </>
  )
}
