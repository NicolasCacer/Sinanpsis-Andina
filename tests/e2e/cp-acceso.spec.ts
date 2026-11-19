import { test, expect } from '@playwright/test'
import { login } from './helpers'

test('F2 · gestor entra con TOTP y ve solo Estrategias', async ({ page }) => {
  await login(page, 'gestor.demo@sinapsis.co')
  await expect(page.getByTestId('encabezado-nombre')).toHaveText('Laura Gestora')
  await expect(page.getByTestId('encabezado-rol')).toHaveText('Gestor de Estrategias')
  const menu = page.getByRole('navigation', { name: 'Menú principal' })
  await expect(menu.getByText('Estrategias')).toBeVisible()
  await expect(menu.getByText('Acompañamientos')).toHaveCount(0)
})

test('F2 · tutor líder ve Acompañamientos, Pilotaje y Ayuda (deshabilitados)', async ({ page }) => {
  await login(page, 'tutor.lider@sinapsis.co')
  const menu = page.getByRole('navigation', { name: 'Menú principal' })
  await expect(menu.getByRole('link', { name: 'Acompañamientos' })).toBeVisible()
  await expect(menu.locator('[aria-disabled=true]', { hasText: 'Pilotaje' })).toBeVisible()
  await expect(menu.locator('[aria-disabled=true]', { hasText: 'Ayuda' })).toBeVisible()
  await expect(menu.getByText('Estrategias')).toHaveCount(0)
})

import { tokenDe, API, limpiarBloqueos, DEMO_PASSWORD, totp } from './helpers'

test('CP_02 · el tutor llama un endpoint de gestor → 403 y bitácora ACCESO_DENEGADO', async ({ page, request }) => {
  await login(page, 'tutor.lider@sinapsis.co')
  const token = await tokenDe(page)
  const h = { Authorization: `Bearer ${token}` }
  const r = await request.post(`${API}/estrategias`, { headers: h, data: {} })
  expect(r.status()).toBe(403)
  expect((await r.json()).code).toBe('ACCESO_DENEGADO')
  const rec = await (await request.get(`${API}/auditoria/recientes?limite=5`, { headers: h })).json()
  expect(rec.some((e: { accion: string }) => e.accion === 'ACCESO_DENEGADO')).toBeTruthy()
  expect(rec.some((e: { accion: string }) => e.accion === 'LOGIN')).toBeTruthy()
  const ver = await (await request.get(`${API}/auditoria/verificacion`, { headers: h })).json()
  expect(ver.ok).toBe(true)
  expect((await request.get(`${API}/me`)).status()).toBe(401)
})

test('CP_01 · TOTP errado ×5 bloquea la cuenta; sin TOTP no entra', async ({ page, request }) => {
  test.setTimeout(240_000)
  await limpiarBloqueos(request)
  // sin TOTP: el flujo se queda en el formulario de código
  await page.goto('/'); await page.getByRole('button', { name: 'Ingresar' }).click()
  await page.locator('#username').fill('admin@sinapsis.co'); await page.locator('#password').fill(DEMO_PASSWORD); await page.locator('#kc-login').click()
  await expect(page.locator('#otp')).toBeVisible()
  await page.goto('/inicio'); await expect(page.getByTestId('encabezado-nombre')).toHaveCount(0)
  // 5 códigos errados
  for (let i = 0; i < 6; i++) {
    await page.context().clearCookies()
    await page.goto('/'); await page.getByRole('button', { name: 'Ingresar' }).click()
    await page.locator('#username').fill('admin@sinapsis.co'); await page.locator('#password').fill(DEMO_PASSWORD); await page.locator('#kc-login').click()
    if (!(await page.locator('#otp').isVisible().catch(() => false))) break
    await page.locator('#otp').fill('000000'); await page.locator('#kc-login').click()
  }
  // con el código correcto ya no entra: cuenta bloqueada temporalmente
  await page.context().clearCookies()
  await page.goto('/'); await page.getByRole('button', { name: 'Ingresar' }).click()
  await page.locator('#username').fill('admin@sinapsis.co'); await page.locator('#password').fill(DEMO_PASSWORD); await page.locator('#kc-login').click()
  if (await page.locator('#otp').isVisible().catch(() => false)) { await page.locator('#otp').fill(totp()); await page.locator('#kc-login').click() }
  await expect(page).not.toHaveURL(/\/inicio/)
  await expect(page.getByTestId('encabezado-nombre')).toHaveCount(0)
  await limpiarBloqueos(request)
})
