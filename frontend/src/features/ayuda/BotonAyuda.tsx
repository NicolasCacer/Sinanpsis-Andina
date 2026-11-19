import { useState } from 'react'
import { Link, useLocation } from 'react-router'
import { ApiError } from '../../app/api'
import { esErrorDeRed } from '../../offline/red'
import { queryClient } from '../../app/queryClient'
import { AreaTexto, Aviso, Boton, Campo, Seleccion } from '../../ui/kit'
import { reportarIncidente, TIPOS, type Incidente } from './ayuda'
import { IconoAyuda } from '../../ui/iconos'

/** HU_011: botón 'Ayuda' que aparece en cualquier pantalla del aplicativo (RQ_020). */
export default function BotonAyuda() {
  const [abierto, setAbierto] = useState(false)
  return (
    <>
      <button onClick={() => setAbierto(true)} data-testid="boton-ayuda" aria-label="Ayuda" title="¿Algo no funciona? Repórtalo aquí"
        className="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-sm font-medium text-andino-700 ring-1 ring-inset ring-andino-200 bg-andino-50/60 hover:bg-andino-50 hover:ring-andino-600 focus:outline-none focus-visible:ring-2 focus-visible:ring-andino-600/50">
        <IconoAyuda width={18} height={18} /><span className="hidden sm:inline">Ayuda</span>
      </button>
      {abierto && <FormularioAyuda cerrar={() => setAbierto(false)} />}
    </>
  )
}

function FormularioAyuda({ cerrar }: { cerrar: () => void }) {
  const { pathname } = useLocation()
  const [tipo, setTipo] = useState<Incidente['tipo'] | ''>('')
  const [descripcion, setDescripcion] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [caso, setCaso] = useState<Incidente | null>(null)

  async function enviar() {
    if (!tipo) { setError('Elige el tipo de inconveniente.'); return }
    if (!descripcion.trim()) { setError('Describe lo que pasó.'); return }
    setEnviando(true); setError(null)
    try {
      setCaso(await reportarIncidente(tipo, descripcion.trim(), pathname))
      void queryClient.invalidateQueries({ queryKey: ['mis-casos'] })
    } catch (e) {
      setError(esErrorDeRed(e) ? 'Sin conexión: no pudimos enviar tu caso. Inténtalo cuando vuelva la señal.' : e instanceof ApiError ? e.message : 'No pudimos enviar tu caso.')
    } finally { setEnviando(false) }
  }

  return (
    <div className="fixed inset-0 z-30 bg-black/40 flex items-end sm:items-center justify-center p-0 sm:p-4" onClick={cerrar}>
      <div role="dialog" aria-modal="true" aria-labelledby="titulo-ayuda" onClick={(e) => e.stopPropagation()}
        className="bg-white w-full sm:max-w-md rounded-t-2xl sm:rounded-2xl p-5 shadow-xl">
        <div className="flex items-center gap-3"><span className="grid place-items-center w-10 h-10 rounded-full bg-andino-50 text-andino-700"><IconoAyuda /></span><div><h2 id="titulo-ayuda" className="text-lg font-bold text-andino-900">Ayuda</h2><p className="text-xs text-slate-500">Mesa de ayuda del pilotaje</p></div></div>
        {caso ? (
          <div className="mt-3 space-y-3">
            <Aviso tono="ok" data-testid="caso-creado">
              Recibimos tu caso <strong data-testid="numero-caso">{caso.numero}</strong>. El equipo de soporte lo atenderá; puedes seguirlo en 'Mis casos'.
            </Aviso>
            <div className="flex gap-2 justify-end">
              <Link to="/ayuda" onClick={cerrar} className="rounded-lg px-4 py-2 text-sm font-semibold text-andino-700 hover:bg-andino-50">Mis casos</Link>
              <Boton onClick={cerrar}>Cerrar</Boton>
            </div>
          </div>
        ) : (
          <div className="mt-3 space-y-4">
            <p className="text-xs text-slate-500">No tienes que explicar en qué pantalla estabas: el aplicativo lo informa por ti.</p>
            <Campo etiqueta="Tipo de inconveniente" htmlFor="ayuda-tipo">
              <Seleccion id="ayuda-tipo" value={tipo} onChange={(e) => setTipo(e.target.value as Incidente['tipo'])}>
                <option value="">Selecciona…</option>
                {TIPOS.map((t) => <option key={t.valor} value={t.valor}>{t.etiqueta}</option>)}
              </Seleccion>
            </Campo>
            <Campo etiqueta="¿Qué pasó?" htmlFor="ayuda-descripcion">
              <AreaTexto id="ayuda-descripcion" maxLength={2000} value={descripcion} onChange={(e) => setDescripcion(e.target.value)} />
            </Campo>
            {error && <Aviso tono="error">{error}</Aviso>}
            <div className="flex gap-2 justify-end">
              <Boton variante="fantasma" onClick={cerrar}>Cancelar</Boton>
              <Boton onClick={() => void enviar()} disabled={enviando}>{enviando ? 'Enviando…' : 'Enviar'}</Boton>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
