import { test, expect } from '@playwright/test'
import { login, programarPlanDemo, adjuntarFotoPesada, resetDemo } from './helpers'
import { execSync } from 'node:child_process'

test.beforeAll(() => { resetDemo() })

test('F6 · registrar la semana 1 en línea con una foto pesada que se reduce a ≤ 1 MB', async ({ page }) => {
  await login(page, 'tutor.lider@sinapsis.co')
  await programarPlanDemo(page)
  await page.getByTestId('calendario').getByTestId('hito').first().getByRole('button', { name: 'Registrar seguimiento' }).click()
  await expect(page.getByRole('heading', { name: 'Registrar seguimiento' })).toBeVisible()
  await page.getByLabel(/Asistió Marina/).check()
  await page.getByLabel('Actividades').fill('Taller de planeación con los docentes.')
  await page.getByLabel('Avances').fill('Los docentes presentaron su primera secuencia didáctica.')
  const original = await adjuntarFotoPesada(page)
  await expect(page.getByTestId('peso-foto')).toBeVisible({ timeout: 60_000 })
  const reducido = parseInt((await page.getByTestId('peso-foto').innerText()), 10) * 1024
  console.log(`foto original ${(original / 1024 / 1024).toFixed(1)} MB → ${(reducido / 1024).toFixed(0)} KB`)
  expect(original).toBeGreaterThan(1_048_576); expect(reducido).toBeLessThanOrEqual(1_048_576)
  await page.getByRole('button', { name: 'Guardar', exact: true }).click()
  await expect(page.getByTestId('aviso-registrada')).toBeVisible()
  const hito1 = page.getByTestId('hito').first()
  await expect(hito1).toHaveAttribute('data-estado', 'REGISTRADA')
  await expect(hito1.getByTestId('estado-hito')).toHaveText('Registrada')
  await expect(page.getByTestId('cumplimiento-porcentaje')).toHaveText('33 %')
  await expect(page.getByTestId('cumplimiento-detalle')).toHaveText('1 de 3 semanas registradas')
  // lo que llegó al servidor pesa ≤ 1 MB
  const bytes = Number(execSync(`docker compose exec -T postgres psql -U postgres -d sses -Atc "select max(bytes) from acompanamiento.evidencia"`, { cwd: '../..' }).toString().trim())
  expect(bytes).toBeGreaterThan(0); expect(bytes).toBeLessThanOrEqual(1_048_576)
})
