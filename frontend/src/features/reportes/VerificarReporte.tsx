import { useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { config } from '../../app/config'
import { Aviso, Boton, Entrada, Tarjeta } from '../../ui/kit'

type Resultado = { valido: boolean; plantilla: string; organizacion: string; vigencia: number; periodo: string | null; emitidaEn: string; filas: number; huella: string; huellaArchivo: string; coincideArchivo: boolean | null }

async function sha256(f: File) {
  const h = await crypto.subtle.digest('SHA-256', await f.arrayBuffer())
  return [...new Uint8Array(h)].map((b) => b.toString(16).padStart(2, '0')).join('')
}

/** RQ_014: verificación pública (sin sesión) por código; opcionalmente compara la huella del PDF que tiene el usuario (se calcula en su navegador). */
export default function VerificarReporte() {
  const { codigo: param } = useParams()
  const nav = useNavigate()
  const [codigo, setCodigo] = useState(param ?? '')
  const [archivo, setArchivo] = useState<File | null>(null)
  const [res, setRes] = useState<Resultado | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [cargando, setCargando] = useState(false)

  async function verificar(e: React.FormEvent) {
    e.preventDefault(); setError(null); setRes(null)
    const c = codigo.trim(); if (!c) return
    setCargando(true)
    try {
      const q = archivo ? `?sha256=${await sha256(archivo)}` : ''
      const r = await fetch(`${config.apiUrl}/reportes-ley/verificar/${encodeURIComponent(c)}${q}`)
      if (r.status === 404) setError('No existe un reporte con ese código de verificación.')
      else if (!r.ok) setError('No pudimos verificar el reporte. Intenta de nuevo.')
      else { setRes((await r.json()) as Resultado); nav(`/verificar/${encodeURIComponent(c)}`, { replace: true }) }
    } catch { setError('No pudimos verificar el reporte. Revisa tu conexión.') } finally { setCargando(false) }
  }

  return (
    <main className="min-h-screen bg-slate-50 p-6 flex flex-col items-center">
      <div className="w-full max-w-xl">
        <a href="/" className="text-sm text-andino-700 underline">SSES · Sinapsis Andina</a>
        <h1 className="mt-2 text-2xl font-bold text-andino-900">Verificar un reporte de ley</h1>
        <p className="text-sm text-slate-500">Comprueba que el reporte fue emitido por el SSES y que su contenido no cambió. No necesitas ingresar.</p>
        <Tarjeta className="mt-4 p-4">
          <form onSubmit={verificar} className="grid gap-3">
            <label className="text-sm"><span className="block text-slate-600 mb-1">Código de verificación</span>
              <Entrada value={codigo} onChange={(e) => setCodigo(e.target.value)} placeholder="XXXX-XXXX-XXXX" className="font-mono" /></label>
            <label className="text-sm"><span className="block text-slate-600 mb-1">Archivo PDF (opcional, para comprobar que es el original)</span>
              <input type="file" accept="application/pdf" onChange={(e) => setArchivo(e.target.files?.[0] ?? null)} className="text-sm" /></label>
            <Boton type="submit" disabled={cargando || !codigo.trim()}>{cargando ? 'Verificando…' : 'Verificar'}</Boton>
          </form>
        </Tarjeta>
        {error && <Aviso tono="error" className="mt-4" data-testid="verificacion-error">{error}</Aviso>}
        {res && (
          <Tarjeta className="mt-4 p-4" data-testid="verificacion-ok">
            <Aviso tono="ok">Reporte válido: lo emitió el SSES para {res.organizacion}.</Aviso>
            <dl className="mt-3 text-sm grid gap-1">
              <div><dt className="inline text-slate-500">Reporte: </dt><dd className="inline">{res.plantilla}</dd></div>
              <div><dt className="inline text-slate-500">Vigencia: </dt><dd className="inline">{res.vigencia}{res.periodo ? ` · ${res.periodo}` : ''}</dd></div>
              <div><dt className="inline text-slate-500">Emitido: </dt><dd className="inline">{new Date(res.emitidaEn).toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'short' })}</dd></div>
              <div><dt className="inline text-slate-500">Huella SHA-256 del contenido: </dt><dd className="block font-mono text-xs break-all">{res.huella}</dd></div>
            </dl>
            {res.coincideArchivo === true && <Aviso tono="ok" className="mt-3" data-testid="archivo-coincide">El archivo que subiste es idéntico al original.</Aviso>}
            {res.coincideArchivo === false && <Aviso tono="error" className="mt-3" data-testid="archivo-no-coincide">El archivo que subiste NO coincide con el original: fue modificado o es otro documento.</Aviso>}
          </Tarjeta>
        )}
      </div>
    </main>
  )
}
