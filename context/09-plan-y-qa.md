# 09 · Plan de implementación y QA del MVP

Avanzar en orden. Cada fase termina cuando su criterio está **verificado** (comando ejecutado o prueba en verde), no cuando el código compila. Las fases 1–7 replican los Sprints 1–3 de la propuesta.

## Fases

### F0 · Esqueleto e infraestructura local
- `docker-compose.yml` (postgres 17, keycloak 26, api, web), `.env.example`, healthchecks.
- Solución .NET (`Sses.Api`, `Sses.Api.Tests`, `Sses.Seed`), proyecto Vite React TS con PWA, Tailwind.
- `/health/live`, `/health/ready`; página pública con 'Ingresar'.
- ✅ `docker compose up` deja todo sano; `curl /health/ready` = 200; el front carga.

### F1 · Datos, seed y auditoría (RQ_002, RQ_005, RQ_026)
- Esquemas, tablas, particiones, índices, grants, trigger de hash de bitácora, `verificar_cadena()`.
- `IProteccionDatos` (AES-GCM + HMAC) con pruebas unitarias.
- Seed con `COPY` por lotes; personajes y documentos conocidos.
- ✅ Seed de 500k en < 10 min; búsqueda por huella `EXPLAIN ANALYZE` < 50 ms; `UPDATE` en bitácora como `sses_api` → permission denied; `verificar_cadena()` OK.

### F2 · Acceso y roles (RQ_003, RQ_005)
- Realm importado (clientes, roles, OTP requerido, fuerza bruta, usuarios demo con TOTP).
- API: JwtBearer, transformación de roles, políticas, `/sesion/inicio`, `/me`, bitácora LOGIN y ACCESO_DENEGADO.
- Front: `oidc-client-ts`, rutas protegidas por rol, shell con menú y encabezado (nombre, rol, último ingreso).
- ✅ Login con TOTP funciona; menú distinto para gestor y tutor; endpoint de otro rol → 403 y queda en bitácora; 5 fallos bloquean.

### F3 · Estrategias (RQ_006, RQ_007)
- Endpoints de entidades y estrategias, ETag/If-Match, trigger de bloqueo, mapeo P0001 → 409.
- UI: listado keyset, ficha con tabs, crear/editar, aviso 'Bloqueada para edición'.
- ✅ Pruebas de integración (Testcontainers): crear, duplicado 409, fechas inválidas, editar con 0, editar con 1 → 409, versión vieja → 412, UPDATE directo en BD → P0001.

### F4 · Vinculación individual (RQ_008)
- Búsqueda por huella con `Server-Timing`; vinculación con ON CONFLICT; trigger de contador y bloqueo.
- UI: 'Asociar estudiantes', resultado, multi-selección, pestaña 'Estudiantes vinculados'. 'Cargar listado' deshabilitado.
- ✅ Doble vínculo no duplica; primer vínculo bloquea en la misma transacción; respuesta < 300 ms con el volumen sembrado.

### F5 · Plan mensual (RQ_010)
- Cálculo de semanas ISO del mes; individual vs grupal; UNIQUE (tutor, docente, mes); `GET /tutores/me/plan` con ETag.
- UI móvil-first: docentes por estrategia, formulario, calendario con cumplimiento.
- ✅ Plan grupal de 3 docentes y 3 semanas crea 1 plan + 3 hitos; repetir → 409; docente ajeno → 422.

### F6 · Seguimiento en línea (RQ_011)
- `POST /evidencias`, `PUT /hitos/{id}/seguimiento` con If-Match; cumplimiento recalculado.
- UI: formulario con asistencia, textos, fotos redimensionadas en el navegador.
- ✅ Registrar semana → 'Registrada' verde y cumplimiento correcto; imagen > 1 MB se reduce antes de subir.

