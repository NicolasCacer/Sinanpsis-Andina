# 06 · Trabajo sin conexión y sincronización (RQ_012, RQ_027)

Es **el diferenciador de la demo**: el pliego valora la conectividad limitada y la propuesta promete que "el trabajo de campo no se pierde ni se duplica". Tiene que funcionar perfecto.

## Piezas

| Pieza | Implementación MVP |
|---|---|
| App shell offline | `vite-plugin-pwa` (`injectManifest`), precache del build; `navigateFallback` a `index.html`. La app abre sin red. |
| Datos del plan offline | Al abrir 'Plan mensual' en línea: `GET /tutores/me/plan?mes=` → se guarda en Dexie (`planes`), con su ETag. Estrategia NetworkFirst con fallback a IndexedDB. Se precargan el mes actual y el siguiente al iniciar sesión. |
| Almacenamiento local | Dexie 4: tablas `planes`, `outbox`, `evidenciasLocales`, `conflictos`, `meta`. |
| Cifrado local | AES-GCM 256 con Web Crypto. Al iniciar sesión se genera una `CryptoKey` **no extraíble** y se guarda el objeto `CryptoKey` en IndexedDB (no es exportable, nunca está en claro). Se cifran los campos sensibles (`actividades`, `avances`, asistencia, imágenes). Al cerrar sesión se borra la clave y los datos cifrados quedan ilegibles. |
| Cola de salida | Tabla `outbox`: `{idempotencyKey (uuid v7), hitoId, versionBase, payloadCifrado, estado: Pendiente|Sincronizado|EnConflicto|Rechazado, intentos, creadoEn}`. |
| Disparo de sync | Evento `online` + al recuperar foco + al iniciar la app + botón manual 'Sincronizar ahora'. Si el navegador soporta Background Sync, registrar `sync` tag `sses-outbox` en el SW. Reintento con backoff 2-4-8-16 s. |
| Indicador | Badge en el encabezado: punto verde/gris de conexión + "N pendientes". Aviso persistente 'Sin conexión: tus registros se guardarán en este dispositivo'. |

> Por qué cola propia y no solo `BackgroundSyncPlugin` de Workbox: necesitamos estado por registro visible en UI y manejo de 409 con ambas versiones. Background Sync se usa solo como disparador adicional. (Registrado en `decisiones.md`.)

## Flujo de guardado

1. El formulario 'Registrar seguimiento' valida con Zod.
2. Las fotos se redimensionan con `<canvas>`/`createImageBitmap` a 1.280 px lado mayor, WebP calidad 0,7; si > 1 MB se baja calidad.
3. Si `navigator.onLine` y la petición responde en < 8 s → `PUT /hitos/{id}/seguimiento` normal (evidencias por `POST /evidencias` antes).
4. Si no hay red, o la petición falla por red/timeout → se cifra y se guarda en `outbox` como `Pendiente`; el hito se marca localmente como "Registrada (pendiente)" con estilo distinto (verde con reloj) y el cumplimiento local se recalcula de forma optimista.

## Flujo de sincronización

- Tomar `outbox` con estado `Pendiente`, en orden de `creadoEn`, lotes de máx. 50.
- Renovar token si expiró (refresh token de 8 h). Si no se puede renovar → pedir ingreso de nuevo **sin borrar la cola**.
- `POST /sincronizacion/seguimientos` con cabecera `Idempotency-Key` por ítem en el cuerpo.
- Servidor, por ítem, en su propia transacción:
  1. Si `idempotencia.clave` existe → devolver la respuesta guardada con estado `DUPLICADO` (sin crear filas).
  2. Si `capturadoEn` > 30 días → `RECHAZADO`.
  3. Si `xmin` actual del hito ≠ `versionBase` **y** el hito ya está `REGISTRADA` → `CONFLICTO` con `versionServidor` y `registroServidor`.
  4. Si no → aplicar como en RQ_011 (origen `SIN_CONEXION`), guardar evidencias, recalcular cumplimiento, bitácora `SINCRONIZAR`, guardar respuesta en `idempotencia` → `APLICADO`.
- Cliente: `APLICADO`/`DUPLICADO` → `Sincronizado` (y refrescar el plan del mes); `CONFLICTO` → `En conflicto`, copiar a tabla `conflictos`; `RECHAZADO` → mostrar motivo.

## Resolución de conflictos

Pantalla lado a lado: "En este dispositivo" vs "En el servidor" (quién y cuándo registró). Opciones:
- **Conservar el del servidor** → se descarta el local, estado `Sincronizado`.
- **Conservar el mío** → se reenvía como nuevo ítem con `versionBase` = versión del servidor y nueva idempotencyKey; el servidor sobrescribe y la bitácora guarda `antes`/`despues` con acción `CONFLICTO_RESUELTO`.

## Cómo se demuestra (ver guion)

- Chrome DevTools → Network → **Offline** (portátil), o modo avión en el celular con la PWA instalada.
- Escenario de conflicto: el mismo Tutor Líder con dos dispositivos (o una ventana de incógnito), uno offline y otro online registrando la misma semana.
- Script `scripts/reset-demo.sh` limpia el estado del servidor; botón oculto en la UI (`/debug`, solo en build de demo) borra IndexedDB.

## Metas (de la propuesta, para mencionar)

- Sincronizar 20 pendientes en ≤ 60 s con 3G 1,6 Mbps (probar con throttling "Slow 3G" en DevTools).
- ≥ 95 % de registros sin conexión sincronizados sin pérdida (criterio de viabilidad del pilotaje).
