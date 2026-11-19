import { apiRaw } from '../../app/api'

export type DatosSeguimiento = { asistencia: { docenteId: string; asistio: boolean }[]; actividades: string; avances: string }
export type ResultadoGuardado = { estado: 'REGISTRADA'; cumplimientoMes: { registradas: number; programadas: number; porcentaje: number } }

/** Sube las fotos (POST /evidencias) y registra el seguimiento (PUT con If-Match = versión del hito). */
export async function guardarEnLinea(hitoId: string, version: number, d: DatosSeguimiento, fotos: Blob[], timeoutMs = 20000): Promise<ResultadoGuardado> {
  const ids: string[] = []
  for (const f of fotos) {
    const form = new FormData(); form.append('archivo', f, f.type === 'image/webp' ? 'foto.webp' : 'foto.jpg')
    const r = await apiRaw<{ evidenciaId: string }>('/evidencias', { method: 'POST', form, timeoutMs })
    ids.push(r.data.evidenciaId)
  }
  const r = await apiRaw<ResultadoGuardado>(`/hitos/${hitoId}/seguimiento`, { method: 'PUT', body: { ...d, evidenciaIds: ids }, headers: { 'If-Match': `W/"${version}"` }, timeoutMs })
  return r.data
}

import type { HitoDetalle } from './tipos'
import { encolarSeguimiento } from '../../offline/outbox'
import { esErrorDeRed } from '../../offline/red'

export type Guardado = { modo: 'en-linea'; res: ResultadoGuardado } | { modo: 'pendiente' }

/**
 * Con red (respuesta < 8 s) guarda en línea; sin red o ante fallo de red/timeout lo cifra en IndexedDB
 * con clave de idempotencia (estado 'Pendiente'). Los demás errores (412, 409, 400) se propagan.
 */
export async function guardarSeguimiento(h: HitoDetalle, d: DatosSeguimiento, fotos: Blob[]): Promise<Guardado> {
  if (navigator.onLine) {
    try { return { modo: 'en-linea', res: await guardarEnLinea(h.id, h.version, d, fotos, 8000) } }
    catch (e) { if (!esErrorDeRed(e)) throw e }
  }
  await encolarSeguimiento(h, d, fotos)
  return { modo: 'pendiente' }
}
