import { test, expect } from '@playwright/test'
import { login } from './helpers'
import { execSync } from 'node:child_process'

const nombre = `Semillero ${Date.now().toString().slice(-6)}`

test('F3/F4 · crear, duplicado, editar, buscar, vincular y bloqueo visible y en BD', async ({ page }) => {
  await login(page, 'gestor.demo@sinapsis.co')
  await page.getByRole('navigation', { name: 'Menú principal' }).getByText('Estrategias').click()
  await page.getByRole('button', { name: 'Nueva estrategia' }).click()

  // crear
  await page.getByLabel('Entidad').selectOption({ index: 1 })
  await page.getByLabel('Nombre').fill(nombre)
  await page.getByLabel('Objetivo').fill('Fortalecer habilidades socioemocionales.')
  await page.getByLabel('Población').fill('Niños de 8 a 12 años')
  await page.getByRole('button', { name: 'Guardar', exact: true }).click()
  await expect(page.getByRole('heading', { name: nombre })).toBeVisible()
  await expect(page.getByTestId('estado-estrategia')).toHaveText('Editable')
  await expect(page.getByTestId('contador-vinculados')).toContainText('0')

  // duplicado en la misma entidad → error claro
  await page.goto('/estrategias/nueva')
  await page.getByLabel('Entidad').selectOption({ index: 1 })
  await page.getByLabel('Nombre').fill(nombre.toUpperCase())
  await page.getByLabel('Objetivo').fill('x'); await page.getByLabel('Población').fill('y')
  await page.getByRole('button', { name: 'Guardar', exact: true }).click()
  await expect(page.getByText('Ya existe una estrategia con ese nombre en la entidad.')).toBeVisible()

  // fecha fin <= inicio → error
  await page.getByLabel('Nombre').fill(nombre + ' bis')
  await page.getByLabel('Fecha de fin').fill('2020-01-01')
  await page.getByRole('button', { name: 'Guardar', exact: true }).click()
  await expect(page.getByText('La fecha de fin debe ser posterior a la de inicio.')).toBeVisible()

  // editar con 0 vinculados
  await page.goBack(); await page.goto('/estrategias')
  await page.getByTestId('fila-estrategia').filter({ hasText: nombre }).first().click()
  await page.getByRole('button', { name: 'Editar' }).click()
  await page.getByLabel('Población').fill('Niñas y niños de 8 a 14 años')
  await page.getByRole('button', { name: 'Guardar cambios' }).click()
  await expect(page.getByText('Niñas y niños de 8 a 14 años')).toBeVisible()

  // asociar estudiantes
  await page.getByRole('tab', { name: 'Estudiantes vinculados' }).click()
  await page.getByRole('button', { name: 'Asociar estudiantes' }).click()
  await page.getByLabel('Número de documento').fill('1001234567'); await page.getByRole('button', { name: 'Buscar' }).click()
  await expect(page.getByTestId('resultados-busqueda')).toContainText('Camila R.')
  await expect(page.getByTestId('tiempo-busqueda')).toBeVisible()
  await page.getByLabel('Número de documento').fill('1001234568'); await page.getByRole('button', { name: 'Buscar' }).click()
  await expect(page.getByTestId('resultados-busqueda').locator('li')).toHaveCount(2)
  await page.getByLabel('Número de documento').fill('9999999999'); await page.getByRole('button', { name: 'Buscar' }).click()
  await expect(page.getByText('No encontramos un estudiante con ese documento.')).toBeVisible()
  await page.getByRole('button', { name: 'Vincular' }).click()
  await expect(page.getByTestId('estado-estrategia')).toContainText('Bloqueada para edición')
  await expect(page.getByTestId('lista-vinculados').locator('li')).toHaveCount(2)
  await expect(page.getByTestId('contador-vinculados')).toContainText('2')
  await page.getByRole('tab', { name: 'Datos' }).click()
  await expect(page.getByRole('button', { name: 'Editar' })).toBeDisabled()
  await expect(page.getByTestId('aviso-bloqueada')).toBeVisible()

  // la BD también lo impide (trigger, P0001)
  const out = (() => { try { return execSync(`docker compose exec -T postgres psql -U postgres -d sses -c "update estrategias.estrategia set nombre='x' where nombre='${nombre}'" 2>&1`, { cwd: '../..' }).toString() } catch (e) { return String((e as { stdout?: Buffer }).stdout ?? e) } })()
  expect(out).toContain('ESTRATEGIA_BLOQUEADA')
})

import { tokenDe, API } from './helpers'

test('F4 · búsqueda por documento entre 500.000 registros: p95 < 300 ms', async ({ page, request }) => {
  await login(page, 'gestor.demo@sinapsis.co')
  const h = { Authorization: `Bearer ${await tokenDe(page)}` }
  const ms: number[] = []
  for (let i = 0; i < 30; i++) {
    const t0 = Date.now()
    const r = await request.get(`${API}/estudiantes?tipoDoc=TI&numDoc=1001234567`, { headers: h })
    ms.push(Date.now() - t0); expect(r.status()).toBe(200)
  }
  ms.sort((a, b) => a - b)
  console.log(`p50=${ms[15]}ms p95=${ms[28]}ms max=${ms[29]}ms`)
  expect(ms[28]).toBeLessThan(300)
})
