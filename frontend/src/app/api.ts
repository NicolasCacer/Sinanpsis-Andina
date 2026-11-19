import { config } from './config'
import { getAccessToken } from './auth-manager'

export class ApiError extends Error {
  status: number
  code: string
  body: Record<string, unknown>
  constructor(status: number, code: string, title: string, body: Record<string, unknown> = {}) {
    super(title)
    this.status = status
    this.code = code
    this.body = body
  }
}

export type ApiOpts = { method?: string; body?: unknown; headers?: Record<string, string>; form?: FormData; timeoutMs?: number }
export type ApiRes<T> = { data: T; etag: string | null; headers: Headers; status: number }

/** Llamada a la API con Bearer. Lanza ApiError con el `code` de Problem Details; errores de red lanzan TypeError. */
export async function apiRaw<T = unknown>(path: string, opts: ApiOpts = {}): Promise<ApiRes<T>> {
  const token = await getAccessToken()
  const headers: Record<string, string> = { Accept: 'application/json', ...(opts.headers ?? {}) }
  if (token) headers.Authorization = `Bearer ${token}`
  let body: BodyInit | undefined
  if (opts.form) body = opts.form
  else if (opts.body !== undefined) { headers['Content-Type'] = 'application/json'; body = JSON.stringify(opts.body) }
  const ctrl = new AbortController()
  const t = setTimeout(() => ctrl.abort(), opts.timeoutMs ?? 8000)
  let res: Response
  try {
    res = await fetch(`${config.apiUrl}${path}`, { method: opts.method ?? 'GET', headers, body, signal: ctrl.signal })
  } finally { clearTimeout(t) }
  const text = await res.text()
  const json = text ? (JSON.parse(text) as Record<string, unknown>) : null
  if (!res.ok) throw new ApiError(res.status, String(json?.code ?? 'ERROR'), String(json?.title ?? res.statusText), json ?? {})
  return { data: json as T, etag: res.headers.get('ETag'), headers: res.headers, status: res.status }
}

export async function api<T = unknown>(path: string, opts: ApiOpts = {}): Promise<T> {
  return (await apiRaw<T>(path, opts)).data
}
