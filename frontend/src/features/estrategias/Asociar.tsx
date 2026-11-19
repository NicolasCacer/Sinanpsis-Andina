import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { apiRaw, ApiError } from '../../app/api'
import { Aviso, Boton, Campo, Entrada, Insignia, Seleccion, Tarjeta } from '../../ui/kit'
import { TIPOS_DOC, type Estrategia } from './tipos'

type Resultado = { id: string; nombre: string; sede: string; municipio: string }

export default function Asociar() {
  const { id } = useParams(); const nav = useNavigate(); const qc = useQueryClient()
  const ficha = useQuery({ queryKey: ['estrategia', id], queryFn: () => apiRaw<Estrategia>(`/estrategias/${id}`) })
  const [tipo, setTipo] = useState<string>('TI'); const [num, setNum] = useState('')
  const [buscando, setBuscando] = useState(false)
  const [msg, setMsg] = useState<{ tono: 'error' | 'ok' | 'info'; texto: string } | null>(null)
  const [tiempo, setTiempo] = useState<{ db: string | null; total: number } | null>(null)
  const [lista, setLista] = useState<(Resultado & { marcado: boolean })[]>([])
  const [enviando, setEnviando] = useState(false)

  async function buscar(ev: React.FormEvent) {
    ev.preventDefault(); setMsg(null)
    if (!num.trim()) { setMsg({ tono: 'error', texto: 'Escribe el número de documento.' }); return }
    setBuscando(true); const t0 = performance.now()
    try {
      const r = await apiRaw<Resultado>(`/estudiantes?${new URLSearchParams({ tipoDoc: tipo, numDoc: num.trim() })}`)
      setTiempo({ db: /dur=([\d.]+)/.exec(r.headers.get('Server-Timing') ?? '')?.[1] ?? null, total: Math.round(performance.now() - t0) })
      setLista((l) => (l.some((x) => x.id === r.data.id) ? l : [...l, { ...r.data, marcado: true }]))
      setNum('')
    } catch (x) {
      setTiempo(null)
      if (x instanceof ApiError && x.status === 404) setMsg({ tono: 'info', texto: 'No encontramos un estudiante con ese documento.' })
      else if (x instanceof ApiError) setMsg({ tono: 'error', texto: x.message })
      else setMsg({ tono: 'error', texto: 'No hay conexión con el servidor. Intenta de nuevo.' })
    } finally { setBuscando(false) }
  }

  async function vincular() {
    const ids = lista.filter((x) => x.marcado).map((x) => x.id)
    if (ids.length === 0) return
    setEnviando(true); setMsg(null)
    try {
      const r = await apiRaw<{ vinculados: number; yaVinculados: number }>(`/estrategias/${id}/estudiantes`, { method: 'POST', body: { estudianteIds: ids } })
      await qc.invalidateQueries({ queryKey: ['estrategia', id] }); await qc.invalidateQueries({ queryKey: ['vinculados', id] }); await qc.invalidateQueries({ queryKey: ['estrategias'] })
      setLista([])
      setMsg({ tono: 'ok', texto: `${r.data.vinculados} vinculado(s)${r.data.yaVinculados ? `, ${r.data.yaVinculados} ya estaba(n) vinculado(s)` : ''}.` })
      setTimeout(() => nav(`/estrategias/${id}?tab=vinculados`), 900)
    } catch (x) {
      setMsg({ tono: 'error', texto: x instanceof ApiError ? x.message : 'No hay conexión con el servidor.' })
    } finally { setEnviando(false) }
  }

  const e = ficha.data?.data
  return (
    <div className="max-w-2xl">
      <Link to={`/estrategias/${id}`} className="text-sm text-andino-700 underline">← {e?.nombre ?? 'Estrategia'}</Link>
      <h1 className="mt-2 text-2xl font-bold text-andino-900">Asociar estudiantes</h1>
      {e?.editable && <Aviso className="mt-3" tono="info">Al vincular el primer estudiante, la estrategia quedará bloqueada para edición.</Aviso>}
      <Tarjeta className="mt-4 p-5">
        <form onSubmit={(ev) => void buscar(ev)} className="grid grid-cols-[7rem_1fr_auto] gap-3 items-end" noValidate>
          <Campo etiqueta="Tipo" htmlFor="tipoDoc"><Seleccion id="tipoDoc" value={tipo} onChange={(x) => setTipo(x.target.value)}>{TIPOS_DOC.map((t) => <option key={t}>{t}</option>)}</Seleccion></Campo>
          <Campo etiqueta="Número de documento" htmlFor="numDoc"><Entrada id="numDoc" inputMode="numeric" autoComplete="off" value={num} onChange={(x) => setNum(x.target.value)} /></Campo>
          <Boton type="submit" disabled={buscando}>Buscar</Boton>
        </form>
        {tiempo && <p data-testid="tiempo-busqueda" className="mt-3 text-xs text-slate-500">Resultado en {tiempo.total} ms{tiempo.db && <> (consulta en base de datos: {tiempo.db} ms)</>}</p>}
        {msg && <Aviso className="mt-3" tono={msg.tono}>{msg.texto}</Aviso>}
      </Tarjeta>

      {lista.length > 0 && (
        <Tarjeta className="mt-4">
          <ul className="divide-y divide-slate-100" data-testid="resultados-busqueda">
            {lista.map((r) => (
              <li key={r.id} className="px-4 py-3 flex items-center gap-3">
                <input type="checkbox" aria-label={`Seleccionar a ${r.nombre}`} checked={r.marcado} onChange={() => setLista((l) => l.map((x) => (x.id === r.id ? { ...x, marcado: !x.marcado } : x)))} className="w-4 h-4" />
                <div className="min-w-0 flex-1"><p className="font-medium">{r.nombre}</p><p className="text-xs text-slate-500">{r.sede} · {r.municipio}</p></div>
                <Insignia tono="azul">Encontrado</Insignia>
              </li>
            ))}
          </ul>
          <div className="p-3 border-t border-slate-100 flex justify-end"><Boton onClick={() => void vincular()} disabled={enviando || !lista.some((x) => x.marcado)}>Vincular</Boton></div>
        </Tarjeta>
      )}
    </div>
  )
}
