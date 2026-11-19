import type { SVGProps } from 'react'

/** Íconos de trazo (24×24) en línea: sin librería externa para no inflar el paquete inicial (RQ_027). */
type P = SVGProps<SVGSVGElement>
function Base({ children, ...p }: P) {
  return (
    <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" {...p}>
      {children}
    </svg>
  )
}

export const IconoInicio = (p: P) => <Base {...p}><path d="M3 10.5 12 3l9 7.5" /><path d="M5 9.5V20a1 1 0 0 0 1 1h4v-6h4v6h4a1 1 0 0 0 1-1V9.5" /></Base>
export const IconoEstrategias = (p: P) => <Base {...p}><path d="m12 3 9 5-9 5-9-5 9-5Z" /><path d="m3 13 9 5 9-5" /></Base>
export const IconoAcompanamiento = (p: P) => <Base {...p}><rect x="3" y="4.5" width="18" height="16" rx="2" /><path d="M3 9.5h18M8 2.5v4M16 2.5v4" /><path d="m9 15 2 2 4-4" /></Base>
export const IconoPilotaje = (p: P) => <Base {...p}><path d="M5 21V4" /><path d="M5 4h11l-2 4 2 4H5" /></Base>
export const IconoAyuda = (p: P) => <Base {...p}><circle cx="12" cy="12" r="9" /><path d="M9.5 9.3a2.6 2.6 0 0 1 5 .9c0 1.7-2.5 2.3-2.5 3.8" /><path d="M12 17.2h.01" /></Base>
export const IconoBitacora = (p: P) => <Base {...p}><path d="M12 3 4.5 6v5.5c0 4.5 3.2 8.2 7.5 9.5 4.3-1.3 7.5-5 7.5-9.5V6L12 3Z" /><path d="m9 12 2 2 4-4" /></Base>
export const IconoSalir = (p: P) => <Base {...p}><path d="M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3" /><path d="M10 16l-4-4 4-4M6 12h10" /></Base>
export const IconoChevron = (p: P) => <Base {...p}><path d="m6 9 6 6 6-6" /></Base>
export const IconoNube = (p: P) => <Base {...p}><path d="M7 18a4.5 4.5 0 0 1-.4-9 6 6 0 0 1 11.6 1.5A3.75 3.75 0 0 1 17.5 18H7Z" /><path d="M12 15v-5m-2 2 2-2 2 2" /></Base>
export const IconoSinSenal = (p: P) => <Base {...p}><path d="M2 2l20 20" /><path d="M8.5 16.5a5 5 0 0 1 7 0M5 13a10 10 0 0 1 5.2-2.8M19 13a10 10 0 0 0-2.3-1.7M2 8.8a15 15 0 0 1 4.2-2.6M22 8.8A15 15 0 0 0 11 5" /><path d="M12 20h.01" /></Base>
export const IconoAlerta = (p: P) => <Base {...p}><path d="M10.3 3.9 2.4 18a2 2 0 0 0 1.7 3h15.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z" /><path d="M12 9v4M12 17h.01" /></Base>
export const IconoCandado = (p: P) => <Base {...p}><rect x="4.5" y="10.5" width="15" height="10.5" rx="2" /><path d="M8 10.5V7a4 4 0 0 1 8 0v3.5" /></Base>
export const IconoFlecha = (p: P) => <Base {...p}><path d="M5 12h14M13 6l6 6-6 6" /></Base>

/** Ícono por clave del menú que entrega la API. */
export const IconoMenu = { inicio: IconoInicio, estrategias: IconoEstrategias, acompanamientos: IconoAcompanamiento, pilotaje: IconoPilotaje, ayuda: IconoAyuda, 'mi-acompanamiento': IconoAcompanamiento } as Record<string, (p: P) => React.JSX.Element>
