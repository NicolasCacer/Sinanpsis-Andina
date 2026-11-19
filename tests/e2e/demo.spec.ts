// E2E del guion de la demo (context/08-semilla-y-demo.md). Debe pasar 3 veces seguidas tras scripts/reset-demo.sh.
import { test, expect, devices } from '@playwright/test'
import { execSync } from 'node:child_process'
import { login, programarPlanDemo, registrarSemana, tokenDe, API, sql, mesActual, resetDemo } from './helpers'

const nombre = `Semillero Demo ${Date.now().toString().slice(-5)}`

test.describe.serial('Demo en vivo', () => {
  test.beforeAll(() => { resetDemo() })

  test('1-4 · Laura (Gestora): TOTP, estrategia, búsqueda entre 500.000, bloqueo visible y en la BD', async ({ page }) => {
    // 1. acceso con segundo factor, menú por rol y encabezado
    await login(page, 'gestor.demo@sinapsis.co')
    await expect(page.getByTestId('encabezado-nombre')).toHaveText('Laura Gestora')
    await expect(page.getByTestId('encabezado-ultimo')).toBeVisible()
    await expect(page.getByText('queda registrado a tu nombre')).toBeVisible()
    await page.getByRole('navigation', { name: 'Menú principal' }).getByRole('link', { name: 'Estrategias' }).click()

    // 2. nueva estrategia en una entidad del Pacífico; nombre duplicado → error; guardar → Editable; editar → OK
    await page.getByRole('button', { name: 'Nueva estrategia' }).click()
    await page.getByLabel('Entidad').selectOption({ label: 'Fundación Semillas del Pacífico' })
    await page.getByLabel('Nombre').fill('semillero de robótica pacífico') // ya existe (otra capitalización)
    await page.getByLabel('Objetivo').fill('Fortalecer el pensamiento computacional en estudiantes de Tumaco.')
    await page.getByLabel('Población').fill('Estudiantes de 10 a 14 años')
    await page.getByRole('button', { name: 'Guardar', exact: true }).click()
    await expect(page.getByText('Ya existe una estrategia con ese nombre en la entidad.')).toBeVisible()
    await page.getByLabel('Nombre').fill(nombre)
    await page.getByRole('button', { name: 'Guardar', exact: true }).click()
    await expect(page.getByRole('heading', { name: nombre })).toBeVisible()
    await expect(page.getByTestId('estado-estrategia')).toHaveText('Editable')
    await expect(page.getByTestId('contador-vinculados')).toContainText('0')
    await page.getByRole('button', { name: 'Editar' }).click()
    await page.getByLabel('Población').fill('Estudiantes de 10 a 15 años')
    await page.getByRole('button', { name: 'Guardar cambios' }).click()
    await expect(page.getByText('Estudiantes de 10 a 15 años')).toBeVisible()

    // 3. buscar TI 1001234567 entre 500.000 y vincular 2 → 'Bloqueada para edición'
    await page.getByRole('tab', { name: 'Estudiantes vinculados' }).click()
    await expect(page.getByRole('button', { name: /Cargar listado/ })).toBeDisabled()
    await page.getByRole('button', { name: 'Asociar estudiantes' }).click()
    await page.getByLabel('Número de documento').fill('1001234567'); await page.getByRole('button', { name: 'Buscar' }).click()
    await expect(page.getByTestId('resultados-busqueda')).toContainText('Camila R.')
    const t = await page.getByTestId('tiempo-busqueda').innerText(); console.log('   ' + t)
    expect(Number(/(\d+) ms/.exec(t)![1])).toBeLessThan(300)
    await page.getByLabel('Número de documento').fill('1001234568'); await page.getByRole('button', { name: 'Buscar' }).click()
    await expect(page.getByTestId('resultados-busqueda').locator('li')).toHaveCount(2)
    await page.getByLabel('Número de documento').fill('9999999999'); await page.getByRole('button', { name: 'Buscar' }).click()
    await expect(page.getByText('No encontramos un estudiante con ese documento.')).toBeVisible()
    await page.getByRole('button', { name: 'Vincular' }).click()
    await expect(page.getByTestId('estado-estrategia')).toContainText('Bloqueada para edición')
    await expect(page.getByTestId('lista-vinculados').locator('li')).toHaveCount(2)
    await page.getByRole('tab', { name: 'Datos' }).click()
    await expect(page.getByRole('button', { name: 'Editar' })).toBeDisabled()

    // 4. ni siquiera con acceso a la BD se puede alterar una estrategia en ejecución
    const out = (() => { try { return execSync(`docker compose exec -T postgres psql -U postgres -d sses -c "update estrategias.estrategia set nombre='x' where nombre='${nombre}'" 2>&1`, { cwd: '../..' }).toString() } catch (e) { return String((e as { stdout?: Buffer }).stdout ?? e) } })()
    expect(out).toContain('ESTRATEGIA_BLOQUEADA')
  })

  test('5-8 · Andrés (Tutor Líder, celular): plan grupal, modo avión, sincronización, conflicto y bitácora', async ({ browser, request }) => {
    test.setTimeout(240_000)
    const ctx = await browser.newContext({ ...devices['Pixel 7'], locale: 'es-CO' })
    const page = await ctx.newPage()
    try {
      await login(page, 'tutor.lider@sinapsis.co')
      // 5. plan mensual grupal, 3 docentes, semanas 1-3 → calendario con 0 %
      await programarPlanDemo(page, [1, 2, 3])
      await expect(page.getByTestId('cumplimiento-porcentaje')).toHaveText('0 %')
      await page.waitForFunction(() => !!navigator.serviceWorker.controller, undefined, { timeout: 30_000 })
      const h = { Authorization: `Bearer ${await tokenDe(page)}` }

      // 6. modo avión: la app abre; registrar la semana 1 con asistencia y foto → 'Pendiente'; volver la red → 'Sincronizado', 33 %
      await ctx.setOffline(true); await page.reload()
      await expect(page.getByTestId('aviso-sin-conexion')).toHaveText('Sin conexión: tus registros se guardarán en este dispositivo')
      await registrarSemana(page, 1, 'Taller de planeación con los docentes de Tumaco', true)
      await expect(page.getByTestId('hito').nth(0).getByTestId('estado-registro')).toHaveText('Pendiente')
      // 7. mientras tanto otra ventana registra la semana 2 en línea → conflicto
      await registrarSemana(page, 2, 'Semana 2 desde el celular sin señal')
      const plan = await (await request.get(`${API}/tutores/me/plan?mes=${mesActual()}`, { headers: h })).json()
      const hitos = plan.planes[0].hitos as { id: string; version: number }[]; const docs = plan.planes[0].docentes as { id: string }[]
      const r = await request.put(`${API}/hitos/${hitos[1].id}/seguimiento`, { headers: { ...h, 'If-Match': `W/"${hitos[1].version}"` },
        data: { asistencia: docs.map((d) => ({ docenteId: d.id, asistio: true })), actividades: 'Semana 2 desde la oficina', avances: 'registro en línea', evidenciaIds: [] } })
      expect(r.status()).toBe(200)
      await ctx.setOffline(false)
      await expect(page.getByTestId('hito').nth(0).getByTestId('estado-registro')).toHaveText('Sincronizado', { timeout: 30_000 })
      await expect(page.getByTestId('hito').nth(1).getByTestId('estado-registro')).toHaveText('En conflicto')
      await expect(page.getByTestId('cumplimiento-porcentaje')).toHaveText('67 %') // 2 de 3 registradas en el servidor (semana 1 sincronizada + semana 2 del otro dispositivo)
      await page.getByTestId('hito').nth(1).getByRole('link', { name: 'Resolver conflicto' }).click()
      await expect(page.getByTestId('version-local')).toContainText('celular sin señal')
      await expect(page.getByTestId('version-servidor')).toContainText('desde la oficina')
      await page.getByRole('button', { name: 'Conservar el mío' }).click()
      await expect(page.getByTestId('hito').nth(1).getByTestId('estado-registro')).toHaveText('Sincronizado', { timeout: 30_000 })

      // 8. bitácora: últimos eventos y cadena íntegra
      await page.goto('/auditoria')
      await expect(page.getByTestId('eventos').locator('li').first()).toBeVisible()
      await page.getByRole('button', { name: 'Verificar cadena' }).click()
      await expect(page.getByTestId('cadena-ok')).toHaveText('Cadena íntegra ✓')
      expect(sql('select ok from auditoria.verificar_cadena()')).toBe('t')
    } finally { await ctx.close() }
  })
})
