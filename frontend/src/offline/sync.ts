import { apiRaw, ApiError } from '../app/api'
import { getAccessToken } from '../app/auth-manager'
import { queryClient } from '../app/queryClient'
import { descifrarBytes, descifrarJson } from './crypto'
import { db, type RegistroOutbox, type RegistroServidor } from './db'
import { esErrorDeRed } from './red'
import { refrescarPlan } from './planLocal'
import type { DatosSeguimiento } from '../features/acompanamiento/guardar'

type ResultadoItem = { idempotencyKey: string; estado: 'APLICADO' | 'DUPLICADO' | 'CONFLICTO' | 'RECHAZADO'; versionServidor?: number; registroServidor?: RegistroServidor; motivo?: string }
export type EstadoSync = 'ocupado' | 'sin-pendientes' | 'sin-sesion' | 'sin-red' | 'ok'

const BACKOFF = [2000, 4000, 8000, 16000]
let corriendo = false
let intentoBackoff = 0
let temporizador: ReturnType<typeof setTimeout> | null = null

function base64(buf: ArrayBuffer) { let s = ''; const b = new Uint8Array(buf); for (let i = 0; i < b.length; i += 0x8000) s += String.fromCharCode(...b.subarray(i, i + 0x8000)); return btoa(s) }

async function armarItem(r: RegistroOutbox) {
  const d = await descifrarJson<DatosSeguimiento>(r.payload)
  const evs = await db.evidenciasLocales.where('idempotencyKey').equals(r.idempotencyKey).toArray()
  const evidencias = await Promise.all(evs.map(async (e) => ({ nombre: 'foto', base64: base64(await descifrarBytes(e.cifrado)) })))
  return { idempotencyKey: r.idempotencyKey, hitoId: r.hitoId, versionBase: r.versionBase, capturadoEn: r.capturadoEn, asistencia: d.asistencia, actividades: d.actividades, avances: d.avances, evidencias, resolucion: r.resolucion ?? null }
}

/** Envía la cola en orden de creación, en lotes de ≤ 50. Idempotente: reenviar nunca duplica (RQ_012). */
export async function sincronizarAhora(): Promise<EstadoSync> {
  if (corriendo) return 'ocupado'
  corriendo = true
  try {
    if (!(await getAccessToken())) return 'sin-sesion' // se pide ingresar de nuevo SIN borrar la cola
    const meses = new Set<string>(); let alguno = false
    for (;;) {
      const lote = (await db.outbox.where('estado').equals('Pendiente').sortBy('creadoEn')).slice(0, 50)
      if (lote.length === 0) break
      alguno = true
      let resultados: ResultadoItem[]
      try {
        const items = await Promise.all(lote.map(armarItem))
        const r = await apiRaw<{ resultados: ResultadoItem[] }>('/sincronizacion/seguimientos', { method: 'POST', body: { items }, timeoutMs: 120000 })
        resultados = r.data.resultados
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return 'sin-sesion'
        if (esErrorDeRed(e)) {
          await db.outbox.bulkUpdate(lote.map((l) => ({ key: l.idempotencyKey, changes: { intentos: l.intentos + 1 } })))
          programarReintento(); return 'sin-red'
        }
        throw e
      }
      for (const res of resultados) {
        const reg = lote.find((l) => l.idempotencyKey === res.idempotencyKey)!; meses.add(reg.mes)
        if (res.estado === 'APLICADO' || res.estado === 'DUPLICADO') {
          await db.transaction('rw', db.outbox, db.evidenciasLocales, db.conflictos, async () => {
            await db.outbox.update(reg.idempotencyKey, { estado: 'Sincronizado', sincronizadoEn: Date.now(), motivo: undefined })
            await db.evidenciasLocales.where('idempotencyKey').equals(reg.idempotencyKey).delete()
            await db.conflictos.delete(reg.idempotencyKey)
          })
        } else if (res.estado === 'CONFLICTO') {
          await db.transaction('rw', db.outbox, db.conflictos, async () => {
            await db.outbox.update(reg.idempotencyKey, { estado: 'En conflicto' })
            await db.conflictos.put({ idempotencyKey: reg.idempotencyKey, hitoId: reg.hitoId, mes: reg.mes, numeroSemana: reg.numeroSemana, servidor: res.registroServidor!, versionServidor: res.versionServidor!, creadoEn: Date.now() })
          })
        } else await db.outbox.update(reg.idempotencyKey, { estado: 'Rechazado', motivo: res.motivo })
      }
    }
    intentoBackoff = 0
    for (const m of meses) { try { await refrescarPlan(m) } catch { /* el plan se actualizará luego */ } }
    await queryClient.invalidateQueries({ queryKey: ['plan'] })
    return alguno ? 'ok' : 'sin-pendientes'
  } finally { corriendo = false }
}

function programarReintento() {
  if (temporizador) return
  const espera = BACKOFF[Math.min(intentoBackoff, BACKOFF.length - 1)]; intentoBackoff++
  temporizador = setTimeout(() => { temporizador = null; void sincronizarAhora() }, espera)
}

export function cancelarReintentos() { if (temporizador) clearTimeout(temporizador); temporizador = null; intentoBackoff = 0 }

let iniciado = false
/** Disparadores: evento 'online', volver a la pestaña, inicio de la app y mensaje del Service Worker (Background Sync). */
export function iniciarSincronizacion() {
  if (iniciado) return; iniciado = true
  const disparar = () => { if (navigator.onLine) void sincronizarAhora() }
  window.addEventListener('online', disparar)
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') disparar() })
  navigator.serviceWorker?.addEventListener('message', (e) => { if ((e.data as { type?: string })?.type === 'SYNC') disparar() })
  disparar()
}
