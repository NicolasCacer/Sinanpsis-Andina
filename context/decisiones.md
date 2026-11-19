# Decisiones y desviaciones respecto a la propuesta

Formato: decisión · por qué · qué decir si el jurado pregunta. Agregar aquí cualquier nueva desviación.

| # | Decisión | Por qué | Respuesta ante el jurado |
|---|---|---|---|
| D01 | Se mantiene el stack de la propuesta (React 19 + Vite PWA, ASP.NET Core 10, Keycloak 26, PostgreSQL 17) | Coherencia total con el documento entregado; el jurado evalúa la propuesta + MVP como un todo | "El MVP está construido con la arquitectura de referencia de la sección 10." |
| D02 | Azure se sustituye localmente: Key Vault → `.env`/user-secrets; Blob Storage → volumen `./data/evidencias`; Service Bus → no se usa (no hay procesos asíncronos en el MVP); Front Door/WAF → nginx | El MVP corre en el ambiente de Desarrollo; los servicios gestionados de Azure son de QA/Producción (DEP_002, DEP_003) | "El MVP corre en el ambiente de Desarrollo; en QA y Producción se despliega sobre Azure como muestran los diagramas de despliegue." |
| D03 | Particiones de `seguimiento_semanal` y `bitacora` declarativas creadas por script, sin pg_partman/pg_cron | La imagen oficial de Postgres no trae pg_partman; para la demo basta con las particiones del periodo | "En producción pg_partman las crea automáticamente (RQ_002)." |
| D04 | Índice de huella en tabla aparte `maestros.estudiante_huella` | En una tabla particionada por HASH(id) no puede haber UNIQUE global sobre otra columna | Explicación técnica directa |
| D05 | Cola de salida propia en IndexedDB en lugar de depender solo de Workbox Background Sync | Background Sync solo existe en Chromium y no expone estado por registro ni maneja 409 con ambas versiones; se usa como disparador adicional | "Funciona igual en Safari/iOS y el tutor ve el estado de cada registro." |
| D06 | Clave de cifrado local = `CryptoKey` no extraíble guardada en IndexedDB | Con tokens solo en memoria, una recarga sin red perdería la clave derivada de sesión; un CryptoKey no extraíble cumple "no se persiste en claro" | "La clave nunca puede leerse ni exportarse, ni siquiera desde la consola." |
| D07 | Evidencias por `POST /evidencias` multipart a la API en lugar de URL SAS directa a Blob | Sin Blob Storage en local; el contrato del front queda igual de simple | "En QA/Prod se cambia a URL SAS de 15 minutos (RQ_011) sin tocar la UI." |
| D08 | Usuarios demo creados en el realm importado, sin UI de habilitación | RQ_001 es prioridad Media, fuera de la ruta crítica y del MVP del Sprint 3 | "La habilitación de participantes está planeada para las semanas 4–5 en paralelo." |
| D09 | Volumen sembrado por defecto 500.000 estudiantes (no 10 M) | Tiempo de seed y recursos del portátil; el diseño (particiones, índices, keyset) es el mismo | "El modelo está dimensionado para 10 M; en QA se certifica con 10 M de registros sintéticos." |
| D10 | Puertos del host distintos: Postgres 5433, Keycloak 8180, web 5174 (API 5080) | 5432, 8080 y 5173 estaban ocupados en el portátil de desarrollo; el issuer de tokens es `http://localhost:8180/realms/sses` (backchannel dinámico para la API dentro de Docker) | Detalle de entorno local, no de arquitectura |
| D11 | El esquema se crea con scripts SQL (`infra/postgres/*.sql`, ejecutados al primer arranque) y no con migraciones EF; EF Core solo mapea | Particiones, triggers, grants y RLS no los expresa EF; el script es la fuente única y reproducible | "El esquema es versionado en SQL; en QA/Prod lo aplica el pipeline con el rol `sses_owner`." |
| D12 | SDK de .NET 10 instalado en `~/.dotnet` (no hay dotnet del sistema) | Entorno de desarrollo | — |
| D13 | Acceso a datos con Npgsql + Dapper (SQL explícito); EF Core no se usa | Las reglas críticas (FOR UPDATE, xmin, ON CONFLICT, keyset, particiones) se expresan mejor y se demuestran más claras en SQL; el esquema ya vive en scripts (D11) | "El SQL es auditable línea a línea; EF Core se puede añadir para el resto de módulos sin tocar esto." |
| D14 | Semana "ISO del mes" = semana ISO (lun–dom) cuyo **jueves** cae en el mes (4 o 5 semanas por mes, numeradas 1..5) | Regla ISO 8601 estándar, evita meses de 6 semanas parciales | Se aplica la regla del jueves de ISO 8601. |
| D15 | Tablas con UNIQUE sobre `huella`/`public_id` del estudiante viven en `estudiante_huella` (incluye `public_id` del estudiante) | Mismo motivo que D04 | Ver D04 |

