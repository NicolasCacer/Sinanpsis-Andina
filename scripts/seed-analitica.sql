-- Datos sintéticos para el tablero (RQ_013): planes, hitos y seguimientos de los tutores 2..200 (el tutor demo 1 no se toca).
-- Idempotente: si ya hay planes de otros tutores no hace nada.
do $$
declare pid bigint; hid bigint; sid bigint; r record; d record; m int; w int; mes_d date; ini date; est text; p numeric; reg timestamptz; participante bigint;
begin
  if exists (select 1 from acompanamiento.plan where tutor_id > 1) then raise notice 'Ya sembrado'; return; end if;
  select id into participante from seguridad.participante where rol='TUTOR_LIDER' limit 1;
  for r in select a.tutor_id, a.estrategia_id, array_agg(a.docente_id order by a.docente_id) docs
           from acompanamiento.asignacion a where a.tutor_id > 1 group by a.tutor_id, a.estrategia_id loop
    p := 0.35 + 0.65 * (((r.tutor_id * 7919) % 100) / 100.0);   -- propensión del tutor a registrar
    for m in 0..2 loop
      mes_d := (date_trunc('month', now()) - make_interval(months => m))::date;
      insert into acompanamiento.plan (public_id,tutor_id,estrategia_id,mes,modalidad,temas)
        values (gen_random_uuid(), r.tutor_id, r.estrategia_id, to_char(mes_d,'YYYY-MM'), case when array_length(r.docs,1) > 1 then 'GRUPAL' else 'INDIVIDUAL' end, 'Acompañamiento pedagógico del mes') returning id into pid;
      insert into acompanamiento.plan_docente select pid, unnest(r.docs), r.tutor_id, to_char(mes_d,'YYYY-MM');
      ini := mes_d + ((8 - extract(isodow from mes_d)::int) % 7);  -- primer lunes
      for w in 1..4 loop
        declare fi date := ini + 7*(w-1); ff date := ini + 7*(w-1) + 6; estado text; begin
          if ff >= current_date then estado := 'PROGRAMADA';
          elsif random() < p then estado := case when random() < 0.6 then 'CONFIRMADO' when random() < 0.15 then 'OBSERVADO' else 'REGISTRADA' end;
          else estado := 'PROGRAMADA'; end if;
          insert into acompanamiento.hito_semanal (public_id,plan_id,numero_semana,semana_iso,fecha_inicio,fecha_fin,estado)
            values (gen_random_uuid(), pid, w, to_char(fi,'IYYY-"W"IW'), fi, ff, estado) returning id into hid;
          if estado <> 'PROGRAMADA' then
            reg := (ff + (case when random() < 0.8 then 0 else 1 + (random()*5)::int end))::timestamptz + interval '10 hours';
            if reg > now() then reg := now() - interval '1 hour'; end if;
            insert into acompanamiento.seguimiento_semanal (public_id,hito_id,actividades,avances,origen,capturado_en,registrado_en,registrado_por)
              values (gen_random_uuid(), hid, 'Taller con docentes', 'Avance observado', case when random() < 0.3 then 'SIN_CONEXION' else 'EN_LINEA' end, reg, reg, participante) returning id into sid;
            insert into acompanamiento.asistencia_seguimiento select sid, x, random() < 0.85 from unnest(r.docs) x;
          end if;
        end;
      end loop;
    end loop;
  end loop;
end $$;
select analitica.refrescar();
