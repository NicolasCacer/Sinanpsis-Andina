import { test, expect } from '@playwright/test'
import { sql } from './helpers'

test('CP_14 · en la BD el documento, nombres y fecha de nacimiento no son legibles', () => {
  const fila = sql(`select encode(tipo_documento_cif,'escape')||'|'||encode(numero_documento_cif,'escape')||'|'||encode(nombres_cif,'escape')||'|'||encode(fecha_nacimiento_cif,'escape') from maestros.estudiante where id=1`)
  expect(fila).not.toContain('1001234567'); expect(fila).not.toContain('Camila'); expect(fila).not.toContain('Rojas')
  expect(sql(`select length(numero_documento_cif) from maestros.estudiante where id=1`)).toBe(String(12 + 10 + 16)) // nonce + texto + tag (AES-256-GCM)
  expect(sql(`select count(*) from maestros.estudiante_huella where huella = '\\x00'`)).toBe('0')
})

test('RQ_005 · sses_api no puede modificar ni borrar la bitácora', () => {
  const run = (q: string) => { try { return sql(q.replace(/"/g, '\\"')) } catch (e) { return String((e as { stderr?: Buffer }).stderr ?? e) } }
  const r = (q: string) => run(`set role sses_api; ${q}`)
  expect(r(`update auditoria.bitacora set usuario='x'`)).toContain('permission denied')
  expect(r(`delete from auditoria.bitacora`)).toContain('permission denied')
})
