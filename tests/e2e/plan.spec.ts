import { test, expect } from '@playwright/test'
import { login } from './helpers'
import { execSync } from 'node:child_process'

test.beforeAll(() => { execSync('./scripts/reset-demo.sh', { cwd: '../..' }) })

test('F5 · el tutor líder programa un plan grupal (3 docentes, semanas 1-3) y ve cumplimiento 0 %', async ({ page }) => {
  await login(page, 'tutor.lider@sinapsis.co')
  await page.getByRole('navigation', { name: 'Menú principal' }).getByRole('link', { name: 'Acompañamientos' }).click()
  await expect(page.getByRole('heading', { name: 'Plan mensual' })).toBeVisible()
  const form = page.getByTestId('form-programar')
  await expect(form.getByText('Semillero de Robótica Pacífico').or(form.getByText(/Lectura|Ajedrez|Danza|Huertas|Fútbol/)).first()).toBeVisible()
  await expect(form.getByText('Marina Docente')).toBeVisible()
  await form.getByRole('checkbox').nth(0).check(); await form.getByRole('checkbox').nth(1).check(); await form.getByRole('checkbox').nth(2).check()
  await form.getByLabel('Temas').fill('Planeación de clases y evaluación formativa')
  for (const n of [1, 2, 3]) await form.getByLabel(`Semana ${n}`).check({ force: true })
  await form.getByRole('button', { name: 'Programar' }).click()
  await expect(page.getByTestId('calendario').getByTestId('hito')).toHaveCount(3)
  await expect(page.getByTestId('calendario').getByText('Programada').first()).toBeVisible()
  await expect(page.getByTestId('cumplimiento-porcentaje')).toHaveText('0 %')
  // repetir el mismo plan → 409 con mensaje claro
  await form.getByRole('checkbox').nth(0).check(); await form.getByRole('checkbox').nth(1).check()
  await form.getByLabel('Temas').fill('Otro'); await form.getByLabel('Semana 1').check({ force: true })
  await form.getByRole('button', { name: 'Programar' }).click()
  await expect(page.getByText(/Ya existe un plan de/)).toBeVisible()
})
