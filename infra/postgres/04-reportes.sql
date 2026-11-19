\c sses sses_owner
-- HU_008 / CU_009 / RQ_014: reportes de ley con huella SHA-256 y verificación pública.
-- RQ_001: cada emisión queda marcada con su organización de control y la política RLS impide que un ente lea las de otro.
create schema if not exists reportes;

create table reportes.organizacion_control (
  id bigint generated always as identity primary key,
  public_id uuid not null unique default gen_random_uuid(),
  nombre text not null unique);

create table reportes.plantilla (
  id bigint generated always as identity primary key,
  public_id uuid not null unique default gen_random_uuid(),
  codigo text not null unique,            -- la API mapea el código a una consulta fija (sin SQL libre en la BD)
  nombre text not null, descripcion text not null);

create table reportes.plantilla_organizacion (
  organizacion_id bigint not null references reportes.organizacion_control(id),
  plantilla_id bigint not null references reportes.plantilla(id),
  primary key (organizacion_id, plantilla_id));

create table reportes.emision (
  id bigint generated always as identity primary key,
  public_id uuid not null unique default gen_random_uuid(),
  organizacion_id bigint not null references reportes.organizacion_control(id),
  plantilla_id bigint not null references reportes.plantilla(id),
  participante_id bigint not null references seguridad.participante(id),
  vigencia int not null, periodo text,
  codigo_verificacion text not null unique,
  huella_contenido char(64) not null,     -- SHA-256 del contenido canónico (es la que se imprime en el pie del PDF)
  huella_archivo char(64) not null,       -- SHA-256 de los bytes del PDF entregado
  filas int not null,
  pdf bytea not null,
  emitida_en timestamptz not null default now());
create index on reportes.emision (organizacion_id, emitida_en desc);

alter table reportes.emision enable row level security;
create policy emision_por_organizacion on reportes.emision
  using (organizacion_id = nullif(current_setting('sses.organizacion_id', true), '')::bigint);

-- vínculo del funcionario con su organización de control
alter table seguridad.participante add column if not exists organizacion_id bigint references reportes.organizacion_control(id);

-- verificación pública: expone solo metadatos de la emisión, nunca el PDF ni al emisor (atraviesa RLS de forma controlada)
create or replace function reportes.verificar(p_codigo text)
returns table (plantilla text, organizacion text, vigencia int, periodo text, emitida_en timestamptz, filas int, huella_contenido char(64), huella_archivo char(64))
language sql security definer set search_path = reportes, pg_temp as $$
  select p.nombre, o.nombre, e.vigencia, e.periodo, e.emitida_en, e.filas, e.huella_contenido, e.huella_archivo
  from reportes.emision e join reportes.plantilla p on p.id = e.plantilla_id join reportes.organizacion_control o on o.id = e.organizacion_id
  where e.codigo_verificacion = upper(p_codigo)
$$;
revoke all on function reportes.verificar(text) from public;

-- parametrización del Ministerio (sintética)
insert into reportes.organizacion_control (nombre) values ('Contraloría (demo)'), ('Procuraduría (demo)');
insert into reportes.plantilla (codigo, nombre, descripcion) values
  ('CUMPLIMIENTO_REGION', 'Cumplimiento del acompañamiento por región', 'Semanas programadas y registradas por región y mes de la vigencia.'),
  ('CONFIRMACION_REGION', 'Confirmación docente del acompañamiento por región', 'Semanas registradas y confirmadas u observadas por el docente, por región y mes.');
insert into reportes.plantilla_organizacion
  select o.id, p.id from reportes.organizacion_control o, reportes.plantilla p
  where o.nombre like 'Procuraduría%' or p.codigo = 'CUMPLIMIENTO_REGION';

grant usage on schema reportes to sses_api;
grant select on reportes.organizacion_control, reportes.plantilla, reportes.plantilla_organizacion to sses_api;
grant select, insert on reportes.emision to sses_api;
grant usage on all sequences in schema reportes to sses_api;
grant execute on function reportes.verificar(text) to sses_api;
