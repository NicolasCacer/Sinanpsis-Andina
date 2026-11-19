import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams, useLocation } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { apiRaw, ApiError } from '../../app/api'
import { getAccessToken } from '../../app/auth-manager'
import { config } from '../../app/config'
import { AreaTexto, Aviso, Boton, Campo, Insignia, Tarjeta, Vacio } from '../../ui/kit'
import { db } from '../../offline/db'
import { esErrorDeRed } from '../../offline/red'
import { guardarConfirmacion, useConfirmaciones } from '../../offline/confirmaciones'
import { mesActual, rangoSemana } from './tipos'

type EstadoDocente = 'PROGRAMADA' | 'REGISTRADA' | 'CONFIRMADO' | 'OBSERVADO'
type HitoDocente = {
  id: string; numeroSemana: number; semanaIso: string; fechaInicio: string; fechaFin: string; estado: EstadoDocente
  seguimiento: { actividades: string; avances: string; asistio: boolean | null; registradoEn: string | null; evidencias: { id: string; tipoMime: string }[] } | null
  miConfirmacion: { confirmado: boolean; observacion: string | null; enviadoEn: string | null } | null
}
type PlanDocente = { id: string; estrategia: string; modalidad: 'INDIVIDUAL' | 'GRUPAL'; temas: string; tutorLider: string; hitos: HitoDocente[] }
type MiMes = { mes: string; planes: PlanDocente[] }

const etiqueta: Record<EstadoDocente, [string, 'azul' | 'ambar' | 'verde' | 'rojo']> = {
  PROGRAMADA: ['Programada', 'azul'], REGISTRADA: ['Registrada', 'ambar'], CONFIRMADO: ['Confirmada por el docente', 'verde'], OBSERVADO: ['Observada', 'rojo'],
}

function useMiMes(mes: string) {
  return useQuery({
    queryKey: ['mi-acompanamiento', mes],
    queryFn: async (): Promise<MiMes & { desdeLocal?: boolean }> => {
      try { const r = await apiRaw<MiMes>(`/mi-acompanamiento?mes=${mes}`); await db.miAcomp.put({ mes, data: r.data, guardadoEn: Date.now() }); return r.data }
      catch (e) { if (esErrorDeRed(e)) { const l = await db.miAcomp.get(mes); if (l) return { ...(l.data as MiMes), desdeLocal: true } } throw e }
    },
  })
}

