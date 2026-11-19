import { createHmac } from 'node:crypto'
import { readFileSync, writeFileSync } from 'node:fs'
import { expect, type Page } from '@playwright/test'

export const DEMO_PASSWORD = process.env.DEMO_PASSWORD ?? 'Sinapsis2026!Demo'
export const DEMO_TOTP_SECRET = process.env.DEMO_TOTP_SECRET ?? 'KNUW4YLQONUXGQLOMRUW4YKEMVWW6MBR'
export const API = process.env.API_URL ?? 'http://localhost:5080/api/v1'
export const KC = process.env.KC_URL ?? 'http://localhost:8180'

function base32Decode(s: string) {
  const al = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = '', out: number[] = []
  for (const c of s.replace(/=+$/, '')) bits += al.indexOf(c).toString(2).padStart(5, '0')
  for (let i = 0; i + 8 <= bits.length; i += 8) out.push(parseInt(bits.slice(i, i + 8), 2))
  return Buffer.from(out)
}
export function totp(secret = DEMO_TOTP_SECRET, offset = 0, now = Date.now()) {
  const counter = Math.floor(now / 30000) + offset
  const buf = Buffer.alloc(8); buf.writeBigUInt64BE(BigInt(counter))
  const h = createHmac('sha1', base32Decode(secret)).update(buf).digest()
  const o = h[h.length - 1] & 0xf
  const code = ((h[o] & 0x7f) << 24 | h[o + 1] << 16 | h[o + 2] << 8 | h[o + 3]) % 1_000_000
  return String(code).padStart(6, '0')
}

// Contador TOTP usado por usuario, persistido entre corridas (Keycloak no admite reutilizar un código)
const ESTADO = new URL('./.totp-state.json', import.meta.url)
const leer = (): Record<string, number> => { try { return JSON.parse(readFileSync(ESTADO, 'utf8')) } catch { return {} } }
const ultimo = { get: (k: string) => leer()[k], set: (k: string, v: number) => writeFileSync(ESTADO, JSON.stringify({ ...leer(), [k]: v })) }

/** Ingresa por Keycloak (correo + contraseña + TOTP). Reintenta en la siguiente ventana si el código ya se usó. */
export async function login(page: Page, correo: string) {
  await page.goto('/')
  await page.getByRole('button', { name: 'Ingresar' }).click()
  await page.locator('#username').fill(correo)
  await page.locator('#password').fill(DEMO_PASSWORD)
  await page.locator('#kc-login').click()
  // Keycloak rechaza reutilizar un código: se usan contadores estrictamente crecientes por usuario (lookAhead 1)
  let ahora = Math.floor(Date.now() / 30000)
  const prev = ultimo.get(correo)
  const c = prev === undefined ? ahora : Math.max(prev + 1, ahora - 1)
  if (c > ahora + 1) { await page.waitForTimeout((c - 1) * 30000 - Date.now() + 500); ahora = Math.floor(Date.now() / 30000) }
  ultimo.set(correo, c)
  await page.locator('#otp').fill(totp(undefined, c - ahora))
  await page.locator('#kc-login').click()
  await page.waitForURL(/\/inicio/, { timeout: 15000 })
  await expect(page.getByTestId('encabezado-nombre')).toBeVisible()
}

export async function tokenDe(page: Page): Promise<string> {
  const t = await page.evaluate(() => {
    const k = Object.keys(sessionStorage).find((x) => x.startsWith('oidc.user:'))
    return k ? (JSON.parse(sessionStorage.getItem(k)!) as { access_token: string }).access_token : null
  })
  if (!t) throw new Error('Sin token en sessionStorage')
  return t
}

/** Admin de Keycloak (solo para pruebas): limpia el bloqueo por fuerza bruta de un usuario. */
export async function limpiarBloqueos(request: import('@playwright/test').APIRequestContext) {
  const tk = await request.post(`${KC}/realms/master/protocol/openid-connect/token`, {
    form: { grant_type: 'password', client_id: 'admin-cli', username: process.env.KEYCLOAK_ADMIN ?? 'admin', password: process.env.KEYCLOAK_ADMIN_PASSWORD ?? 'admin' } })
  const { access_token } = await tk.json()
  await request.delete(`${KC}/admin/realms/sses/attack-detection/brute-force/users`, { headers: { Authorization: `Bearer ${access_token}` } })
}
