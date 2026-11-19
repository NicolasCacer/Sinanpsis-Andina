import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../../app/api'
import { getAccessToken } from '../../app/auth-manager'
import { config } from '../../app/config'
import { Aviso, Boton, Entrada, Seleccion, Tarjeta, Vacio } from '../../ui/kit'

type Plantillas = { organizacion: string; plantillas: { id: string; nombre: string; descripcion: string }[] }
type Reporte = { id: string; codigo: string; huella: string; plantilla: string; organizacion: string; vigencia: number; periodo: string | null; emitidaEn: string; encabezados: string[]; filas: string[][]; descarga: string }
type Emision = { id: string; plantilla: string; vigencia: number; periodo: string | null; codigo: string; huella: string; filas: number; emitidaEn: string; emitidoPor: string }

async function descargar(id: string, codigo: string) {
  const t = await getAccessToken()
  const r = await fetch(`${config.apiUrl}/reportes-ley/${id}/pdf`, { headers: t ? { Authorization: `Bearer ${t}` } : {} })
  if (!r.ok) throw new ApiError(r.status, 'DESCARGAR', 'No pudimos descargar el reporte.')
  const url = URL.createObjectURL(await r.blob()); const a = document.createElement('a')
  a.href = url; a.download = `reporte-${codigo}.pdf`; a.click(); URL.revokeObjectURL(url)
}
const fecha = (iso: string) => new Date(iso).toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'short' })

