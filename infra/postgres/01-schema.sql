\c sses sses_owner
-- Esquema SSES (D11: scripts SQL como fuente única). RQ_002, RQ_005, RQ_007, RQ_026.
create schema maestros; create schema seguridad; create schema estrategias;
create schema acompanamiento; create schema auditoria;

-- ===================== maestros (solo lectura para la API) =====================
create table maestros.region (id int primary key, nombre text not null);
create table maestros.municipio (
  id int primary key, region_id int not null references maestros.region(id),
  nombre text not null, codigo_dane text not null);
create table maestros.entidad (
  id bigint generated always as identity primary key,
  public_id uuid not null unique default gen_random_uuid(),
  nombre text not null, municipio_id int not null references maestros.municipio(id),
  activa boolean not null default true);
create table maestros.sede (
  id int primary key, nombre text not null, municipio_id int not null references maestros.municipio(id));

-- RQ_002: particionada HASH(id) en 16. RQ_026: columnas *_cif cifradas AES-256-GCM (nonce||ciphertext||tag)
create table maestros.estudiante (
  id bigint not null,
  tipo_documento_cif bytea not null,
  numero_documento_cif bytea not null,
  nombres_cif bytea not null,
  fecha_nacimiento_cif bytea not null,
  nombre_visible text not null,
  sede_id int not null references maestros.sede(id),
  primary key (id)
) partition by hash (id);
do $$ begin
  for i in 0..15 loop
    execute format('create table maestros.estudiante_p%s partition of maestros.estudiante for values with (modulus 16, remainder %s)', i, i);
  end loop;
end $$;
-- D04: índice de huella aparte (UNIQUE global imposible en tabla particionada por HASH(id))
create table maestros.estudiante_huella (
  huella bytea primary key check (octet_length(huella) = 32),
  estudiante_id bigint not null,
  public_id uuid not null unique);
create index ix_huella_estudiante on maestros.estudiante_huella (estudiante_id);
create table maestros.tutor (
  id bigint generated always as identity primary key,
  public_id uuid not null unique default gen_random_uuid(),
  nombres text not null, correo text not null, municipio_id int not null references maestros.municipio(id));
create table maestros.docente (
  id bigint generated always as identity primary key,
  public_id uuid not null unique default gen_random_uuid(),
  nombres text not null, correo text not null, sede_id int not null references maestros.sede(id));

-- ===================== seguridad =====================
create table seguridad.participante (
  id bigint generated always as identity primary key,
  public_id uuid not null unique default gen_random_uuid(),
  keycloak_sub text not null unique,
  correo text not null, nombre text not null,
  rol text not null,
  persona_tipo text not null check (persona_tipo in ('TUTOR','DOCENTE','FUNCIONARIO')),
  persona_id bigint,
  ultimo_ingreso timestamptz, ingreso_actual timestamptz);
create table seguridad.gestor_entidad (
  participante_id bigint not null references seguridad.participante(id),
  entidad_id bigint not null references maestros.entidad(id),
  primary key (participante_id, entidad_id));

-- ===================== estrategias =====================
create table estrategias.estrategia (
  id bigint generated always as identity primary key,
  public_id uuid not null unique,
  entidad_id bigint not null references maestros.entidad(id),
  nombre varchar(150) not null,
  objetivo varchar(2000) not null,
  poblacion varchar(300) not null,
  fecha_inicio date not null,
  fecha_fin date not null check (fecha_fin > fecha_inicio),
  estado text not null default 'EDITABLE' check (estado in ('EDITABLE','BLOQUEADA')),
  estudiantes_vinculados int not null default 0,
  creado_por bigint not null, creado_en timestamptz not null default now(),
  actualizado_en timestamptz);
create unique index ux_estrategia_entidad_nombre on estrategias.estrategia (entidad_id, lower(nombre));
create index ix_estrategia_entidad_id on estrategias.estrategia (entidad_id, id);

create table estrategias.estrategia_estudiante (
  estrategia_id bigint not null references estrategias.estrategia(id),
  estudiante_id bigint not null,
  vinculado_por bigint not null, vinculado_en timestamptz not null default now(),
  primary key (estrategia_id, estudiante_id));

