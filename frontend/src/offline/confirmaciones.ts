import { v7 as uuidv7 } from 'uuid'
import { useLiveQuery } from 'dexie-react-hooks'
import { apiRaw, ApiError } from '../app/api'
import { queryClient } from '../app/queryClient'
import { cifrarJson, descifrarJson } from './crypto'
import { db, type ConfirmacionLocal } from './db'
import { esErrorDeRed } from './red'

export type DatosConfirmacion = { confirmado: boolean; observacion: string }
type Contexto = { hitoId: string; mes: string; numeroSemana: number; estrategia: string }

/** RQ_016: guarda la confirmación cifrada en el dispositivo con su Idempotency-Key (estado 'Pendiente'). */
export async function encolarConfirmacion(c: Contexto, d: DatosConfirmacion): Promise<ConfirmacionLocal> {
  const reg: ConfirmacionLocal = { idempotencyKey: uuidv7(), ...c, estado: 'Pendiente', intentos: 0, creadoEn: Date.now(), capturadoEn: new Date().toISOString(), payload: await cifrarJson(d) }
  await db.confirmaciones.add(reg)
  try { const r = await navigator.serviceWorker?.ready; await (r as unknown as { sync?: { register: (t: string) => Promise<void> } } | undefined)?.sync?.register('sses-outbox') } catch { /* sin Background Sync */ }
  return reg
}

/** Envía la confirmación en línea; 201 nueva, 200 si la clave ya estaba aplicada. */
export async function enviarConfirmacion(c: Contexto & { idempotencyKey: string; capturadoEn: string; origen: 'EN_LINEA' | 'SIN_CONEXION' }, d: DatosConfirmacion, timeoutMs = 8000) {
  return apiRaw<{ estado: string }>(`/seguimientos/${c.hitoId}/confirmaciones`, {
    method: 'POST', body: { confirmado: d.confirmado, observacion: d.observacion.trim() || null, capturadoEn: c.capturadoEn },
    headers: { 'Idempotency-Key': c.idempotencyKey, 'X-Origen': c.origen }, timeoutMs,
  })
}

/** Con red guarda en línea; sin red o ante fallo de red lo deja 'Pendiente' en IndexedDB. Los demás errores se propagan. */
export async function guardarConfirmacion(c: Contexto, d: DatosConfirmacion): Promise<'en-linea' | 'pendiente'> {
  if (navigator.onLine) {
    try { await enviarConfirmacion({ ...c, idempotencyKey: uuidv7(), capturadoEn: new Date().toISOString(), origen: 'EN_LINEA' }, d); return 'en-linea' }
    catch (e) { if (!esErrorDeRed(e)) throw e }
  }
  await encolarConfirmacion(c, d); return 'pendiente'
}

/** Reenvía la cola en orden con la MISMA clave: el servidor no duplica. Devuelve 'sin-red' si se cortó la conexión. */
export async function sincronizarConfirmaciones(): Promise<'sin-red' | 'ok' | null> {
  const cola = await db.confirmaciones.where('estado').equals('Pendiente').sortBy('creadoEn')
  if (cola.length === 0) return null
  for (const r of cola) {
    try {
      await enviarConfirmacion({ ...r, origen: 'SIN_CONEXION' }, await descifrarJson<DatosConfirmacion>(r.payload), 15000)
      await db.confirmaciones.update(r.idempotencyKey, { estado: 'Sincronizado', sincronizadoEn: Date.now(), motivo: undefined })
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) return null
      if (esErrorDeRed(e)) { await db.confirmaciones.update(r.idempotencyKey, { intentos: r.intentos + 1 }); return 'sin-red' }
      // 409 (ya confirmada desde otro dispositivo), 404, 422: no se reintenta
      await db.confirmaciones.update(r.idempotencyKey, { estado: e instanceof ApiError && e.code === 'YA_CONFIRMADA' ? 'Sincronizado' : 'Rechazado', motivo: e instanceof ApiError ? e.message : 'Error' })
    }
  }
  await queryClient.invalidateQueries({ queryKey: ['mi-acompanamiento'] })
  return 'ok'
}

export function useConfirmaciones(mes: string): ConfirmacionLocal[] {
  return useLiveQuery(() => db.confirmaciones.filter((r) => r.mes === mes).toArray(), [mes], [])
}