export function MiAcompanamiento() {
  const [sp, setSp] = useSearchParams(); const mes = sp.get('mes') ?? mesActual()
  const q = useMiMes(mes); const locales = useConfirmaciones(mes)
  const st = useLocation().state as { enviada?: number; pendiente?: number } | null
  const pend = new Map(locales.filter((l) => l.estado === 'Pendiente').map((l) => [l.hitoId, l]))
  return (
    <div className="max-w-3xl">
      <div className="flex items-end justify-between gap-3 flex-wrap">
        <div><h1 className="text-2xl font-bold text-andino-900">Mi acompañamiento</h1><p className="text-sm text-slate-500">El plan que programó tu Tutor Líder y sus encuentros</p></div>
        <label className="text-sm"><span className="block text-slate-600 mb-1">Mes</span>
          <input type="month" value={mes} onChange={(e) => e.target.value && setSp({ mes: e.target.value })} className="rounded-lg border border-slate-300 px-3 py-2 bg-white" data-testid="selector-mes" /></label>
      </div>
      {st?.enviada && <Aviso tono="ok" className="mt-4" data-testid="aviso-enviada">Semana {st.enviada} enviada.</Aviso>}
      {st?.pendiente && <Aviso tono="alerta" className="mt-4" data-testid="aviso-pendiente">Semana {st.pendiente} guardada en este dispositivo: Pendiente. Se enviará cuando vuelva la señal.</Aviso>}
      {q.data?.desdeLocal && <Aviso tono="info" className="mt-4">Sin conexión: mostrando lo último que se guardó en este dispositivo.</Aviso>}
      {q.isLoading && <p className="mt-6 text-slate-500">Cargando…</p>}
      {q.isError && <Aviso tono="error" className="mt-4">No pudimos cargar tu acompañamiento. <button className="underline" onClick={() => void q.refetch()}>Reintentar</button></Aviso>}
      {q.data && q.data.planes.length === 0 && <div className="mt-6"><Vacio titulo="No tienes acompañamientos programados este mes" detalle="Cuando tu Tutor Líder programe un plan, lo verás aquí." /></div>}
      <div className="mt-4 grid gap-4">
        {q.data?.planes.map((p) => (
          <Tarjeta key={p.id} className="p-4" data-testid="plan-docente">
            <div className="flex items-start justify-between gap-2 flex-wrap">
              <div><h2 className="font-semibold text-andino-900">{p.estrategia}</h2><p className="text-xs text-slate-500">Tutor Líder: {p.tutorLider}</p></div>
              <Insignia tono="gris">{p.modalidad === 'GRUPAL' ? 'Grupal' : 'Individual'}</Insignia>
            </div>
            <p className="mt-2 text-sm text-slate-700"><span className="font-medium">Temas:</span> {p.temas}</p>
            <ul className="mt-3 divide-y divide-slate-100">
              {p.hitos.map((h) => {
                const local = pend.get(h.id); const [txt, tono] = etiqueta[h.estado]
                return (
                  <li key={h.id} className="py-3 flex items-center justify-between gap-3 flex-wrap" data-testid={`semana-${h.numeroSemana}`}>
                    <div><p className="text-sm font-medium">Semana {h.numeroSemana} <span className="text-slate-500 font-normal">· {rangoSemana(h.fechaInicio, h.fechaFin)}</span></p></div>
                    <div className="flex items-center gap-2">
                      {local ? <Insignia tono="ambar" data-testid="estado-semana">Pendiente</Insignia> : <Insignia tono={tono} data-testid="estado-semana">{txt}</Insignia>}
                      {h.estado !== 'PROGRAMADA' && <Link to={`semana/${h.id}?mes=${mes}`} className="text-sm text-andino-700 underline min-h-11 inline-flex items-center">{h.miConfirmacion || local ? 'Ver' : 'Abrir'}</Link>}
                    </div>
                  </li>
                )
              })}
            </ul>
          </Tarjeta>
        ))}
      </div>
    </div>
  )
}

function Foto({ id }: { id: string }) {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    let u: string | null = null; let vivo = true
    void (async () => {
      try {
        const t = await getAccessToken(); const r = await fetch(`${config.apiUrl}/mi-acompanamiento/evidencias/${id}`, { headers: t ? { Authorization: `Bearer ${t}` } : {} })
        if (r.ok && vivo) { u = URL.createObjectURL(await r.blob()); setUrl(u) }
      } catch { /* sin conexión: no se muestra la miniatura */ }
    })()
    return () => { vivo = false; if (u) URL.revokeObjectURL(u) }
  }, [id])
  return url ? <img src={url} alt="Evidencia del encuentro" className="w-24 h-24 object-cover rounded-lg border" /> : <span className="w-24 h-24 rounded-lg border bg-slate-100 text-[10px] text-slate-500 grid place-items-center">Sin vista previa</span>
}