export default function ReportesLeyRoutes() {
  const qc = useQueryClient()
  const plantillas = useQuery({ queryKey: ['rl-plantillas'], queryFn: () => api<Plantillas>('/reportes-ley/plantillas') })
  const mis = useQuery({ queryKey: ['rl-mis'], queryFn: () => api<{ reportes: Emision[] }>('/reportes-ley') })
  const [plantillaId, setPlantillaId] = useState('')
  const [vigencia, setVigencia] = useState(String(new Date().getFullYear()))
  const [periodo, setPeriodo] = useState('')
  const generar = useMutation({
    mutationFn: () => api<Reporte>('/reportes-ley', { method: 'POST', timeoutMs: 40000, body: { plantillaId: plantillaId || plantillas.data?.plantillas[0]?.id, vigencia: Number(vigencia), periodo: periodo || null } }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['rl-mis'] }),
  })
  const bajar = useMutation({ mutationFn: (e: { id: string; codigo: string }) => descargar(e.id, e.codigo) })
  const r = generar.data
  const err = generar.error instanceof ApiError ? generar.error.message : generar.isError ? 'No pudimos generar el reporte. Intenta de nuevo.' : null

  return (
    <div className="max-w-5xl">
      <h1 className="text-2xl font-bold text-andino-900">Reportes de Ley</h1>
      <p className="text-sm text-slate-500">{plantillas.data ? `Entidad: ${plantillas.data.organizacion}` : 'Reportes autorizados para tu entidad'}</p>
      {plantillas.isError && <Aviso tono="error" className="mt-4">No pudimos cargar los reportes de tu entidad.</Aviso>}

      <Tarjeta className="mt-4 p-4">
        <form className="grid gap-3 sm:grid-cols-4 items-end" data-testid="form-reporte" onSubmit={(e) => { e.preventDefault(); generar.mutate() }}>
          <label className="text-sm sm:col-span-2"><span className="block text-slate-600 mb-1">Reporte</span>
            <Seleccion value={plantillaId} onChange={(e) => setPlantillaId(e.target.value)}>{plantillas.data?.plantillas.map((p) => <option key={p.id} value={p.id}>{p.nombre}</option>)}</Seleccion></label>
          <label className="text-sm"><span className="block text-slate-600 mb-1">Vigencia</span>
            <Entrada type="number" min={2020} max={2100} value={vigencia} onChange={(e) => setVigencia(e.target.value)} /></label>
          <label className="text-sm"><span className="block text-slate-600 mb-1">Periodo (opcional)</span>
            <Entrada type="month" value={periodo} onChange={(e) => setPeriodo(e.target.value)} /></label>
          <Boton type="submit" disabled={generar.isPending || !plantillas.data}>{generar.isPending ? 'Generando…' : 'Generar'}</Boton>
        </form>
        {err && <Aviso tono="error" className="mt-3">{err}</Aviso>}
      </Tarjeta>

      {r && (
        <Tarjeta className="mt-4 p-4" data-testid="vista-previa">
          <div className="flex items-start justify-between gap-3 flex-wrap">
            <div><h2 className="font-semibold text-andino-900">Vista previa · {r.plantilla}</h2>
              <p className="text-xs text-slate-500">Vigencia {r.vigencia}{r.periodo ? ` · ${r.periodo}` : ''} · {r.filas.length} filas</p></div>
            <Boton onClick={() => bajar.mutate({ id: r.id, codigo: r.codigo })} disabled={bajar.isPending}>Descargar</Boton>
          </div>
          {bajar.isError && <Aviso tono="error" className="mt-2">No pudimos descargar el reporte. Intenta de nuevo.</Aviso>}
          <div className="overflow-x-auto mt-3">
            {r.filas.length === 0 ? <Vacio titulo="Sin datos para esa vigencia" detalle="El reporte se emitió vacío; su huella queda registrada." /> : (
              <table className="w-full text-sm"><thead><tr className="text-left text-xs text-slate-500 border-b">{r.encabezados.map((h) => <th key={h} className="py-2 pr-2">{h}</th>)}</tr></thead>
                <tbody>{r.filas.map((f, i) => <tr key={i} className="border-b border-slate-100" data-testid="fila-reporte">{f.map((v, j) => <td key={j} className="py-1.5 pr-2">{v}</td>)}</tr>)}</tbody></table>)}
          </div>
          <dl className="mt-3 text-xs text-slate-600 grid gap-1">
            <div><dt className="inline font-medium">Código de verificación: </dt><dd className="inline font-mono text-sm text-andino-900" data-testid="codigo-verificacion">{r.codigo}</dd></div>
            <div><dt className="inline font-medium">Huella SHA-256: </dt><dd className="inline font-mono break-all" data-testid="huella">{r.huella}</dd></div>
          </dl>
        </Tarjeta>
      )}

      <Tarjeta className="mt-4 p-4" data-testid="mis-reportes">
        <h2 className="font-semibold text-andino-900 mb-2">Mis reportes</h2>
        <p className="text-xs text-slate-500 mb-2">Solo los emitidos por tu entidad</p>
        {mis.isError && <Aviso tono="error">No pudimos cargar tus reportes.</Aviso>}
        {mis.data && mis.data.reportes.length === 0 && <Vacio titulo="Aún no has emitido reportes" detalle="Elige un reporte y la vigencia, y pulsa 'Generar'." />}
        {mis.data && mis.data.reportes.length > 0 && (
          <div className="overflow-x-auto"><table className="w-full text-sm">
            <thead><tr className="text-left text-xs text-slate-500 border-b"><th className="py-2">Reporte</th><th>Vigencia</th><th>Emitido</th><th>Por</th><th>Código</th><th /></tr></thead>
            <tbody>{mis.data.reportes.map((e) => (
              <tr key={e.id} className="border-b border-slate-100" data-testid="fila-emision"><td className="py-2">{e.plantilla}</td><td>{e.vigencia}{e.periodo ? ` · ${e.periodo}` : ''}</td><td>{fecha(e.emitidaEn)}</td><td>{e.emitidoPor}</td>
                <td className="font-mono">{e.codigo}</td><td className="text-right"><Boton variante="fantasma" onClick={() => bajar.mutate({ id: e.id, codigo: e.codigo })}>Descargar</Boton></td></tr>))}</tbody></table></div>)}
      </Tarjeta>
    </div>
  )
}
