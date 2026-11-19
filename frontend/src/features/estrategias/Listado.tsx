import { useState } from 'react'
import { Link } from 'react-router'
import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { api } from '../../app/api'
import { Boton, Insignia, Seleccion, Tarjeta, Vacio, Aviso } from '../../ui/kit'
import type { Entidad, Estrategia } from './tipos'

type Pagina = { items: Estrategia[]; siguiente: string | null }
export function EstadoInsignia({ e }: { e: Estrategia['estado'] }) {
  return e === 'EDITABLE' ? <Insignia tono="verde">Editable</Insignia> : <Insignia tono="ambar" data-testid="insignia-bloqueada">🔒 Bloqueada para edición</Insignia>
}

export default function Listado() {
  const [entidad, setEntidad] = useState(''); const [estado, setEstado] = useState('')
  const entidades = useQuery({ queryKey: ['entidades'], queryFn: () => api<Entidad[]>('/entidades') })
  const q = useInfiniteQuery({
    queryKey: ['estrategias', entidad, estado],
    initialPageParam: '' as string,
    queryFn: ({ pageParam }) => api<Pagina>(`/estrategias?${new URLSearchParams({ ...(entidad && { entidadId: entidad }), ...(estado && { estado }), ...(pageParam && { despuesDe: pageParam }) })}`),
    getNextPageParam: (p) => p.siguiente ?? undefined,
  })
  const items = q.data?.pages.flatMap((p) => p.items) ?? []
  return (
    <div className="max-w-5xl">
      <div className="flex items-center justify-between gap-3 flex-wrap">
        <h1 className="text-2xl font-bold text-andino-900">Estrategias</h1>
        <Link to="nueva"><Boton>Nueva estrategia</Boton></Link>
      </div>
      <div className="mt-4 flex gap-3 flex-wrap">
        <Seleccion aria-label="Filtrar por entidad" value={entidad} onChange={(e) => setEntidad(e.target.value)} className="!w-auto min-w-48">
          <option value="">Todas las entidades</option>
          {entidades.data?.map((e) => <option key={e.id} value={e.id}>{e.nombre}</option>)}
        </Seleccion>
        <Seleccion aria-label="Filtrar por estado" value={estado} onChange={(e) => setEstado(e.target.value)} className="!w-auto">
          <option value="">Todos los estados</option><option value="EDITABLE">Editable</option><option value="BLOQUEADA">Bloqueada para edición</option>
        </Seleccion>
      </div>
      <Tarjeta className="mt-4 overflow-hidden">
        {q.isLoading && <p className="p-6 text-slate-500">Cargando estrategias…</p>}
        {q.isError && <div className="p-4"><Aviso tono="error">No pudimos cargar las estrategias. <button className="underline" onClick={() => void q.refetch()}>Reintentar</button></Aviso></div>}
        {!q.isLoading && !q.isError && items.length === 0 && (
          <Vacio titulo="Aún no hay estrategias" detalle="Crea la primera estrategia para una de tus entidades." accion={<Link to="nueva"><Boton>Nueva estrategia</Boton></Link>} />
        )}
        <ul className="divide-y divide-slate-100">
          {items.map((e) => (
            <li key={e.id}>
              <Link to={e.id} className="flex items-center gap-3 px-4 py-3 hover:bg-slate-50 flex-wrap" data-testid="fila-estrategia">
                <div className="min-w-0 flex-1">
                  <p className="font-medium text-slate-900 truncate">{e.nombre}</p>
                  <p className="text-xs text-slate-500 truncate">{e.entidad.nombre}</p>
                </div>
                <span className="text-xs text-slate-500">{e.estudiantesVinculados} estudiantes</span>
                <EstadoInsignia e={e.estado} />
              </Link>
            </li>
          ))}
        </ul>
        {q.hasNextPage && <div className="p-3 text-center border-t border-slate-100"><Boton variante="fantasma" onClick={() => void q.fetchNextPage()} disabled={q.isFetchingNextPage}>Cargar más</Boton></div>}
      </Tarjeta>
    </div>
  )
}
