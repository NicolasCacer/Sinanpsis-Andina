import type { ButtonHTMLAttributes, ReactNode, InputHTMLAttributes, TextareaHTMLAttributes, SelectHTMLAttributes } from 'react'
import { forwardRef } from 'react'

export function Boton({ variante = 'primario', className = '', ...p }: ButtonHTMLAttributes<HTMLButtonElement> & { variante?: 'primario' | 'secundario' | 'peligro' | 'fantasma' }) {
  const v = {
    primario: 'bg-andino-900 text-white shadow-sm shadow-andino-900/20 hover:bg-andino-800 active:bg-andino-950 disabled:bg-slate-300 disabled:shadow-none',
    secundario: 'bg-white text-andino-900 border border-slate-300 shadow-sm hover:border-andino-600 hover:bg-andino-50 disabled:text-slate-400 disabled:hover:bg-white',
    peligro: 'bg-red-700 text-white shadow-sm hover:bg-red-600 disabled:bg-slate-300',
    fantasma: 'text-andino-700 hover:bg-andino-50 disabled:text-slate-400',
  }[variante]
  return <button {...p} className={`inline-flex items-center justify-center gap-2 rounded-lg px-4 py-2 text-sm font-semibold transition-colors duration-150 disabled:cursor-not-allowed focus:outline-none focus-visible:ring-2 focus-visible:ring-andino-600/50 focus-visible:ring-offset-1 ${v} ${className}`} />
}

export function Insignia({ tono, children, ...rest }: { tono: 'verde' | 'ambar' | 'gris' | 'rojo' | 'azul'; children: ReactNode } & React.HTMLAttributes<HTMLSpanElement>) {
  const t = { verde: 'bg-cumple-50 text-cumple', ambar: 'bg-amber-100 text-amber-800', gris: 'bg-slate-100 text-slate-600', rojo: 'bg-red-100 text-red-800', azul: 'bg-andino-50 text-andino-700' }[tono]
  return <span {...rest} className={`inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-medium ${t}`}>{children}</span>
}

export function Tarjeta({ children, className = '', ...rest }: { children: ReactNode; className?: string } & React.HTMLAttributes<HTMLElement>) {
  return <section {...rest} className={`rounded-xl bg-white border border-slate-200/80 shadow-suave ${className}`}>{children}</section>
}

export function Campo({ etiqueta, error, ayuda, children, htmlFor }: { etiqueta: string; error?: string; ayuda?: string; children: ReactNode; htmlFor: string }) {
  return (
    <div>
      <label htmlFor={htmlFor} className="block text-sm font-medium text-slate-700 mb-1">{etiqueta}</label>
      {children}
      {ayuda && !error && <p className="text-xs text-slate-500 mt-1">{ayuda}</p>}
      {error && <p role="alert" className="text-xs text-red-700 mt-1">{error}</p>}
    </div>
  )
}
const base = 'w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-andino-700/40 disabled:bg-slate-100 disabled:text-slate-500'
export const Entrada = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>((p, r) => <input ref={r} {...p} className={`${base} ${p.className ?? ''}`} />)
export const AreaTexto = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement>>((p, r) => <textarea ref={r} rows={4} {...p} className={`${base} ${p.className ?? ''}`} />)
export const Seleccion = forwardRef<HTMLSelectElement, SelectHTMLAttributes<HTMLSelectElement>>((p, r) => <select ref={r} {...p} className={`${base} ${p.className ?? ''}`} />)

export function Aviso({ tono = 'info', children, ...rest }: { tono?: 'info' | 'error' | 'ok' | 'alerta'; children: ReactNode } & React.HTMLAttributes<HTMLDivElement>) {
  const t = { info: 'bg-sky-50 border-sky-200 text-sky-900', error: 'bg-red-50 border-red-200 text-red-800', ok: 'bg-cumple-50 border-green-200 text-green-900', alerta: 'bg-amber-50 border-amber-200 text-amber-900' }[tono]
  return <div role={tono === 'error' ? 'alert' : 'status'} {...rest} className={`rounded-lg border px-4 py-3 text-sm ${t} ${rest.className ?? ''}`}>{children}</div>
}

export function Vacio({ titulo, detalle, accion }: { titulo: string; detalle?: string; accion?: ReactNode }) {
  return (
    <div className="text-center py-12 px-4">
      <p className="font-semibold text-slate-700">{titulo}</p>
      {detalle && <p className="text-sm text-slate-500 mt-1 max-w-sm mx-auto">{detalle}</p>}
      {accion && <div className="mt-4">{accion}</div>}
    </div>
  )
}
