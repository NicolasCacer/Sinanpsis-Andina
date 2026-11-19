\c sses sses_owner
-- Mínimo privilegio para la API (RQ_005, RQ_026). Bitácora: solo INSERT y SELECT.
grant usage on schema maestros, seguridad, estrategias, acompanamiento, auditoria to sses_api;
grant select on all tables in schema maestros to sses_api;           -- solo lectura (maestros del Ministerio)
grant select on all tables in schema seguridad to sses_api;
grant update (ultimo_ingreso, ingreso_actual) on seguridad.participante to sses_api;
grant select, insert, update on estrategias.estrategia to sses_api;
grant select, insert on estrategias.estrategia_estudiante to sses_api;
grant select on acompanamiento.asignacion to sses_api;
grant select, insert, update on acompanamiento.plan, acompanamiento.plan_docente, acompanamiento.hito_semanal,
  acompanamiento.seguimiento_semanal, acompanamiento.asistencia_seguimiento, acompanamiento.evidencia,
  acompanamiento.idempotencia to sses_api;
grant select, insert on acompanamiento.confirmacion_docente to sses_api;
grant delete on acompanamiento.asistencia_seguimiento, acompanamiento.evidencia to sses_api;
grant select, insert on auditoria.bitacora to sses_api;
grant execute on function auditoria.verificar_cadena() to sses_api;
grant usage on all sequences in schema estrategias, acompanamiento, auditoria to sses_api;
-- sses_owner: migraciones / seed
grant all privileges on all tables in schema maestros, seguridad, estrategias, acompanamiento to sses_owner;
grant usage on schema maestros, seguridad, estrategias, acompanamiento, auditoria to sses_owner;
grant usage on all sequences in schema maestros, seguridad, estrategias, acompanamiento to sses_owner;
grant select, insert on auditoria.bitacora to sses_owner;
