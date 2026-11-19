import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { apiRaw, ApiError } from '../../app/api'
import { AreaTexto, Aviso, Boton, Campo, Tarjeta } from '../../ui/kit'
import { reducirImagen } from '../../lib/imagen'
import { guardarEnLinea } from './guardar'
import { rangoSemana, type HitoDetalle } from './tipos'

type Foto = { blob: Blob; url: string }

export default function Registrar() {
  const { hitoId } = useParams(); const nav = useNavigate(); const qc = useQueryClient()
  const q = useQuery({ queryKey: ['hito', hitoId], queryFn: () => apiRaw<HitoDetalle>(`/hitos/${hitoId}`) })
  const h = q.data?.data
  const [asis, setAsis] = useState<Record<string, boolean>>({})
  const [actividades, setActividades] = useState(''); const [avances, setAvances] = useState('')
  const [fotos, setFotos] = useState<Foto[]>([]); const [procesando, setProcesando] = useState(false)
  const [enviando, setEnviando] = useState(false)
  const [errores, setErrores] = useState<Record<string, string>>({})
  const [aviso, setAviso] = useState<{ tono: 'error' | 'alerta'; texto: string } | null>(null)
  useEffect(() => { if (h) setAsis(Object.fromEntries(h.docentes.map((d) => [d.id, false]))) }, [h])
  useEffect(() => () => fotos.forEach((f) => URL.revokeObjectURL(f.url)), [fotos])

  async function agregarFotos(files: FileList | null) {
    if (!files) return
    setProcesando(true); setAviso(null)
    try {
      const nuevas: Foto[] = []
      for (const f of Array.from(files).slice(0, 5 - fotos.length)) {
        const blob = await reducirImagen(f); nuevas.push({ blob, url: URL.createObjectURL(blob) })
      }
      setFotos((l) => [...l, ...nuevas].slice(0, 5))
    } catch (e) { setAviso({ tono: 'error', texto: e instanceof Error ? e.message : 'No se pudo procesar la foto.' }) }
    finally { setProcesando(false) }
  }

  async function guardar() {
    if (!h) return
    const e: Record<string, string> = {}
    if (!actividades.trim()) e.actividades = 'Describe las actividades.'
    if (!avances.trim()) e.avances = 'Describe los avances.'
    setErrores(e); if (Object.keys(e).length) return
    setEnviando(true); setAviso(null)
    try {
      await guardarEnLinea(h.id, q.data!.etag ? Number(/\d+/.exec(q.data!.etag)![0]) : h.version,
        { asistencia: h.docentes.map((d) => ({ docenteId: d.id, asistio: !!asis[d.id] })), actividades, avances }, fotos.map((f) => f.blob))
      await qc.invalidateQueries({ queryKey: ['plan'] })
      nav(`/acompanamientos?mes=${h.mes}`, { state: { registrada: h.numeroSemana } })
    } catch (x) {
      if (x instanceof ApiError && x.code === 'VERSION_CAMBIADA') setAviso({ tono: 'alerta', texto: 'La semana cambió mientras la editabas. Recarga para ver la versión vigente.' })
      else if (x instanceof ApiError && x.code === 'SEMANA_CERRADA') setAviso({ tono: 'alerta', texto: 'Esta semana ya tiene un seguimiento registrado.' })
      else if (x instanceof ApiError && x.code === 'VALIDACION') { const m = (x.body.errores ?? {}) as Record<string, string[]>; setErrores(Object.fromEntries(Object.entries(m).map(([k, v]) => [k, v[0]]))); setAviso({ tono: 'error', texto: 'Revisa los campos marcados.' }) }
      else setAviso({ tono: 'error', texto: x instanceof ApiError ? x.message : 'No pudimos guardar. Intenta de nuevo.' })
    } finally { setEnviando(false) }
  }

  if (q.isLoading) return <p className="text-slate-500">Cargando…</p>
  if (q.isError || !h) return <Aviso tono="error">No encontramos esa semana. <Link className="underline" to="/acompanamientos">Volver al plan</Link></Aviso>
  const cerrada = h.estado !== 'PROGRAMADA'
  return (
    <div className="max-w-xl">
      <Link to={`/acompanamientos?mes=${h.mes}`} className="text-sm text-andino-700 underline">← Plan mensual</Link>
      <h1 className="mt-2 text-xl font-bold text-andino-900">Registrar seguimiento</h1>
      <p className="text-sm text-slate-500">Semana {h.numeroSemana} · {rangoSemana(h.fechaInicio, h.fechaFin)} · {h.estrategia.nombre}</p>
      {cerrada && <Aviso tono="alerta" className="mt-3">Esta semana ya tiene un seguimiento registrado.</Aviso>}
      {aviso && <Aviso tono={aviso.tono} className="mt-3">{aviso.texto} {aviso.texto.includes('Recarga') && <button className="underline" onClick={() => { setAviso(null); void q.refetch() }}>Recargar</button>}</Aviso>}

      <Tarjeta className="mt-4 p-4 grid gap-5">
        <fieldset>
          <legend className="text-sm font-medium text-slate-700 mb-1">Asistencia por docente</legend>
          <ul className="divide-y divide-slate-100">
            {h.docentes.map((d) => (
              <li key={d.id}><label className="flex items-center justify-between gap-3 py-3 min-h-12 cursor-pointer">
                <span className="text-sm">{d.nombres}</span>
                <span className="flex items-center gap-2 text-sm"><input type="checkbox" className="w-6 h-6" checked={!!asis[d.id]} onChange={(e) => setAsis((a) => ({ ...a, [d.id]: e.target.checked }))} aria-label={`Asistió ${d.nombres}`} />{asis[d.id] ? 'Asistió' : 'No asistió'}</span>
              </label></li>
            ))}
          </ul>
        </fieldset>
        <Campo etiqueta="Actividades" htmlFor="actividades" error={errores.actividades} ayuda="Máximo 4.000 caracteres."><AreaTexto id="actividades" maxLength={4000} value={actividades} onChange={(e) => setActividades(e.target.value)} disabled={cerrada} /></Campo>
        <Campo etiqueta="Avances" htmlFor="avances" error={errores.avances} ayuda="Máximo 4.000 caracteres."><AreaTexto id="avances" maxLength={4000} value={avances} onChange={(e) => setAvances(e.target.value)} disabled={cerrada} /></Campo>
        <div>
          <p className="text-sm font-medium text-slate-700 mb-1">Fotos de evidencia <span className="text-slate-400 font-normal">(hasta 5)</span></p>
          <input id="fotos" data-testid="input-fotos" type="file" accept="image/*" capture="environment" multiple disabled={cerrada || fotos.length >= 5}
            onChange={(e) => { void agregarFotos(e.target.files); e.target.value = '' }} className="block w-full text-sm file:mr-3 file:rounded-lg file:border-0 file:bg-andino-50 file:px-4 file:py-2.5 file:text-andino-900 file:font-medium" />
          {procesando && <p className="text-xs text-slate-500 mt-1">Reduciendo imagen…</p>}
          <ul className="mt-2 flex gap-2 flex-wrap" data-testid="fotos">
            {fotos.map((f, i) => (
              <li key={f.url} className="relative"><img src={f.url} alt={`Evidencia ${i + 1}`} className="w-20 h-20 object-cover rounded-lg border" />
                <span className="absolute bottom-0 inset-x-0 bg-black/60 text-white text-[10px] text-center rounded-b-lg" data-testid="peso-foto">{Math.round(f.blob.size / 1024)} KB</span>
                <button aria-label={`Quitar foto ${i + 1}`} onClick={() => setFotos((l) => l.filter((x) => x !== f))} className="absolute -top-2 -right-2 w-6 h-6 rounded-full bg-red-700 text-white text-xs">×</button></li>
            ))}
          </ul>
        </div>
        <div className="flex gap-3 justify-end">
          <Link to={`/acompanamientos?mes=${h.mes}`}><Boton variante="secundario" type="button">Cancelar</Boton></Link>
          <Boton onClick={() => void guardar()} disabled={enviando || procesando || cerrada}>Guardar</Boton>
        </div>
      </Tarjeta>
    </div>
  )
}
