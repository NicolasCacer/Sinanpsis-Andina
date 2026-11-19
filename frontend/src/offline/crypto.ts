// D06: clave AES-GCM 256 NO extraíble, guardada como objeto CryptoKey en IndexedDB (nunca en claro ni exportable).
import { db, type Cifrado } from './db'

const CLAVE = 'claveLocal'

export async function obtenerClave(): Promise<CryptoKey> {
  const m = await db.meta.get(CLAVE)
  if (m?.valor) return m.valor as CryptoKey
  const k = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt'])
  await db.meta.put({ clave: CLAVE, valor: k })
  return k
}

export async function cifrarBytes(bytes: ArrayBuffer | Uint8Array): Promise<Cifrado> {
  const iv = crypto.getRandomValues(new Uint8Array(12))
  const data = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, await obtenerClave(), bytes as BufferSource)
  return { iv, data }
}
export async function descifrarBytes(c: Cifrado): Promise<ArrayBuffer> {
  return crypto.subtle.decrypt({ name: 'AES-GCM', iv: c.iv }, await obtenerClave(), c.data)
}
export const cifrarJson = (o: unknown) => cifrarBytes(new TextEncoder().encode(JSON.stringify(o)))
export async function descifrarJson<T>(c: Cifrado): Promise<T> { return JSON.parse(new TextDecoder().decode(await descifrarBytes(c))) as T }

/** Al cerrar sesión: se borra la clave y lo cifrado queda ilegible (se eliminan también las tablas cifradas). */
export async function olvidarClave(): Promise<void> {
  await db.transaction('rw', db.meta, db.outbox, db.evidenciasLocales, db.conflictos, async () => {
    await db.meta.delete(CLAVE); await db.outbox.clear(); await db.evidenciasLocales.clear(); await db.conflictos.clear()
  })
}
