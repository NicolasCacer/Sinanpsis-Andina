import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../app/api'
import { Aviso, Boton, Insignia, Tarjeta, Vacio } from '../ui/kit'

type Evento = { id: number; fechaUtc: string; accion: string; entidad: string | null; entidadId: string | null; hash: string }
type Verificacion = { ok: boolean; filas: number; primerIdRoto: number | null }

const ETIQUETA: Record<string, string> = {
  LOGIN: 'Ingreso', CREAR_ESTRATEGIA: 'Crear estrategia', EDITAR_ESTRATEGIA: 'Editar estrategia', BUSCAR_ESTUDIANTE: 'Consulta de estudiante', VINCULAR: 'Vincular estudiantes',
  PROGRAMAR_PLAN: 'Programar plan', REGISTRAR_SEGUIMIENTO: 'Registrar seguimiento', SINCRONIZAR: 'Sincronizar', CONFLICTO_RESUELTO: 'Conflicto resuelto', ACCESO_DENEGADO: 'Acceso denegado',
}

export default function Auditoria() {
  const eventos = useQuery({ queryKey: ['auditoria'], queryFn: () => api<Evento[]>('/auditoria/recientes?limite=20') })
  const [ver, setVer] = useState<Verificacion | null>(null); const [verificando, setVerificando] = useState(false); const [err, setErr] = useState(false)
  async function verificar() {
    setVerificando(true); setErr(false)
    try { setVer(await api<Verificacion>('/auditoria/verificacion')) } catch { setErr(true) } finally { setVerificando(false) }
  }
  return (
    <div className="max-w-3xl">
      <h1 className="text-2xl font-bold text-andino-900">Bitácora de actividad</h1>
      <p className="text-sm text-slate-600 mt-1">Todo lo que haces en el SSES queda registrado a tu nombre. La bitácora es de solo inserción y encadena cada evento con el anterior mediante SHA-256.</p>
      <Tarjeta className="mt-4 p-4 flex items-center gap-3 flex-wrap">
        <Boton onClick={() => void verificar()} disabled={verificando}>Verificar cadena</Boton>
        {ver?.ok && <Insignia tono="verde" data-testid="cadena-ok">Cadena íntegra ✓</Insignia>}
        {ver?.ok && <span className="text-xs text-slate-500" data-testid="cadena-filas">{ver.filas.toLocaleString('es-CO')} eventos verificados</span>}
        {ver && !ver.ok && <Insignia tono="rojo" data-testid="cadena-rota">Cadena rota en el evento {ver.primerIdRoto}</Insignia>}
        {err && <span className="text-sm text-red-700">No pudimos verificar. Intenta de nuevo.</span>}
      </Tarjeta>
      <h2 className="mt-6 mb-2 font-semibold text-slate-800">Tus últimos eventos</h2>
      <Tarjeta>
        {eventos.isLoading && <p className="p-4 text-slate-500">Cargando…</p>}
        {eventos.isError && <div className="p-4"><Aviso tono="error">No pudimos cargar la bitácora (requiere conexión).</Aviso></div>}
        {eventos.data?.length === 0 && <Vacio titulo="Aún no hay eventos" />}
        <ul className="divide-y divide-slate-100" data-testid="eventos">
          {eventos.data?.map((e) => (
            <li key={e.id} className="px-4 py-2.5 flex items-center gap-3 text-sm flex-wrap">
              <span className="font-medium w-44">{ETIQUETA[e.accion] ?? e.accion}</span>
              <span className="text-slate-500 flex-1 min-w-32">{new Date(e.fechaUtc).toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'medium' })}</span>
              <code className="text-xs text-slate-400" title="Huella SHA-256 (primeros caracteres)">{e.hash}</code>
            </li>
          ))}
        </ul>
      </Tarjeta>
    </div>
  )
}
