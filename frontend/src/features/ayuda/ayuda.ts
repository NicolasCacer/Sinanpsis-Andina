import { api } from '../../app/api'

export type Incidente = {
  id: string; numero: string; tipo: 'BLOQUEANTE' | 'FUNCIONAL' | 'CONSULTA'; severidad: 'ALTA' | 'MEDIA' | 'BAJA'
  estado: 'ABIERTO' | 'EN_ATENCION' | 'RESUELTO' | 'CERRADO'; descripcion: string; pantalla: string | null
  creadoEn: string; atendidoEn: string | null; resueltoEn: string | null
}

export const TIPOS: { valor: Incidente['tipo']; etiqueta: string }[] = [
  { valor: 'BLOQUEANTE', etiqueta: 'No puedo continuar (bloqueante)' },
  { valor: 'FUNCIONAL', etiqueta: 'Algo no funciona como esperaba' },
  { valor: 'CONSULTA', etiqueta: 'Tengo una duda' },
]
export const ESTADOS: Record<Incidente['estado'], string> = { ABIERTO: 'Abierto', EN_ATENCION: 'En atención', RESUELTO: 'Resuelto', CERRADO: 'Cerrado' }
export const SEVERIDADES: Record<Incidente['severidad'], string> = { ALTA: 'Alta', MEDIA: 'Media', BAJA: 'Baja' }

const hex = (n: number) => Array.from(crypto.getRandomValues(new Uint8Array(n)), (b) => b.toString(16).padStart(2, '0')).join('')

/** RQ_020 / CU_013: el aplicativo informa la pantalla, el navegador y el identificador de correlación (W3C traceparent) por el usuario. */
export function contextoTecnico(pantalla: string) {
  return { pantalla, navegador: navigator.userAgent.slice(0, 500), traceparent: `00-${hex(16)}-${hex(8)}-01` }
}

export function reportarIncidente(tipo: Incidente['tipo'], descripcion: string, pantalla: string) {
  const ctx = contextoTecnico(pantalla)
  return api<Incidente>('/incidentes', { method: 'POST', body: { tipo, descripcion, ...ctx }, headers: { traceparent: ctx.traceparent } })
}
