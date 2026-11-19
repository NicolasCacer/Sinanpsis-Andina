import { lazy, Suspense, useState } from 'react'
import { Link, Route, Routes } from 'react-router'
import { useMutation, useQuery } from '@tanstack/react-query'
import { api, ApiError } from '../../app/api'
import { getAccessToken } from '../../app/auth-manager'
import { config } from '../../app/config'
import { Aviso, Boton, Insignia, Seleccion, Tarjeta, Vacio } from '../../ui/kit'
import { mesActual, rangoSemana } from '../acompanamiento/tipos'

const Grafico = lazy(() => import('./Grafico'))

type Filtros = { regiones: { id: number; nombre: string }[]; entidades: { id: string; nombre: string }[]; estrategias: { id: string; nombre: string; entidadId: string }[] }
type Fila = { id: string; nombres: string; region: string; programadas: number; registradas: number; confirmadas: number; cumplimiento: number; regularidad: number; docentesAtendidos: number }
type Tablero = {
  mes: string
  resumen: { tutores: number; programadas: number; registradas: number; cumplimiento: number; regularidad: number; docentesAtendidos: number }
  porRegion: { region: string; tutores: number; cumplimiento: number; regularidad: number }[]; ranking: Fila[]
}
type Detalle = { tutor: { id: string; nombres: string; region: string }; mes: string; cumplimiento: number
  semanas: { numero: number; semanaIso: string; fechaInicio: string; fechaFin: string; programadas: number; registradas: number; confirmadas: number; docentesAtendidos: number; cumplimiento: number }[] }
type Aplicados = { mes: string; region: string; entidadId: string; estrategiaId: string }

const qs = (f: Aplicados) => new URLSearchParams(Object.entries(f).filter(([, v]) => v)).toString()
const pct = (n: number) => `${n.toLocaleString('es-CO', { maximumFractionDigits: 1 })} %`

function Inicio() {
  return (
    <div className="max-w-2xl">
      <h1 className="text-2xl font-bold text-andino-900">Analítica</h1>
      <p className="mt-1 text-sm text-slate-500">Indicadores para seguimiento y toma de decisiones</p>
      <div className="mt-6"><Link to="tutores" className="block rounded-xl bg-white border border-slate-200 p-5 shadow-sm hover:border-andino-700">
        <span className="font-semibold text-andino-900">Desempeño de tutores</span>
        <span className="block text-xs text-slate-500 mt-1">Cumplimiento, regularidad y docentes atendidos por región</span></Link></div>
    </div>
  )
}

