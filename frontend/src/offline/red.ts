import { ApiError } from '../app/api'
/** Error de red/timeout (o pasarela caída): se trata como "sin conexión". */
export function esErrorDeRed(e: unknown): boolean {
  if (e instanceof ApiError) return [502, 503, 504].includes(e.status)
  return true // TypeError (fetch), AbortError (timeout)
}
