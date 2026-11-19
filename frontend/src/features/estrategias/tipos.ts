import { z } from 'zod'

export type Estrategia = {
  id: string; entidad: { id: string; nombre: string }; nombre: string; objetivo: string; poblacion: string
  fechaInicio: string; fechaFin: string; estado: 'EDITABLE' | 'BLOQUEADA'; editable: boolean; estudiantesVinculados: number; creadoEn: string
}
export type Entidad = { id: string; nombre: string; municipio: string }
export type EstudianteVinculado = { id: string; nombre: string; sede: string; municipio: string; vinculadoEn: string }

export const TIPOS_DOC = ['RC', 'TI', 'CC', 'CE', 'PPT', 'NUIP'] as const

// RQ_006: mismas reglas que FluentValidation en la API
export const esquemaEstrategia = z.object({
  entidadId: z.string().min(1, 'Selecciona una entidad.'),
  nombre: z.string().trim().min(1, 'El nombre es obligatorio.').max(150, 'El nombre admite máximo 150 caracteres.'),
  objetivo: z.string().trim().min(1, 'El objetivo es obligatorio.').max(2000, 'El objetivo admite máximo 2.000 caracteres.'),
  poblacion: z.string().trim().min(1, 'La población es obligatoria.').max(300, 'La población admite máximo 300 caracteres.'),
  fechaInicio: z.string().min(1, 'Indica la fecha de inicio.'),
  fechaFin: z.string().min(1, 'Indica la fecha de fin.'),
}).refine((d) => !d.fechaInicio || !d.fechaFin || d.fechaFin > d.fechaInicio, { path: ['fechaFin'], message: 'La fecha de fin debe ser posterior a la de inicio.' })
export type DatosEstrategia = z.infer<typeof esquemaEstrategia>
