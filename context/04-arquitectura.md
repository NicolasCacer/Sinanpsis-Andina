# 04 · Arquitectura del MVP

## Vista de componentes (local, Docker Compose)

```
 Navegador (portátil / celular)
 └─ SSES Web (React 19 PWA)  ── Service Worker (Workbox 7) ── IndexedDB (Dexie 4, AES-GCM)
        │ HTTPS/OIDC (PKCE)                 │ REST JSON /api/v1 + Bearer JWT
        ▼                                   ▼
   Keycloak 26 (realm sses, TOTP) ◄─JWKS── Sses.Api (ASP.NET Core 10)
        │                                   │ Npgsql (TLS en prod)
        └──────────── PostgreSQL 17 ◄───────┘
                     ├─ db sses (maestros, seguridad, estrategias, acompanamiento, auditoria)
                     └─ db keycloak
   Volumen ./data/evidencias  (sustituye Blob Storage)
   .env / dotnet user-secrets (sustituye Key Vault)
```

Servicios de `docker-compose.yml`:

| Servicio | Imagen | Puerto | Notas |
|---|---|---|---|
| `postgres` | `postgres:17` | 5432 | init: crea dbs `sses` y `keycloak`, roles `sses_api` (app) y `sses_owner` (migraciones) |
| `keycloak` | `quay.io/keycloak/keycloak:26.x` | 8080 | `start-dev --import-realm`, monta `infra/keycloak/realm-sses.json` |
| `api` | build `backend/` | 5080 | aplica migraciones al arrancar en dev; `/health/live`, `/health/ready` |
| `web` | build `frontend/` (nginx con build) | 5173/443 | para la demo se sirve el build (el SW no funciona bien en `vite dev`) |

Para la demo en celular se necesita **HTTPS** (el service worker exige contexto seguro salvo `localhost`): usar un túnel HTTPS (p. ej. Cloudflare Tunnel) o desplegar. Ver `08-semilla-y-demo.md`.

## Módulos (alineados con el diagrama de clases de la propuesta, §12)

El diagrama de clases de la propuesta tiene estos módulos. El MVP implementa los marcados:

| Módulo de la propuesta | MVP | Dónde vive |
|---|---|---|
| Front-End / Portal SSES | ✅ | `frontend/` |
| Seguridad y Acceso | ✅ | Keycloak + `Sses.Api/Seguridad` |
| Gestión de Estrategias | ✅ | `Sses.Api/Estrategias` |
| Asociación de Estudiantes | ✅ (individual) | `Sses.Api/Estrategias/Vinculacion` |
| Datos Maestros y Participantes | ✅ solo lectura | `Sses.Api/Maestros` |
| Acompañamiento Pedagógico | ✅ | `Sses.Api/Acompanamiento` |
| Sincronización sin Conexión | ✅ | `frontend/src/offline` + `Sses.Api/Sincronizacion` |
| Auditoría | ✅ | `Sses.Api/Auditoria` + `auditoria.bitacora` |
| Base de Datos | ✅ | PostgreSQL |
| Analítica de Desempeño | stretch | — |
| Reportes de Ley | stretch | — |
| Directorio Institucional MEN | ❌ | — |
| Procesamiento Asíncrono, Notificaciones | ❌ | — |
| Gestión del Pilotaje, Mesa de Ayuda e Incidentes, Monitoreo y Observabilidad, Instrumentos de Percepción | ❌ | — |

En la presentación se puede mostrar el diagrama de clases con los módulos del MVP resaltados.

## Backend (Sses.Api)

- **Minimal APIs** agrupadas por módulo (`MapGroup("/api/v1/estrategias")`), una carpeta por módulo con endpoints, comandos/consultas, validadores (FluentValidation) y DTOs. Sin capas ceremoniales: es un MVP.
- **EF Core 10** para escritura transaccional; SQL directo (Dapper o `FromSql`) donde haga falta rendimiento (búsqueda por huella, listados keyset).
- Migraciones EF + scripts SQL en `infra/postgres/` para lo que EF no expresa (triggers, RLS, grants, particiones, `btree_gist` si aplica).
- **Autenticación:** `JwtBearer` contra Keycloak (issuer `http://keycloak:8080/realms/sses`, audiencia `sses-api`, JWKS en caché 10 min). Políticas por rol: `[Authorize(Policy="Gestor")]`, etc. Filtros por fila: gestor ↔ `seguridad.gestor_entidad`, tutor ↔ `acompanamiento.asignacion`.
- **Errores:** `ProblemDetails` (RFC 9457) con `code` de negocio (`ESTRATEGIA_BLOQUEADA`, `NOMBRE_DUPLICADO`, `PLAN_DUPLICADO`, `VERSION_CAMBIADA`…).
- **Concurrencia optimista:** `xmin` como token, expuesto como `ETag` (`W/"<xmin>"`), exigido en `If-Match` en PUT.
- **Auditoría:** middleware/servicio `IBitacora` que inserta en la misma transacción del comando. `traceparent` del request.
- **Cifrado:** servicio `IProteccionDatos` (AES-256-GCM por columna, HMAC-SHA-256 para huellas) con claves desde configuración (`SSES__CLAVE_HMAC`, `SSES__CLAVE_AES` en base64).
- **OpenAPI 3.1** en `/openapi/v1.json` + Scalar UI en dev (útil para mostrar el contrato si preguntan).
- **Health:** `/health/live` (proceso) y `/health/ready` (DB + Keycloak JWKS).

## Frontend

- Vite 7 + React 19 + TS estricto. Router: React Router 7 (data router). Estado de servidor: TanStack Query 5. Formularios: React Hook Form + Zod (las mismas reglas que FluentValidation).
- Estilos: Tailwind CSS 4, componentes propios accesibles (o shadcn/ui). Paleta Sinapsis Andina: azul andino profundo + verde de cumplimiento; nada de logos oficiales del Gobierno.
- Auth: `oidc-client-ts` (PKCE S256), tokens solo en memoria, renovación silenciosa con refresh token.
- PWA: `vite-plugin-pwa` (Workbox 7) con `injectManifest` para controlar el SW; detalle en `06-offline-sync.md`.
- División de código por ruta (`lazy()`); objetivo paquete inicial ≤ 300 KB comprimido (RQ_027, deseable).
- Móvil-first en Acompañamientos (el Tutor Líder está en campo); escritorio-first en Estrategias.

## Estructura de carpetas sugerida

```
backend/
  Sses.Api/
    Program.cs
    Comun/ (ProblemDetails, Etag, Paginacion, Auth policies, ProteccionDatos, Bitacora)
    Maestros/ Seguridad/ Estrategias/ Acompanamiento/ Sincronizacion/ Auditoria/
    Datos/ (SsesDbContext, entidades, configuraciones, Migraciones/)
  Sses.Api.Tests/ (xUnit + Testcontainers PostgreSQL)
tools/Sses.Seed/
frontend/
  src/ app/ (router, shell, auth) features/estrategias/ features/acompanamiento/ offline/ (db, crypto, outbox, sync) ui/
  src/sw.ts
infra/keycloak/realm-sses.json
infra/postgres/01-init.sql 02-triggers.sql 03-rls-grants.sql
tests/e2e/
scripts/reset-demo.sh
docker-compose.yml  .env.example
```
