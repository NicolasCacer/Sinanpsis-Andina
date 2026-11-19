import { test, expect } from '@playwright/test'
import { login, sql } from './helpers'

// CP_023 / RQ_020: incidente desde el botón 'Ayuda' con captura de contexto (Línea B: soporte en tiempo real)
test('CP_023 · el tutor reporta un inconveniente desde Ayuda y lo ve en Mis casos', async ({ page }) => {
  await login(page, 'tutor.lider@sinapsis.co')
  await page.goto('/acompanamientos')
  await page.getByTestId('boton-ayuda').click()
  await page.getByLabel('Tipo de inconveniente').selectOption('BLOQUEANTE')
  await page.getByLabel('¿Qué pasó?').fill('No carga la foto de evidencia')
  await page.getByRole('button', { name: 'Enviar' }).click()
  const numero = (await page.getByTestId('numero-caso').textContent())!.trim()
  expect(numero).toMatch(/^INC-\d{4}-\d{6}$/)
  expect(sql(`select severidad || '|' || pantalla || '|' || (traceparent is not null) from mesa.incidente where numero='${numero}'`)).toBe('ALTA|/acompanamientos|t')
  await page.getByRole('link', { name: 'Mis casos' }).click()
  await expect(page.getByTestId('lista-casos')).toContainText(numero)
  await expect(page.getByTestId('lista-casos')).toContainText('Abierto')
})

test('CP_023 · el botón Ayuda también está para el gestor', async ({ page }) => {
  await login(page, 'gestor.demo@sinapsis.co')
  await expect(page.getByTestId('boton-ayuda')).toBeVisible()
})
