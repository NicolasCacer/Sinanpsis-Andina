import { useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { api, apiRaw } from '../../app/api'
import { Aviso, Boton, Tarjeta, Vacio } from '../../ui/kit'
import { EstadoInsignia } from './Listado'
import type { Estrategia, EstudianteVinculado } from './tipos'

const f = (d: string) => new Date(d + 'T00:00:00').toLocaleDateString('es-CO', { dateStyle: 'long' })

export default function Ficha() {
  const { id } = useParams()
  const [sp] = useSearchParams()
  const [tab, setTab] = useState<'datos' | 'vinculados'>(sp.get('tab') === 'vinculados' ? 'vinculados' : 'datos')
  const q = useQuery({ queryKey: ['estrategia', id], queryFn: () => apiRaw<Estrategia>(`/estrategias/${id}`) })
  const e = q.data?.data
  if (q.isLoading) return <p className="text-slate-500">Cargando…</p>
  if (q.isError || !e) return <Aviso tono="error">No encontramos la estrategia. <Link className="underline" to="/estrategias">Volver al listado</Link></Aviso>
  return (
    <div className="max-w-4xl">
      <Link to="/estrategias" className="text-sm text-andino-700 underline">← Estrategias</Link>
      <div className="mt-2 flex items-start gap-3 flex-wrap">
        <div className="flex-1 min-w-0">
          <h1 className="text-2xl font-bold text-andino-900">{e.nombre}</h1>
          <p className="text-sm text-slate-500">{e.entidad.nombre}</p>
        </div>
        <div className="flex items-center gap-3">
          <div data-testid="estado-estrategia"><EstadoInsignia e={e.estado} /></div>
          <span className="text-sm text-slate-600" data-testid="contador-vinculados"><b>{e.estudiantesVinculados}</b> estudiantes vinculados</span>
        </div>
      </div>
      {!e.editable && <Aviso tono="alerta" className="mt-4" data-testid="aviso-bloqueada">Bloqueada para edición: la estrategia ya tiene estudiantes vinculados, por eso sus datos no se pueden modificar.</Aviso>}

      <div role="tablist" className="mt-5 flex gap-1 border-b border-slate-200">
        {(['datos', 'vinculados'] as const).map((t) => (
          <button key={t} role="tab" aria-selected={tab === t} onClick={() => setTab(t)}
            className={`px-4 py-2 text-sm font-medium -mb-px border-b-2 ${tab === t ? 'border-andino-700 text-andino-900' : 'border-transparent text-slate-500 hover:text-slate-700'}`}>
            {t === 'datos' ? 'Datos' : 'Estudiantes vinculados'}
          </button>
        ))}
      </div>

      {tab === 'datos' && (
        <Tarjeta className="mt-4 p-5">
          <dl className="grid sm:grid-cols-2 gap-x-6 gap-y-4 text-sm">
            <div className="sm:col-span-2"><dt className="text-slate-500">Objetivo</dt><dd className="mt-0.5 whitespace-pre-wrap">{e.objetivo}</dd></div>
            <div><dt className="text-slate-500">Población</dt><dd className="mt-0.5">{e.poblacion}</dd></div>
            <div><dt className="text-slate-500">Vigencia</dt><dd className="mt-0.5">{f(e.fechaInicio)} – {f(e.fechaFin)}</dd></div>
          </dl>
          <div className="mt-5 flex gap-3 justify-end">
            {e.editable
              ? <Link to="editar"><Boton>Editar</Boton></Link>
              : <Boton disabled title="Bloqueada para edición">Editar</Boton>}
          </div>
        </Tarjeta>
      )}
      {tab === 'vinculados' && <Vinculados id={id!} total={e.estudiantesVinculados} />}
    </div>
  )
}

function Vinculados({ id, total }: { id: string; total: number }) {
  const q = useInfiniteQuery({
    queryKey: ['vinculados', id], initialPageParam: '',
    queryFn: ({ pageParam }) => api<{ items: EstudianteVinculado[]; siguiente: string | null }>(`/estrategias/${id}/estudiantes${pageParam ? `?despuesDe=${pageParam}` : ''}`),
    getNextPageParam: (p) => p.siguiente ?? undefined,
  })
  const items = q.data?.pages.flatMap((p) => p.items) ?? []
  return (
    <Tarjeta className="mt-4">
      <div className="p-4 flex gap-3 justify-end flex-wrap border-b border-slate-100">
        <Boton variante="secundario" disabled title="Próximo sprint">Cargar listado <small className="opacity-70">(Próximo sprint)</small></Boton>
        <Link to="asociar"><Boton>Asociar estudiantes</Boton></Link>
      </div>
      {q.isLoading && <p className="p-6 text-slate-500">Cargando…</p>}
      {!q.isLoading && items.length === 0 && <Vacio titulo="Sin estudiantes vinculados" detalle="Usa 'Asociar estudiantes' para buscar por documento y vincular." />}
      <ul className="divide-y divide-slate-100" data-testid="lista-vinculados">
        {items.map((s) => (
          <li key={s.id} className="px-4 py-2.5 flex flex-wrap gap-x-4 text-sm"><span className="font-medium">{s.nombre}</span><span className="text-slate-500">{s.sede} · {s.municipio}</span></li>
        ))}
      </ul>
      {q.hasNextPage && <div className="p-3 text-center border-t border-slate-100"><Boton variante="fantasma" onClick={() => void q.fetchNextPage()}>Cargar más</Boton></div>}
      <p className="px-4 py-2 text-xs text-slate-400 border-t border-slate-100">{total} vinculados en total</p>
    </Tarjeta>
  )
}
