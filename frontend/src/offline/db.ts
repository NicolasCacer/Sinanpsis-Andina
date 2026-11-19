import Dexie, { type Table } from 'dexie'
import type { PlanMes } from '../features/acompanamiento/tipos'

export type Cifrado = { iv: Uint8Array<ArrayBuffer>; data: ArrayBuffer }
export type EstadoRegistro = 'Pendiente' | 'Sincronizado' | 'En conflicto' | 'Rechazado'

export interface PlanLocal { mes: string; data: PlanMes; etag: string | null; guardadoEn: number }
export interface RegistroOutbox {
  idempotencyKey: string; hitoId: string; mes: string; numeroSemana: number; estrategia: string
  versionBase: number; estado: EstadoRegistro; intentos: number; creadoEn: number; capturadoEn: string
  payload: Cifrado // { asistencia, actividades, avances } cifrado con AES-GCM
  nFotos: number; resolucion?: 'SOBRESCRIBIR'; motivo?: string; sincronizadoEn?: number
}
export interface EvidenciaLocal { id: string; idempotencyKey: string; cifrado: Cifrado }
export interface RegistroServidor {
  actividades: string; avances: string; origen: string; registradoPor: string | null; registradoEn: string
  asistencia: { docenteId: string; nombres: string; asistio: boolean }[]
}
export interface ConflictoLocal { idempotencyKey: string; hitoId: string; mes: string; numeroSemana: number; servidor: RegistroServidor; versionServidor: number; creadoEn: number }
export interface Meta { clave: string; valor: unknown }

class SsesDb extends Dexie {
  planes!: Table<PlanLocal, string>
  outbox!: Table<RegistroOutbox, string>
  evidenciasLocales!: Table<EvidenciaLocal, string>
  conflictos!: Table<ConflictoLocal, string>
  meta!: Table<Meta, string>
  constructor() {
    super('sses-local')
    this.version(1).stores({
      planes: 'mes',
      outbox: 'idempotencyKey, estado, creadoEn, hitoId',
      evidenciasLocales: 'id, idempotencyKey',
      conflictos: 'idempotencyKey, hitoId',
      meta: 'clave',
    })
  }
}
export const db = new SsesDb()