function Desempeno() {
  const filtros = useQuery({ queryKey: ['analitica-filtros'], queryFn: () => api<Filtros>('/analitica/filtros') })
  const [f, setF] = useState<Aplicados>({ mes: mesActual(), region: '', entidadId: '', estrategiaId: '' })
  const [aplicados, setAplicados] = useState<Aplicados>(f)
  const [tutor, setTutor] = useState<string | null>(null)
  const tablero = useQuery({ queryKey: ['analitica-tutores', aplicados], queryFn: () => api<Tablero>(`/analitica/tutores?${qs(aplicados)}`) })
  const detalle = useQuery({
    queryKey: ['analitica-detalle', tutor, aplicados], enabled: !!tutor,
    queryFn: () => api<Detalle>(`/analitica/tutores/${tutor}/detalle?${qs({ ...aplicados, region: '' })}`),
  })
  const exportar = useMutation({
    mutationFn: async () => {
      const t = await getAccessToken()
      const r = await fetch(`${config.apiUrl}/analitica/tutores/exportar?${qs(aplicados)}`, { headers: t ? { Authorization: `Bearer ${t}` } : {} })
      if (!r.ok) throw new ApiError(r.status, 'EXPORTAR', 'No pudimos exportar la tabla.')
      const url = URL.createObjectURL(await r.blob()); const a = document.createElement('a')
      a.href = url; a.download = `desempeno-tutores-${aplicados.mes}.xlsx`; a.click(); URL.revokeObjectURL(url)
    },
  })
  const estrategias = (filtros.data?.estrategias ?? []).filter((e) => !f.entidadId || e.entidadId === f.entidadId)
  const t = tablero.data

  return (
    <div className="max-w-5xl">
      <h1 className="text-2xl font-bold text-andino-900">Desempeño de tutores</h1>
      <p className="text-sm text-slate-500">Indicadores preagregados, actualizados cada 15 minutos</p>

      <Tarjeta className="mt-4 p-4">
        <form className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5 items-end" data-testid="filtros" onSubmit={(e) => { e.preventDefault(); setTutor(null); setAplicados(f) }}>
          <label className="text-sm"><span className="block text-slate-600 mb-1">Región</span>
            <Seleccion value={f.region} onChange={(e) => setF({ ...f, region: e.target.value })}><option value="">Todas</option>{filtros.data?.regiones.map((r) => <option key={r.id} value={r.id}>{r.nombre}</option>)}</Seleccion></label>
          <label className="text-sm"><span className="block text-slate-600 mb-1">Entidad</span>
            <Seleccion value={f.entidadId} onChange={(e) => setF({ ...f, entidadId: e.target.value, estrategiaId: '' })}><option value="">Todas</option>{filtros.data?.entidades.map((r) => <option key={r.id} value={r.id}>{r.nombre}</option>)}</Seleccion></label>
          <label className="text-sm"><span className="block text-slate-600 mb-1">Estrategia</span>
            <Seleccion value={f.estrategiaId} onChange={(e) => setF({ ...f, estrategiaId: e.target.value })}><option value="">Todas</option>{estrategias.map((r) => <option key={r.id} value={r.id}>{r.nombre}</option>)}</Seleccion></label>
          <label className="text-sm"><span className="block text-slate-600 mb-1">Mes</span>
            <input type="month" value={f.mes} onChange={(e) => e.target.value && setF({ ...f, mes: e.target.value })} className="w-full rounded-lg border border-slate-300 px-3 py-2 bg-white" /></label>
          <Boton type="submit">Aplicar</Boton>
        </form>
      </Tarjeta>

      {tablero.isLoading && <p className="mt-6 text-slate-500">Cargando…</p>}
      {tablero.isError && <Aviso tono="error" className="mt-4">No pudimos cargar el tablero. <button className="underline" onClick={() => void tablero.refetch()}>Reintentar</button></Aviso>}
      {t && t.resumen.tutores === 0 && <div className="mt-6"><Vacio titulo="Sin datos para estos filtros" detalle="Prueba con otro mes, región o estrategia." /></div>}
      {t && t.resumen.tutores > 0 && (
        <>
          <div className="mt-4 grid grid-cols-2 lg:grid-cols-4 gap-3" data-testid="indicadores">
            {[['Tutores', t.resumen.tutores.toLocaleString('es-CO'), 'tutores'], ['Cumplimiento', pct(t.resumen.cumplimiento), 'cumplimiento'], ['Regularidad', pct(t.resumen.regularidad), 'regularidad'], ['Docentes atendidos', t.resumen.docentesAtendidos.toLocaleString('es-CO'), 'docentes']].map(([l, v, k]) => (
              <Tarjeta key={k} className="p-4"><p className="text-xs text-slate-500">{l}</p><p className="text-2xl font-bold text-andino-900" data-testid={`kpi-${k}`}>{v}</p></Tarjeta>
            ))}
          </div>
          <Tarjeta className="mt-4 p-4"><h2 className="font-semibold text-andino-900 mb-2">Por región</h2>
            <Suspense fallback={<p className="text-slate-500 h-64">Cargando gráfico…</p>}><Grafico datos={t.porRegion} /></Suspense></Tarjeta>

          <Tarjeta className="mt-4 p-4">
            <div className="flex items-center justify-between gap-3 flex-wrap mb-2">
              <h2 className="font-semibold text-andino-900">Clasificación de tutores <span className="text-xs font-normal text-slate-500">(primeros {t.ranking.length})</span></h2>
              <Boton variante="secundario" onClick={() => exportar.mutate()} disabled={exportar.isPending}>Exportar</Boton>
            </div>
            {exportar.isError && <Aviso tono="error">No pudimos exportar la tabla. Intenta de nuevo.</Aviso>}
            <div className="overflow-x-auto">
              <table className="w-full text-sm" data-testid="ranking">
                <thead><tr className="text-left text-xs text-slate-500 border-b"><th className="py-2 pr-2">#</th><th>Tutor</th><th>Región</th><th className="text-right">Programadas</th><th className="text-right">Registradas</th><th className="text-right">Cumplimiento</th><th className="text-right">Regularidad</th><th className="text-right">Docentes</th></tr></thead>
                <tbody>
                  {t.ranking.map((r, i) => (
                    <tr key={r.id} className={`border-b border-slate-100 ${tutor === r.id ? 'bg-andino-50' : ''}`} data-testid="fila-tutor">
                      <td className="py-2 pr-2 text-slate-500">{i + 1}</td>
                      <td><button className="text-andino-700 underline text-left min-h-9" onClick={() => setTutor(r.id)}>{r.nombres}</button></td>
                      <td>{r.region}</td><td className="text-right">{r.programadas}</td><td className="text-right">{r.registradas}</td>
                      <td className="text-right"><Insignia tono={r.cumplimiento >= 80 ? 'verde' : r.cumplimiento >= 50 ? 'ambar' : 'rojo'}>{pct(r.cumplimiento)}</Insignia></td>
                      <td className="text-right">{pct(r.regularidad)}</td><td className="text-right">{r.docentesAtendidos}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Tarjeta>
        </>
      )}

      {tutor && (
        <Tarjeta className="mt-4 p-4" data-testid="detalle-tutor">
          {detalle.isLoading && <p className="text-slate-500">Cargando detalle…</p>}
          {detalle.isError && <Aviso tono="error">No pudimos cargar el detalle del tutor.</Aviso>}
          {detalle.data && (
            <>
              <div className="flex items-start justify-between gap-3"><div><h2 className="font-semibold text-andino-900">{detalle.data.tutor.nombres}</h2><p className="text-xs text-slate-500">{detalle.data.tutor.region} · {detalle.data.mes} · cumplimiento {pct(detalle.data.cumplimiento)}</p></div>
                <Boton variante="fantasma" onClick={() => setTutor(null)}>Cerrar</Boton></div>
              <table className="w-full text-sm mt-3">
                <thead><tr className="text-left text-xs text-slate-500 border-b"><th className="py-2">Semana</th><th className="text-right">Programadas</th><th className="text-right">Registradas</th><th className="text-right">Docentes atendidos</th><th className="text-right">Cumplimiento</th></tr></thead>
                <tbody>{detalle.data.semanas.map((s) => (
                  <tr key={s.numero} className="border-b border-slate-100" data-testid="fila-semana"><td className="py-2">Semana {s.numero} <span className="text-slate-500">· {rangoSemana(s.fechaInicio, s.fechaFin)}</span></td>
                    <td className="text-right">{s.programadas}</td><td className="text-right">{s.registradas}</td><td className="text-right">{s.docentesAtendidos}</td><td className="text-right">{pct(s.cumplimiento)}</td></tr>))}</tbody>
              </table>
            </>
          )}
        </Tarjeta>
      )}
    </div>
  )
}

export default function AnaliticaRoutes() {
  return <Routes><Route index element={<Inicio />} /><Route path="tutores" element={<Desempeno />} /></Routes>
}
