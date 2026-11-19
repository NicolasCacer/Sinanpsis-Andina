import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { useLiveQuery } from 'dexie-react-hooks'
import { v7 as uuidv7 } from 'uuid'
import { apiRaw, ApiError } from '../../app/api'
import { useOnline } from '../../app/useOnline'
import { descifrarJson } from '../../offline/crypto'
import { db } from '../../offline/db'
import { refrescarPlan } from '../../offline/planLocal'
import { sincronizarAhora } from '../../offline/sync'
import { queryClient } from '../../app/queryClient'
import { Aviso, Boton, Tarjeta } from '../../ui/kit'
import type { DatosSeguimiento } from './guardar'

const fecha = (s: string | null) => (s ? new Date(s).toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'short' }) : '—')

export default function Conflicto() {
  const { clave } = useParams(); const nav = useNavigate(); const online = useOnline()
  const [error, setError] = useState<string | null>(null); const [ocupado, setOcupado] = useState(false)
  const datos = useLiveQuery(async () => {
    const c = await db.conflictos.get(clave!); const r = await db.outbox.get(clave!)
    if (!c || !r) return null
    return { c, r, local: await descifrarJson<DatosSeguimiento>(r.payload) }
  }, [clave])

  if (datos === undefined) return <p className="text-slate-500">Cargando…</p>
  if (datos === null) return <Aviso tono="info">Este conflicto ya no está pendiente. <Link className="underline" to="/acompanamientos">Volver al plan</Link></Aviso>
  const { c, r, local } = datos
  const nombres = Object.fromEntries(c.servidor.asistencia.map((a) => [a.docenteId, a.nombres]))

  async function conservarServidor() {
    setOcupado(true); setError(null)
    try {
      await apiRaw('/sincronizacion/resoluciones', { method: 'POST', body: { hitoId: c.hitoId, decision: 'SERVIDOR' } })
      await db.transaction('rw', db.outbox, db.conflictos, db.evidenciasLocales, async () => {
        await db.outbox.update(r.idempotencyKey, { estado: 'Sincronizado', sincronizadoEn: Date.now(), motivo: 'Se conservó el registro del servidor' })
        await db.conflictos.delete(r.idempotencyKey); await db.evidenciasLocales.where('idempotencyKey').equals(r.idempotencyKey).delete()
      })
      try { await refrescarPlan(c.mes) } catch { /* luego */ }
      await queryClient.invalidateQueries({ queryKey: ['plan'] }); nav(`/acompanamientos?mes=${c.mes}`)
    } catch (e) { setError(e instanceof ApiError ? e.message : 'Necesitas conexión para resolver el conflicto.') } finally { setOcupado(false) }
  }

  async function conservarMio() {
    setOcupado(true); setError(null)
    try {
      // se reenvía como nuevo ítem: nueva clave de idempotencia y versionBase = versión del servidor
      const nueva = uuidv7()
      await db.transaction('rw', db.outbox, db.conflictos, db.evidenciasLocales, async () => {
        await db.outbox.delete(r.idempotencyKey)
        await db.outbox.add({ ...r, idempotencyKey: nueva, versionBase: c.versionServidor, resolucion: 'SOBRESCRIBIR', estado: 'Pendiente', intentos: 0, creadoEn: Date.now() })
        await db.evidenciasLocales.where('idempotencyKey').equals(r.idempotencyKey).modify({ idempotencyKey: nueva })
        await db.conflictos.delete(r.idempotencyKey)
      })
      await sincronizarAhora()
      nav(`/acompanamientos?mes=${c.mes}`)
    } catch { setError('No pudimos reenviar tu registro. Intenta de nuevo.') } finally { setOcupado(false) }
  }

  return (
    <div className="max-w-3xl">
      <Link to={`/acompanamientos?mes=${c.mes}`} className="text-sm text-andino-700 underline">← Plan mensual</Link>
      <h1 className="mt-2 text-xl font-bold text-andino-900">Conflicto en la semana {c.numeroSemana}</h1>
      <p className="text-sm text-slate-600 mt-1">Esta semana se registró en otro dispositivo mientras trabajabas sin conexión. Elige qué versión conservar.</p>
      {error && <Aviso tono="error" className="mt-3">{error}</Aviso>}
      <div className="mt-4 grid md:grid-cols-2 gap-4" data-testid="conflicto">
        <Tarjeta className="p-4" data-testid="version-local">
          <h2 className="font-semibold text-andino-900">En este dispositivo</h2>
          <p className="text-xs text-slate-500">Capturado el {fecha(r.capturadoEn)}</p>
          <Detalle actividades={local.actividades} avances={local.avances} asistencia={local.asistencia.map((a) => ({ nombre: nombres[a.docenteId] ?? 'Docente', asistio: a.asistio }))} fotos={r.nFotos} />
        </Tarjeta>
        <Tarjeta className="p-4" data-testid="version-servidor">
          <h2 className="font-semibold text-andino-900">En el servidor</h2>
          <p className="text-xs text-slate-500">Registrado por {c.servidor.registradoPor ?? '—'} el {fecha(c.servidor.registradoEn)}</p>
          <Detalle actividades={c.servidor.actividades} avances={c.servidor.avances} asistencia={c.servidor.asistencia.map((a) => ({ nombre: a.nombres, asistio: a.asistio }))} />
        </Tarjeta>
      </div>
      <div className="mt-4 flex gap-3 justify-end flex-wrap">
        <Boton variante="secundario" onClick={() => void conservarServidor()} disabled={ocupado || !online}>Conservar el del servidor</Boton>
        <Boton onClick={() => void conservarMio()} disabled={ocupado}>Conservar el mío</Boton>
      </div>
      {!online && <p className="mt-2 text-xs text-slate-500 text-right">Conservar el del servidor necesita conexión.</p>}
    </div>
  )
}

function Detalle({ actividades, avances, asistencia, fotos }: { actividades: string; avances: string; asistencia: { nombre: string; asistio: boolean }[]; fotos?: number }) {
  return (
    <dl className="mt-3 grid gap-2 text-sm">
      <div><dt className="text-slate-500 text-xs">Actividades</dt><dd className="whitespace-pre-wrap">{actividades}</dd></div>
      <div><dt className="text-slate-500 text-xs">Avances</dt><dd className="whitespace-pre-wrap">{avances}</dd></div>
      <div><dt className="text-slate-500 text-xs">Asistencia</dt><dd>{asistencia.map((a) => `${a.nombre}: ${a.asistio ? 'asistió' : 'no asistió'}`).join(' · ')}</dd></div>
      {fotos !== undefined && <div><dt className="text-slate-500 text-xs">Fotos</dt><dd>{fotos}</dd></div>}
    </dl>
  )
}