export function Semana() {
  const { hitoId } = useParams(); const [sp] = useSearchParams(); const mes = sp.get('mes') ?? mesActual(); const nav = useNavigate(); const qc = useQueryClient()
  const q = useMiMes(mes); const locales = useConfirmaciones(mes)
  const [ok, setOk] = useState(false); const [obs, setObs] = useState('')
  const [enviando, setEnviando] = useState(false); const [error, setError] = useState<string | null>(null)
  const plan = q.data?.planes.find((p) => p.hitos.some((h) => h.id === hitoId)); const h = plan?.hitos.find((x) => x.id === hitoId)
  if (q.isLoading) return <p className="text-slate-500">Cargando…</p>
  if (!plan || !h) return <Aviso tono="error">No encontramos esa semana. <Link className="underline" to={`/mi-acompanamiento?mes=${mes}`}>Volver</Link></Aviso>
  const local = locales.find((l) => l.hitoId === h.id && l.estado === 'Pendiente')
  const yaEnviada = !!h.miConfirmacion || !!local

  async function enviar() {
    if (!plan || !h) return
    if (!ok && !obs.trim()) { setError('Marca "Confirmar acompañamiento" o escribe una observación.'); return }
    setEnviando(true); setError(null)
    try {
      const modo = await guardarConfirmacion({ hitoId: h.id, mes, numeroSemana: h.numeroSemana, estrategia: plan.estrategia }, { confirmado: ok, observacion: obs })
      await qc.invalidateQueries({ queryKey: ['mi-acompanamiento'] })
      nav(`/mi-acompanamiento?mes=${mes}`, { state: modo === 'en-linea' ? { enviada: h.numeroSemana } : { pendiente: h.numeroSemana } })
    } catch (x) {
      setError(x instanceof ApiError ? (x.code === 'YA_CONFIRMADA' ? 'Ya enviaste tu confirmación de esta semana.' : x.message) : 'No pudimos enviar. Intenta de nuevo.')
    } finally { setEnviando(false) }
  }

  const s = h.seguimiento
  return (
    <div className="max-w-xl">
      <Link to={`/mi-acompanamiento?mes=${mes}`} className="text-sm text-andino-700 underline">← Mi acompañamiento</Link>
      <h1 className="mt-2 text-xl font-bold text-andino-900">Semana {h.numeroSemana}</h1>
      <p className="text-sm text-slate-500">{rangoSemana(h.fechaInicio, h.fechaFin)} · {plan.estrategia} · Tutor Líder: {plan.tutorLider}</p>
      <Tarjeta className="mt-4 p-4 grid gap-4">
        {s && <>
          <div><p className="text-sm font-medium text-slate-700">Actividades</p><p className="text-sm whitespace-pre-wrap" data-testid="actividades">{s.actividades}</p></div>
          <div><p className="text-sm font-medium text-slate-700">Avances</p><p className="text-sm whitespace-pre-wrap" data-testid="avances">{s.avances}</p></div>
          {s.asistio != null && <p className="text-sm">Asistencia registrada: <strong>{s.asistio ? 'Asististe' : 'No asististe'}</strong></p>}
          {s.evidencias.length > 0 && <ul className="flex gap-2 flex-wrap">{s.evidencias.map((e) => <li key={e.id}><Foto id={e.id} /></li>)}</ul>}
        </>}
        {yaEnviada ? (
          <Aviso tono="ok" data-testid="confirmacion-enviada">
            {local ? 'Tu confirmación está Pendiente en este dispositivo y se enviará cuando vuelva la señal.' : h.miConfirmacion?.confirmado ? 'Confirmaste este acompañamiento.' : 'Enviaste una observación.'}
            {h.miConfirmacion?.observacion && <span className="block mt-1">Observación: {h.miConfirmacion.observacion}</span>}
          </Aviso>
        ) : (
          <>
            {error && <Aviso tono="error">{error}</Aviso>}
            <label className="flex items-center gap-3 min-h-12 cursor-pointer"><input type="checkbox" className="w-6 h-6" checked={ok} onChange={(e) => setOk(e.target.checked)} /><span className="text-sm">Confirmar acompañamiento</span></label>
            <Campo etiqueta="Observación" htmlFor="observacion" ayuda="Obligatoria si el encuentro no se realizó como quedó registrado. Máximo 500 caracteres."><AreaTexto id="observacion" maxLength={500} value={obs} onChange={(e) => setObs(e.target.value)} /></Campo>
            <div className="flex justify-end"><Boton onClick={() => void enviar()} disabled={enviando}>Enviar</Boton></div>
          </>
        )}
      </Tarjeta>
    </div>
  )
}
