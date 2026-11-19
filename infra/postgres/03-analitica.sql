\c sses sses_owner
-- RQ_013: indicadores preagregados del tablero de desempeño de tutores (D22: refresco por la API cada 15 min, sin pg_cron)
create schema if not exists analitica;
create materialized view analitica.mv_cumplimiento_tutor as
  select p.tutor_id, p.estrategia_id, p.mes, t.municipio_id, m.region_id, e.entidad_id,
         count(*)::int programadas,
         count(*) filter (where h.estado <> 'PROGRAMADA')::int registradas,
         count(*) filter (where h.estado in ('CONFIRMADO','OBSERVADO'))::int confirmadas,
         count(*) filter (where h.estado <> 'PROGRAMADA' and s.registrado_en::date <= h.fecha_fin + 2)::int a_tiempo
  from acompanamiento.hito_semanal h
  join acompanamiento.plan p on p.id = h.plan_id
  join maestros.tutor t on t.id = p.tutor_id
  join maestros.municipio m on m.id = t.municipio_id
  join estrategias.estrategia e on e.id = p.estrategia_id
  left join lateral (select ss.registrado_en from acompanamiento.seguimiento_semanal ss where ss.hito_id = h.id order by ss.registrado_en desc limit 1) s on true
  group by p.tutor_id, p.estrategia_id, p.mes, t.municipio_id, m.region_id, e.entidad_id;
create unique index ux_mv_cumplimiento on analitica.mv_cumplimiento_tutor (tutor_id, estrategia_id, mes);
create index ix_mv_cumplimiento_mes on analitica.mv_cumplimiento_tutor (mes, region_id);
create materialized view analitica.mv_docentes_atendidos as
  select p.tutor_id, p.estrategia_id, p.mes, count(distinct a.docente_id)::int docentes_atendidos
  from acompanamiento.plan p
  join acompanamiento.hito_semanal h on h.plan_id = p.id
  join acompanamiento.seguimiento_semanal s on s.hito_id = h.id
  join acompanamiento.asistencia_seguimiento a on a.seguimiento_id = s.id and a.asistio
  group by p.tutor_id, p.estrategia_id, p.mes;
create unique index ux_mv_docentes on analitica.mv_docentes_atendidos (tutor_id, estrategia_id, mes);
create or replace function analitica.refrescar() returns void language plpgsql security definer set search_path = analitica, pg_temp as $$
begin
  refresh materialized view concurrently analitica.mv_cumplimiento_tutor;
  refresh materialized view concurrently analitica.mv_docentes_atendidos;
end $$;
revoke all on function analitica.refrescar() from public;
grant usage on schema analitica to sses_api;
grant select on analitica.mv_cumplimiento_tutor, analitica.mv_docentes_atendidos to sses_api;
grant execute on function analitica.refrescar() to sses_api;