### F7 · Offline y sincronización (RQ_012)
- SW con precache; Dexie + CryptoKey no extraíble; outbox; motor de sync; endpoint de sincronización con idempotencia y conflictos; pantalla de conflicto; indicador de conexión/pendientes.
- ✅ E2E Playwright con `context.setOffline(true)`: registrar offline → 'Pendiente' → online → 'Sincronizado'; reenvío del mismo lote → `DUPLICADO` sin filas nuevas; escenario de conflicto resuelto en ambos sentidos.

### F8 · Pulido de demo
- `reset-demo.sh`, datos de los personajes, panel de bitácora (últimos eventos + verificación), ítems de menú "Próximo sprint", responsive, estados vacíos y de error cuidados, textos literales revisados.
- Build de producción + túnel HTTPS + PWA instalada en celular.
- E2E del guion completo (`tests/e2e/demo.spec.ts`) en verde 3 veces seguidas tras `reset-demo.sh`.
- ✅ Ensayo cronometrado del guion de `08-semilla-y-demo.md` sin intervención manual.

### F9 · Stretch (en el orden de `02-alcance-mvp.md`)

## Pruebas (subconjunto de la Matriz de Pruebas §14.4 de la propuesta)

| Tipo | Herramienta | Qué cubre |
|---|---|---|
| Unitarias | xUnit, Vitest | Cifrado/HMAC, cálculo de semanas ISO, cumplimiento, validadores, cifrado local, lógica de outbox |
| Integración | xUnit + Testcontainers (PostgreSQL 17) + WebApplicationFactory | Endpoints, transacciones, triggers, RLS/filtros por fila, idempotencia, 409/412 |
| E2E | Playwright | Guion de la demo; offline con `setOffline`; móvil con `devices['Pixel 7']` |
| Rendimiento (ligero) | k6 o `EXPLAIN ANALYZE` | Búsqueda por documento p95 < 300 ms; listado de vinculados < 800 ms |
| Seguridad | Manual + pruebas | 403 por rol, aislamiento gestor/entidad y tutor/docente, bitácora inmutable, nada sensible en respuestas ni logs |

Casos imprescindibles (todos deben pasar antes de la presentación):

| ID | Relación | Caso | Esperado |
|---|---|---|---|
| CP_01 | RQ_003 | Login sin TOTP / TOTP errado ×5 | No entra / cuenta bloqueada 15 min |
| CP_02 | RQ_005 | Tutor llama `POST /estrategias` | 403 + bitácora `ACCESO_DENEGADO` |
| CP_03 | RQ_006 | Nombre duplicado en misma entidad (otra capitalización) | 409 `NOMBRE_DUPLICADO` |
| CP_04 | RQ_007 | Editar estrategia con 1 vinculado | 409 `ESTRATEGIA_BLOQUEADA`, UI deshabilitada |
| CP_05 | RQ_007 | Dos gestores: A abre edición, B vincula, A guarda | 409 |
| CP_06 | RQ_007 | UPDATE directo por SQL | P0001 |
| CP_07 | RQ_008 | Vincular mismo estudiante 2 veces | `yaVinculados: 1`, sin duplicado |
| CP_08 | RQ_008 | Respuesta de búsqueda | Solo nombre, sede, municipio |
| CP_09 | RQ_010 | Programar plan repetido mismo mes/docente | 409 `PLAN_DUPLICADO` |
| CP_10 | RQ_011 | Guardar con versión vieja | 412 |
| CP_11 | RQ_012 | Registro offline y reconexión | `Sincronizado`, 1 sola fila |
| CP_12 | RQ_012 | Reenvío del mismo lote | `DUPLICADO`, 0 filas nuevas |
| CP_13 | RQ_012 | Conflicto de versión | `CONFLICTO` con ambas versiones; resolución registrada en bitácora |
| CP_14 | RQ_026 | Revisar BD | Documento/nombres/fecha de nacimiento ilegibles en `maestros.estudiante` |
| CP_15 | RQ_005 | `verificar_cadena()` tras la demo | OK |
