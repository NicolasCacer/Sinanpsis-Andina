// Muestra el código TOTP (6 dígitos, 30 s) de los usuarios demo. Uso: node scripts/codigo-totp.mjs
// Equivale a la app de autenticación del celular; el secreto es DEMO_TOTP_SECRET de .env (solo datos de demo).
import { createHmac } from 'node:crypto'
import { readFileSync } from 'node:fs'

let secreto = process.env.DEMO_TOTP_SECRET
try { secreto ??= readFileSync(new URL('../.env', import.meta.url), 'utf8').match(/^DEMO_TOTP_SECRET=(\S+)/m)?.[1] } catch { /* sin .env */ }
secreto ??= 'KNUW4YLQONUXGQLOMRUW4YKEMVWW6MBR'

const alfabeto = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
let bits = ''
for (const c of secreto.replace(/=+$/, '').toUpperCase()) bits += alfabeto.indexOf(c).toString(2).padStart(5, '0')
const clave = Buffer.from(bits.match(/.{8}/g).map((b) => parseInt(b, 2)))

const ahora = Math.floor(Date.now() / 1000)
const contador = Buffer.alloc(8); contador.writeBigUInt64BE(BigInt(Math.floor(ahora / 30)))
const h = createHmac('sha1', clave).update(contador).digest()
const o = h[h.length - 1] & 0xf
const codigo = ((h.readUInt32BE(o) & 0x7fffffff) % 1_000_000).toString().padStart(6, '0')
console.log(`Código: ${codigo}   (vence en ${30 - (ahora % 30)} s)`)
