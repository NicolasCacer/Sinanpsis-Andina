import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiRaw, ApiError } from '../../app/api'
import { AreaTexto, Aviso, Boton, Campo, Entrada, Seleccion, Tarjeta } from '../../ui/kit'
import { esquemaEstrategia, type DatosEstrategia, type Entidad, type Estrategia } from './tipos'

const hoy = () => new Date().toISOString().slice(0, 10)
const enMeses = (m: number) => { const d = new Date(); d.setMonth(d.getMonth() + m); return d.toISOString().slice(0, 10) }

export default function Formulario() {
  const { id } = useParams(); const edicion = !!id
  const nav = useNavigate(); const qc = useQueryClient()
  const [error, setError] = useState<{ texto: string; recargar?: boolean } | null>(null)
  const entidades = useQuery({ queryKey: ['entidades'], queryFn: () => api<Entidad[]>('/entidades') })
  const ficha = useQuery({ enabled: edicion, queryKey: ['estrategia', id], queryFn: () => apiRaw<Estrategia>(`/estrategias/${id}`) })
  const { register, handleSubmit, reset, setError: setCampo, formState: { errors, isSubmitting } } = useForm<DatosEstrategia>({
    resolver: zodResolver(esquemaEstrategia),
    defaultValues: { entidadId: '', nombre: '', objetivo: '', poblacion: '', fechaInicio: hoy(), fechaFin: enMeses(6) },
  })
  const e = ficha.data?.data
  useEffect(() => { if (e) reset({ entidadId: e.entidad.id, nombre: e.nombre, objetivo: e.objetivo, poblacion: e.poblacion, fechaInicio: e.fechaInicio, fechaFin: e.fechaFin }) }, [e, reset])
  const bloqueada = edicion && !!e && !e.editable

  async function guardar(d: DatosEstrategia) {
    setError(null)
    try {
      if (edicion) {
        await apiRaw(`/estrategias/${id}`, { method: 'PUT', body: d, headers: { 'If-Match': ficha.data?.etag ?? '' } })
        await qc.invalidateQueries({ queryKey: ['estrategia', id] }); await qc.invalidateQueries({ queryKey: ['estrategias'] })
        nav(`/estrategias/${id}`)
      } else {
        const r = await apiRaw<Estrategia>('/estrategias', { method: 'POST', body: d })
        await qc.invalidateQueries({ queryKey: ['estrategias'] })
        nav(`/estrategias/${r.data.id}`)
      }
    } catch (x) {
      if (!(x instanceof ApiError)) { setError({ texto: 'No hay conexión con el servidor. Intenta de nuevo.' }); return }
      if (x.code === 'NOMBRE_DUPLICADO') setCampo('nombre', { message: 'Ya existe una estrategia con ese nombre en la entidad.' })
      else if (x.code === 'ESTRATEGIA_BLOQUEADA') { setError({ texto: 'Bloqueada para edición: la estrategia ya tiene estudiantes vinculados.', recargar: true }); void ficha.refetch() }
      else if (x.code === 'VERSION_CAMBIADA') setError({ texto: 'La estrategia fue modificada por otra persona, recarga.', recargar: true })
      else if (x.code === 'VALIDACION') {
        const errs = (x.body.errores ?? {}) as Record<string, string[]>
        for (const [k, v] of Object.entries(errs)) { const campo = k.charAt(0).toLowerCase() + k.slice(1); setCampo(campo as keyof DatosEstrategia, { message: v[0] }) }
      } else setError({ texto: x.message })
    }
  }

  if (edicion && ficha.isLoading) return <p className="text-slate-500">Cargando…</p>
  if (edicion && ficha.isError) return <Aviso tono="error">No encontramos la estrategia. <Link className="underline" to="/estrategias">Volver al listado</Link></Aviso>

  return (
    <div className="max-w-2xl">
      <h1 className="text-2xl font-bold text-andino-900">{edicion ? 'Editar estrategia' : 'Nueva estrategia'}</h1>
      {bloqueada && <Aviso tono="alerta" className="mt-4" data-testid="aviso-bloqueada">Bloqueada para edición: la estrategia ya tiene {e!.estudiantesVinculados} estudiante(s) vinculado(s) y no admite cambios.</Aviso>}
      {error && (
        <Aviso tono="error" className="mt-4">{error.texto}{' '}
          {error.recargar && <button className="underline" onClick={() => { setError(null); void ficha.refetch() }}>Recargar</button>}
        </Aviso>
      )}
      <Tarjeta className="mt-4 p-5">
        <form onSubmit={(ev) => void handleSubmit(guardar)(ev)} className="grid gap-4" noValidate>
          <Campo etiqueta="Entidad" htmlFor="entidadId" error={errors.entidadId?.message}>
            <Seleccion id="entidadId" {...register('entidadId')} disabled={edicion || bloqueada}>
              <option value="">Selecciona una entidad…</option>
              {entidades.data?.map((x) => <option key={x.id} value={x.id}>{x.nombre}</option>)}
              {edicion && e && !entidades.data?.some((x) => x.id === e.entidad.id) && <option value={e.entidad.id}>{e.entidad.nombre}</option>}
            </Seleccion>
          </Campo>
          <Campo etiqueta="Nombre" htmlFor="nombre" error={errors.nombre?.message}><Entrada id="nombre" maxLength={150} {...register('nombre')} disabled={bloqueada} /></Campo>
          <Campo etiqueta="Objetivo" htmlFor="objetivo" error={errors.objetivo?.message} ayuda="Máximo 2.000 caracteres."><AreaTexto id="objetivo" maxLength={2000} {...register('objetivo')} disabled={bloqueada} /></Campo>
          <Campo etiqueta="Población" htmlFor="poblacion" error={errors.poblacion?.message}><Entrada id="poblacion" maxLength={300} {...register('poblacion')} disabled={bloqueada} /></Campo>
          <div className="grid sm:grid-cols-2 gap-4">
            <Campo etiqueta="Fecha de inicio" htmlFor="fechaInicio" error={errors.fechaInicio?.message}><Entrada id="fechaInicio" type="date" {...register('fechaInicio')} disabled={bloqueada} /></Campo>
            <Campo etiqueta="Fecha de fin" htmlFor="fechaFin" error={errors.fechaFin?.message}><Entrada id="fechaFin" type="date" {...register('fechaFin')} disabled={bloqueada} /></Campo>
          </div>
          <div className="flex gap-3 justify-end">
            <Link to={edicion ? `/estrategias/${id}` : '/estrategias'}><Boton type="button" variante="secundario">Cancelar</Boton></Link>
            <Boton type="submit" disabled={isSubmitting || bloqueada}>{edicion ? 'Guardar cambios' : 'Guardar'}</Boton>
          </div>
        </form>
      </Tarjeta>
    </div>
  )
}