-- RQ_007: bloqueo en BD. Si hay vinculados no se puede alterar ningún dato de negocio.
create function estrategias.f_bloqueo_edicion() returns trigger language plpgsql as $$
begin
  if (new.entidad_id, new.nombre, new.objetivo, new.poblacion, new.fecha_inicio, new.fecha_fin)
     is distinct from
     (old.entidad_id, old.nombre, old.objetivo, old.poblacion, old.fecha_inicio, old.fecha_fin)
     and exists (select 1 from estrategias.estrategia_estudiante where estrategia_id = old.id) then
    raise exception 'ESTRATEGIA_BLOQUEADA' using errcode = 'P0001';
  end if;
  return new;
end $$;
create trigger t_bloqueo_edicion before update on estrategias.estrategia
  for each row execute function estrategias.f_bloqueo_edicion();

-- RQ_007/RQ_008: el primer vínculo bloquea la estrategia y actualiza el contador (misma transacción)
create function estrategias.f_vinculo_contador() returns trigger language plpgsql as $$
begin
  update estrategias.estrategia e
     set estudiantes_vinculados = e.estudiantes_vinculados + n.cnt,
         estado = 'BLOQUEADA'
    from (select estrategia_id, count(*)::int cnt from nuevos group by estrategia_id) n
   where e.id = n.estrategia_id;
  return null;
end $$;
create trigger t_vinculo_contador after insert on estrategias.estrategia_estudiante
  referencing new table as nuevos for each statement execute function estrategias.f_vinculo_contador();

-- ===================== acompanamiento =====================
create table acompanamiento.asignacion (
  tutor_id bigint not null references maestros.tutor(id),
  docente_id bigint not null references maestros.docente(id),
  estrategia_id bigint not null references estrategias.estrategia(id),
  vigente boolean not null default true,
  primary key (tutor_id, docente_id, estrategia_id));
create index ix_asignacion_tutor on acompanamiento.asignacion (tutor_id) where vigente;

create table acompanamiento.plan (
  id bigint generated always as identity primary key,
  public_id uuid not null unique,
  tutor_id bigint not null references maestros.tutor(id),
  estrategia_id bigint not null references estrategias.estrategia(id),
  mes char(7) not null, modalidad text not null check (modalidad in ('INDIVIDUAL','GRUPAL')),
  temas varchar(2000) not null, creado_en timestamptz not null default now());
create table acompanamiento.plan_docente (
  plan_id bigint not null references acompanamiento.plan(id),
  docente_id bigint not null references maestros.docente(id),
  tutor_id bigint not null, mes char(7) not null,
  primary key (plan_id, docente_id),
  constraint ux_plan_docente_mes unique (tutor_id, docente_id, mes));
create index ix_plan_tutor_mes on acompanamiento.plan (tutor_id, mes);
create table acompanamiento.hito_semanal (
  id bigint generated always as identity primary key,
  public_id uuid not null unique,
  plan_id bigint not null references acompanamiento.plan(id),
  numero_semana smallint not null check (numero_semana between 1 and 5),
  semana_iso char(8) not null, fecha_inicio date not null, fecha_fin date not null,
  estado text not null default 'PROGRAMADA' check (estado in ('PROGRAMADA','REGISTRADA','CONFIRMADO','OBSERVADO')),
  actualizado_en timestamptz not null default now(),
  unique (plan_id, numero_semana));

-- RQ_016: confirmación del acompañamiento por el Tutor Docente (una por docente y semana)
create table acompanamiento.confirmacion_docente (
  id bigint generated always as identity primary key,
  public_id uuid not null unique,
  hito_id bigint not null references acompanamiento.hito_semanal(id),
  docente_id bigint not null references maestros.docente(id),
  confirmado boolean not null, observacion varchar(500),
  origen text not null check (origen in ('EN_LINEA','SIN_CONEXION')),
  capturado_en timestamptz not null, creado_en timestamptz not null default now(),
  constraint ux_confirmacion_hito_docente unique (hito_id, docente_id),
  constraint ck_confirmacion_obs check (confirmado or length(btrim(coalesce(observacion,''))) > 0));
create index ix_confirmacion_docente on acompanamiento.confirmacion_docente (docente_id);

-- RQ_002: partición mensual (D03). hito_id sin UNIQUE global (la unicidad se garantiza bajo FOR UPDATE del hito).
create table acompanamiento.seguimiento_semanal (
  id bigint generated always as identity,
  public_id uuid not null,
  hito_id bigint not null,
  actividades varchar(4000) not null, avances varchar(4000) not null,
  origen text not null check (origen in ('EN_LINEA','SIN_CONEXION')),
  capturado_en timestamptz not null, registrado_en timestamptz not null default now(),
  registrado_por bigint not null,
  primary key (id, registrado_en)
) partition by range (registrado_en);
create index ix_seguimiento_hito on acompanamiento.seguimiento_semanal (hito_id);
create table acompanamiento.asistencia_seguimiento (
  seguimiento_id bigint not null, docente_id bigint not null, asistio boolean not null,
  primary key (seguimiento_id, docente_id));
