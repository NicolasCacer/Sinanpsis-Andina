\c sses sses_owner
-- Mesa de ayuda del pilotaje (Línea B): RQ_020 / CU_013 / HU_011. Idempotente: se ejecuta al primer arranque
-- y también desde scripts/reset-demo.sh sobre bases ya creadas (D32).
create schema if not exists mesa;
create sequence if not exists mesa.consecutivo_incidente;

create table if not exists mesa.incidente (
  id bigint generated always as identity primary key,
  public_id uuid not null unique,
  numero text not null unique default ('INC-' || to_char(now() at time zone 'America/Bogota', 'YYYY') || '-' || lpad(nextval('mesa.consecutivo_incidente')::text, 6, '0')),
  participante_id bigint not null references seguridad.participante(id),
  rol text not null,
  tipo text not null check (tipo in ('BLOQUEANTE','FUNCIONAL','CONSULTA')),
  severidad text not null check (severidad in ('ALTA','MEDIA','BAJA')),
  estado text not null default 'ABIERTO' check (estado in ('ABIERTO','EN_ATENCION','RESUELTO','CERRADO')),
  origen text not null default 'USUARIO' check (origen in ('USUARIO','MONITOREO')),
  descripcion varchar(2000) not null,
  pantalla varchar(300), navegador varchar(500), traceparent varchar(100),
  creado_en timestamptz not null default now(),
  atendido_en timestamptz, resuelto_en timestamptz, cerrado_en timestamptz);
create index if not exists ix_incidente_participante on mesa.incidente (participante_id, id desc);

grant usage on schema mesa to sses_api;
grant select, insert on mesa.incidente to sses_api;
grant usage on sequence mesa.consecutivo_incidente to sses_api;
grant usage on schema mesa to sses_owner;

-- RQ_012: las claves de idempotencia se conservan 7 días; la API purga las vencidas
grant delete on acompanamiento.idempotencia to sses_api;
