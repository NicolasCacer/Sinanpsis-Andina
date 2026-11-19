# 07 · Seguridad (RQ_003, RQ_005, RQ_026)

## Keycloak — realm `sses` (importado desde `infra/keycloak/realm-sses.json`)

- **Clientes**
  - `sses-web`: público, Authorization Code + **PKCE S256**, redirect `http://localhost:5173/*` y la URL HTTPS de la demo, web origins iguales.
  - `sses-api`: bearer-only / audiencia. Mapper de audiencia en `sses-web` para que el access token traiga `aud: sses-api`.
- **Tokens:** access 10 min (RS256), SSO session / refresh 8 h.
- **Roles de realm:** `ADMIN_FUNCIONAL, GESTOR_ESTRATEGIAS, TUTOR_LIDER, TUTOR_DOCENTE, ANALISTA_MEN, ENTE_CONTROL, COORDINADOR_PILOTO, SOPORTE` en `realm_access.roles`.
- **Atributos de usuario → claims:** `persona_id` (public_id de tutor/docente), `organizacion_id` (no se usa en MVP).
- **Política de contraseñas:** `length(12) and passwordHistory(5)`, hash Argon2 (disponible en Keycloak 26).
- **2FA:** OTP Policy TOTP, 6 dígitos, 30 s, HmacSHA1 (compatible con Google/Microsoft Authenticator). Flujo `browser` con OTP **requerido** para todos.
- **Fuerza bruta:** `bruteForceProtected: true`, `failureFactor: 5`, espera 15 min.
- **Eventos:** login events habilitados (LOGIN, LOGIN_ERROR) — útil para mostrarlo.
- **Usuarios demo con TOTP pre-enrolado**: importar credencial `otp` con un secreto conocido para no enrolar en vivo (ver `08-semilla-y-demo.md`). Verificar el formato exacto de `secretData`/`credentialData` contra Keycloak 26 antes de darlo por hecho; si falla, enrolar una vez antes de la demo y exportar el realm.
- Opcional (stretch): tema de login con marca Sinapsis Andina / SSES.

## API

- Valida firma, emisor, audiencia y expiración (`JwtBearer`, `RoleClaimType` mapeado desde `realm_access.roles` con un `IClaimsTransformation`).
- Políticas: `Gestor` (GESTOR_ESTRATEGIAS), `TutorLider` (TUTOR_LIDER), `TutorDocente` (TUTOR_DOCENTE).
- **Filtros por fila** siempre en la consulta, nunca en el front:
  - Gestor → solo estrategias cuya `entidad_id` esté en `seguridad.gestor_entidad` para su participante. Otra entidad → `404` (no filtrar existencia).
  - Tutor Líder → solo planes/hitos con `tutor_id` = su persona; docentes solo de `acompanamiento.asignacion` vigente.
- 403 ante rol no autorizado, con bitácora del intento (`ACCESO_DENEGADO`).
- CORS solo para el origen del front. Cabeceras: HSTS en el despliegue HTTPS, `X-Content-Type-Options`, CSP básica en nginx.

## Datos personales (Ley 1581 de 2012, Decreto 1377 de 2013 — datos de niños, niñas y adolescentes)

- Columnas cifradas en `maestros.estudiante`: tipo y número de documento, nombres, fecha de nacimiento → AES-256-GCM, nonce de 96 bits aleatorio por valor, formato `nonce||ciphertext||tag` en `bytea`.
- Búsqueda: `huella = HMAC-SHA-256(clave_hmac, upper(tipo) || ':' || numero_normalizado)`.
- Respuesta mínima: nombre visible, sede, municipio. Nunca devolver documento ni fecha de nacimiento.
- Claves en `.env` / user-secrets (`SSES__CLAVE_HMAC`, `SSES__CLAVE_AES`), **nunca** en el repo; `.env.example` con valores de ejemplo marcados como no productivos. En producción: Azure Key Vault con identidad administrada y rotación 90 días (decir esto en la demo).
- Solo datos **sintéticos** en todos los ambientes.
- La `BUSCAR_ESTUDIANTE` queda en bitácora (consulta de dato personal) con el public_id resultado, no con el documento buscado.

## Bitácora inmutable (RQ_005)

- Toda escritura + búsquedas de estudiantes + LOGIN + ACCESO_DENEGADO.
- Campos: fecha UTC, usuario, rol, IP, acción, entidad, id, antes/después JSONB, traceparent, hash_anterior, hash.
- Inmutable por permisos (rol `sses_api` sin UPDATE/DELETE) + cadena de hashes verificable (`auditoria.verificar_cadena()`).
- Momento demo: mostrar `GET /auditoria/verificacion` → OK; intentar `UPDATE auditoria.bitacora` como `sses_api` en psql → `permission denied`.
