import { v7 as uuidv7 } from 'uuid'
import { useLiveQuery } from 'dexie-react-hooks'
import type { DatosSeguimiento } from '../features/acompanamiento/guardar'
import type { HitoDetalle } from '../features/acompanamiento/tipos'
import { cifrarBytes, cifrarJson } from './crypto'
import { db, type RegistroOutbox } from './db'

/** Guarda el seguimiento cifrado en el dispositivo con su clave de idempotencia (estado 'Pendiente'). */
export async function encolarSeguimiento(h: HitoDetalle, d: DatosSeguimiento, fotos: Blob[]): Promise<RegistroOutbox> {
  const key = uuidv7()
  const payload = await cifrarJson(d)
  const evs = await Promise.all(fotos.map(async (f) => ({ id: uuidv7(), idempotencyKey: key, cifrado: await cifrarBytes(await f.arrayBuffer()) })))
  const reg: RegistroOutbox = {
    idempotencyKey: key, hitoId: h.id, mes: h.mes, numeroSemana: h.numeroSemana, estrategia: h.estrategia.nombre, versionBase: h.version,
    estado: 'Pendiente', intentos: 0, creadoEn: Date.now(), capturadoEn: new Date().toISOString(), payload, nFotos: fotos.length,
  }
  await db.transaction('rw', db.outbox, db.evidenciasLocales, async () => { await db.outbox.add(reg); if (evs.length) await db.evidenciasLocales.bulkAdd(evs) })
  void registrarBackgroundSync()
  return reg
}

async function registrarBackgroundSync() {
  try {
    const reg = await navigator.serviceWorker?.ready
    const sync = (reg as unknown as { sync?: { register: (t: string) => Promise<void> } } | undefined)?.sync
    await sync?.register('sses-outbox') // disparador adicional (Chromium); el motor propio funciona en todos los navegadores (D05)
  } catch { /* no soportado */ }
}

export const contarPendientes = () => db.outbox.where('estado').equals('Pendiente').count()
export function usePendientes(): number { return useLiveQuery(() => contarPendientes(), [], 0) }
export function useEnConflicto(): number { return useLiveQuery(() => db.outbox.where('estado').equals('En conflicto').count(), [], 0) }
export function useOutbox(mes?: string): RegistroOutbox[] {
  return useLiveQuery(async () => (mes ? db.outbox.filter((r) => r.mes === mes).toArray() : db.outbox.toArray()), [mes], [])
}
