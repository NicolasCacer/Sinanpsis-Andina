#!/usr/bin/env bash
# Deja la base en el estado inicial del guion de demo (no re-siembra estudiantes; la bitácora NO se borra: es inmutable).
set -euo pipefail
cd "$(dirname "$0")/.."
PSQL="docker compose exec -T postgres psql -U postgres -d sses -v ON_ERROR_STOP=1 -q"
$PSQL <<'SQL'
-- Transaccional del Tutor Líder demo (tutor_id = 1)
create temp table _planes as select id from acompanamiento.plan where tutor_id = 1;
create temp table _hitos  as select id from acompanamiento.hito_semanal where plan_id in (select id from _planes);
create temp table _segs   as select id from acompanamiento.seguimiento_semanal where hito_id in (select id from _hitos);
delete from acompanamiento.asistencia_seguimiento where seguimiento_id in (select id from _segs);
delete from acompanamiento.evidencia where seguimiento_id in (select id from _segs) or subido_por = (select id from seguridad.participante where rol = 'TUTOR_LIDER' and persona_id = 1);
delete from acompanamiento.seguimiento_semanal where id in (select id from _segs);
delete from acompanamiento.confirmacion_docente where hito_id in (select id from _hitos);
delete from acompanamiento.hito_semanal where id in (select id from _hitos);
delete from acompanamiento.plan_docente where plan_id in (select id from _planes);
delete from acompanamiento.plan where id in (select id from _planes);
delete from acompanamiento.idempotencia;
-- Estrategias creadas durante la demo (las precargadas son las 60 primeras)
delete from acompanamiento.asignacion where estrategia_id > 60;
delete from estrategias.estrategia_estudiante where estrategia_id > 60;
delete from estrategias.estrategia where id > 60;
SQL
rm -f data/evidencias/* 2>/dev/null || true
# Limpia bloqueos de fuerza bruta de Keycloak (si está arriba)
KC=${KC_URL:-http://localhost:8180}
T=$(curl -sf -d "grant_type=password&client_id=admin-cli&username=${KEYCLOAK_ADMIN:-admin}&password=${KEYCLOAK_ADMIN_PASSWORD:-admin}" "$KC/realms/master/protocol/openid-connect/token" 2>/dev/null | python3 -c 'import sys,json;print(json.load(sys.stdin)["access_token"])' 2>/dev/null || true)
[ -n "$T" ] && curl -sf -X DELETE -H "Authorization: Bearer $T" "$KC/admin/realms/sses/attack-detection/brute-force/users" >/dev/null || true
echo "Demo reiniciada."
