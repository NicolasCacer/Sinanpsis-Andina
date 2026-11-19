import { apiRaw } from '../app/api'
import { mesActual, type HitoDetalle, type PlanMes } from '../features/acompanamiento/tipos'
import { db } from './db'

export async function guardarPlan(mes: string, data: PlanMes, etag: string | null) { await db.planes.put({ mes, data, etag, guardadoEn: Date.now() }) }
export const leerPlan = (mes: string) => db.planes.get(mes)

/** Reconstruye el detalle de un hito desde el plan guardado (para registrar sin conexión). */
export async function buscarHitoLocal(hitoId: string): Promise<HitoDetalle | null> {
  for (const p of await db.planes.toArray())
    for (const pl of p.data.planes) {
      const h = pl.hitos.find((x) => x.id === hitoId)
      if (h) return { id: h.id, mes: p.mes, numeroSemana: h.numeroSemana, semanaIso: h.semanaIso, fechaInicio: h.fechaInicio, fechaFin: h.fechaFin, estado: h.estado, version: h.version, temas: pl.temas, estrategia: pl.estrategia, docentes: pl.docentes }
    }
  return null
}

export async function refrescarPlan(mes: string) {
  const r = await apiRaw<PlanMes>(`/tutores/me/plan?mes=${mes}`)
  await guardarPlan(mes, r.data, r.etag)
  return r.data
}

const siguienteMes = (m: string) => { const [a, b] = m.split('-').map(Number); const d = new Date(a, b, 1); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}` }

/** Al iniciar sesión se precargan el mes actual y el siguiente. */
export async function precargarPlanes() {
  const m = mesActual()
  for (const mes of [m, siguienteMes(m)]) { try { await refrescarPlan(mes) } catch { /* sin red: se usa lo guardado */ } }
}
