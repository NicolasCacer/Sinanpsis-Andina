import { apiRaw } from '../../app/api'

export type DatosSeguimiento = { asistencia: { docenteId: string; asistio: boolean }[]; actividades: string; avances: string }
export type ResultadoGuardado = { estado: 'REGISTRADA'; cumplimientoMes: { registradas: number; programadas: number; porcentaje: number } }

/** Sube las fotos (POST /evidencias) y registra el seguimiento (PUT con If-Match = versión del hito). */
export async function guardarEnLinea(hitoId: string, version: number, d: DatosSeguimiento, fotos: Blob[]): Promise<ResultadoGuardado> {
  const ids: string[] = []
  for (const f of fotos) {
    const form = new FormData(); form.append('archivo', f, f.type === 'image/webp' ? 'foto.webp' : 'foto.jpg')
    const r = await apiRaw<{ evidenciaId: string }>('/evidencias', { method: 'POST', form, timeoutMs: 20000 })
    ids.push(r.data.evidenciaId)
  }
  const r = await apiRaw<ResultadoGuardado>(`/hitos/${hitoId}/seguimiento`, { method: 'PUT', body: { ...d, evidenciaIds: ids }, headers: { 'If-Match': `W/"${version}"` }, timeoutMs: 20000 })
  return r.data
}
