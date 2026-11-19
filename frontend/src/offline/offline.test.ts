// @vitest-environment node
import 'fake-indexeddb/auto'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const apiRaw = vi.fn()
vi.mock('../app/api', () => ({ apiRaw: (...a: unknown[]) => apiRaw(...a), ApiError: class ApiError extends Error { status = 0; code = ''; constructor(s: number, c: string, t: string) { super(t); this.status = s; this.code = c } } }))
vi.mock('../app/auth-manager', () => ({ getAccessToken: async () => 'tok' }))
vi.mock('../app/queryClient', () => ({ queryClient: { invalidateQueries: async () => undefined } }))
vi.mock('./planLocal', () => ({ refrescarPlan: async () => undefined }))

import { cifrarJson, descifrarJson, obtenerClave, olvidarClave } from './crypto'
import { db } from './db'
import { encolarSeguimiento } from './outbox'
import { cancelarReintentos, sincronizarAhora } from './sync'

const hito = { id: 'h1', mes: '2026-11', numeroSemana: 1, semanaIso: '2026-W45', fechaInicio: '2026-11-02', fechaFin: '2026-11-08', estado: 'PROGRAMADA' as const, version: 7, temas: 't', estrategia: { id: 'e', nombre: 'Estrategia X' }, docentes: [{ id: 'd1', nombres: 'Marina' }] }
const datos = { asistencia: [{ docenteId: 'd1', asistio: true }], actividades: 'Actividad secreta del tutor', avances: 'Avance' }
const foto = () => new Blob([new Uint8Array([0xff, 0xd8, 0xff, 1, 2, 3])], { type: 'image/jpeg' })

beforeEach(async () => { cancelarReintentos(); await db.delete(); await db.open(); apiRaw.mockReset() })

describe('cifrado local (D06)', () => {
  it('cifra y descifra; la CryptoKey no es extraíble', async () => {
    const c = await cifrarJson({ a: 'secreto' })
    expect(await descifrarJson<{ a: string }>(c)).toEqual({ a: 'secreto' })
    const k = await obtenerClave()
    expect(k.extractable).toBe(false)
    await expect(crypto.subtle.exportKey('raw', k)).rejects.toThrow()
  })
  it('la clave se reutiliza desde IndexedDB y al olvidarla lo cifrado queda ilegible', async () => {
    const c = await cifrarJson({ x: 1 })
    expect((await db.meta.get('claveLocal'))?.valor).toBeTruthy()
    await olvidarClave()
    expect(await db.meta.get('claveLocal')).toBeUndefined()
    await expect(descifrarJson(c)).rejects.toThrow() // clave nueva ≠ clave anterior
  })
})

describe('outbox', () => {
  it('encola cifrado, con clave de idempotencia y estado Pendiente (sin texto en claro)', async () => {
    const r = await encolarSeguimiento(hito, datos, [foto()])
    const fila = (await db.outbox.get(r.idempotencyKey))!
    expect(fila.estado).toBe('Pendiente'); expect(fila.versionBase).toBe(7)
    const crudo = new TextDecoder('latin1').decode(fila.payload.data)
    expect(crudo).not.toContain('secreta')
    expect(await db.evidenciasLocales.where('idempotencyKey').equals(r.idempotencyKey).count()).toBe(1)
  })
})

describe('motor de sincronización', () => {
  it('APLICADO → Sincronizado y se liberan las fotos locales; envía descifrado con su clave de idempotencia', async () => {
    const r = await encolarSeguimiento(hito, datos, [foto()])
    apiRaw.mockResolvedValue({ data: { resultados: [{ idempotencyKey: r.idempotencyKey, estado: 'APLICADO' }] } })
    expect(await sincronizarAhora()).toBe('ok')
    const body = apiRaw.mock.calls[0][1].body.items[0]
    expect(body.idempotencyKey).toBe(r.idempotencyKey); expect(body.actividades).toBe('Actividad secreta del tutor'); expect(body.evidencias).toHaveLength(1)
    expect((await db.outbox.get(r.idempotencyKey))!.estado).toBe('Sincronizado')
    expect(await db.evidenciasLocales.count()).toBe(0)
  })
  it('DUPLICADO se trata como Sincronizado', async () => {
    const r = await encolarSeguimiento(hito, datos, [])
    apiRaw.mockResolvedValue({ data: { resultados: [{ idempotencyKey: r.idempotencyKey, estado: 'DUPLICADO' }] } })
    await sincronizarAhora()
    expect((await db.outbox.get(r.idempotencyKey))!.estado).toBe('Sincronizado')
  })
  it('CONFLICTO → En conflicto y guarda la versión del servidor', async () => {
    const r = await encolarSeguimiento(hito, datos, [])
    const servidor = { actividades: 'del servidor', avances: 'a', origen: 'EN_LINEA', registradoPor: 'Tutor Uno', registradoEn: '2026-11-18T10:00:00Z', asistencia: [] }
    apiRaw.mockResolvedValue({ data: { resultados: [{ idempotencyKey: r.idempotencyKey, estado: 'CONFLICTO', versionServidor: 9, registroServidor: servidor }] } })
    await sincronizarAhora()
    expect((await db.outbox.get(r.idempotencyKey))!.estado).toBe('En conflicto')
    const c = (await db.conflictos.get(r.idempotencyKey))!
    expect(c.versionServidor).toBe(9); expect(c.servidor.actividades).toBe('del servidor')
  })
  it('un fallo de red deja el registro Pendiente (no se pierde) y reintenta', async () => {
    const r = await encolarSeguimiento(hito, datos, [])
    apiRaw.mockRejectedValue(new TypeError('Failed to fetch'))
    expect(await sincronizarAhora()).toBe('sin-red')
    const fila = (await db.outbox.get(r.idempotencyKey))!
    expect(fila.estado).toBe('Pendiente'); expect(fila.intentos).toBe(1)
    cancelarReintentos()
  })
  it('envía en orden de creación y en lotes de máximo 50', async () => {
    for (let i = 0; i < 55; i++) await encolarSeguimiento({ ...hito, id: 'h' + i }, datos, [])
    apiRaw.mockImplementation(async (_: string, o: { body: { items: { idempotencyKey: string }[] } }) => ({ data: { resultados: o.body.items.map((x) => ({ idempotencyKey: x.idempotencyKey, estado: 'APLICADO' })) } }))
    await sincronizarAhora()
    expect(apiRaw.mock.calls.map((c) => c[1].body.items.length)).toEqual([50, 5])
    expect(await db.outbox.where('estado').equals('Pendiente').count()).toBe(0)
  })
})