create table acompanamiento.evidencia (
  id bigint generated always as identity primary key,
  public_id uuid not null unique,
  seguimiento_id bigint, subido_por bigint not null,
  uri text not null, tipo_mime text not null, bytes int not null, sha256 text not null,
  creado_en timestamptz not null default now());
create table acompanamiento.idempotencia (
  clave uuid primary key, endpoint text not null,
  respuesta_status int not null, respuesta_cuerpo jsonb,
  creado_en timestamptz not null default now());

do $$ declare d date; begin
  for d in select generate_series(date '2026-01-01', date '2028-12-01', interval '1 month')::date loop
    execute format('create table acompanamiento.seguimiento_semanal_%s partition of acompanamiento.seguimiento_semanal for values from (%L) to (%L)',
      to_char(d,'YYYY_MM'), d, (d + interval '1 month')::date);
  end loop;
end $$;
create table acompanamiento.seguimiento_semanal_default partition of acompanamiento.seguimiento_semanal default;

-- ===================== auditoria (RQ_005) =====================
create table auditoria.bitacora (
  id bigint generated always as identity,
  fecha_utc timestamptz not null default now(),
  usuario text not null, rol text not null, ip inet,
  accion text not null,
  entidad text, entidad_id text,
  antes jsonb, despues jsonb,
  traceparent text,
  hash_anterior bytea, hash bytea not null,
  primary key (id, fecha_utc)
) partition by range (fecha_utc);
create index ix_bitacora_id on auditoria.bitacora (id);
create index ix_bitacora_usuario on auditoria.bitacora (usuario, id desc);
do $$ declare d date; begin
  for d in select generate_series(date '2026-01-01', date '2028-12-01', interval '1 month')::date loop
    execute format('create table auditoria.bitacora_%s partition of auditoria.bitacora for values from (%L) to (%L)',
      to_char(d,'YYYY_MM'), d, (d + interval '1 month')::date);
  end loop;
end $$;
create table auditoria.bitacora_default partition of auditoria.bitacora default;

-- Último hash de la cadena (una sola fila; solo se toca desde el trigger SECURITY DEFINER)
create table auditoria.cadena (id int primary key check (id = 1), ultimo_hash bytea);
insert into auditoria.cadena values (1, null);

create function auditoria.f_canonica(b auditoria.bitacora) returns text language sql immutable as $$
  select concat_ws('|', b.id::text, to_char(b.fecha_utc at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.US'),
    b.usuario, b.rol, coalesce(host(b.ip),''), b.accion, coalesce(b.entidad,''), coalesce(b.entidad_id,''),
    coalesce(b.antes::text,''), coalesce(b.despues::text,''), coalesce(b.traceparent,''))
$$;

-- hash = sha256(hash_anterior || fila_canonica); la fila de cadena serializa los inserts
create function auditoria.f_hash_bitacora() returns trigger language plpgsql security definer
  set search_path = pg_catalog, auditoria as $$
declare prev bytea;
begin
  select ultimo_hash into prev from auditoria.cadena where id = 1 for update;
  new.hash_anterior := prev;
  new.hash := sha256(coalesce(prev, ''::bytea) || convert_to(auditoria.f_canonica(new), 'UTF8'));
  update auditoria.cadena set ultimo_hash = new.hash where id = 1;
  return new;
end $$;
create trigger t_hash_bitacora before insert on auditoria.bitacora
  for each row execute function auditoria.f_hash_bitacora();

create function auditoria.verificar_cadena(out ok boolean, out filas bigint, out primer_id_roto bigint)
language plpgsql stable as $$
declare r auditoria.bitacora; prev bytea := null; esperado bytea;
begin
  ok := true; filas := 0; primer_id_roto := null;
  for r in select * from auditoria.bitacora order by id loop
    filas := filas + 1;
    esperado := sha256(coalesce(prev, ''::bytea) || convert_to(auditoria.f_canonica(r), 'UTF8'));
    if r.hash is distinct from esperado or r.hash_anterior is distinct from prev then
      ok := false; primer_id_roto := r.id; return;
    end if;
    prev := r.hash;
  end loop;
end $$;
