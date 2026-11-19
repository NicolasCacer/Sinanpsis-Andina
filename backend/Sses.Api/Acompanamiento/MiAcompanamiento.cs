using System.Text.Json;
using Dapper;
using Npgsql;
using Sses.Api.Comun;

namespace Sses.Api.Acompanamiento;

public sealed record ConfirmacionDto(bool? Confirmado, string? Observacion, DateTime? CapturadoEn);

/// <summary>HU_009 / CU_010 / RQ_016: el Tutor Docente consulta y confirma el acompañamiento que registró su Tutor Líder.</summary>
public static class MiAcompanamiento
{
    const int MaxObservacion = 500;
    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    public static void MapMiAcompanamiento(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").RequireAuthorization(Auth.PTutorDocente);

        // RQ_016: solo los planes en los que participa el docente del token (filtro por docente_id sobre plan_docente)
        g.MapGet("/mi-acompanamiento", async (string? mes, NpgsqlDataSource ds, IUsuarioActual ua, IBitacora bit) =>
        {
            var u = await ua.ObtenerAsync(); if (u?.PersonaId == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            if (!SemanasIso.MesValido(mes)) return Problemas.Validacion(new Dictionary<string, string[]> { ["mes"] = ["Elige un mes válido (AAAA-MM)."] });
            await using var cn = await ds.OpenConnectionAsync();
            var planes = (await cn.QueryAsync<(long id, Guid public_id, string est_nombre, string modalidad, string temas, string tutor)>(
                @"select p.id, p.public_id, e.nombre est_nombre, p.modalidad, p.temas, t.nombres tutor
                  from acompanamiento.plan_docente pd join acompanamiento.plan p on p.id=pd.plan_id
                  join estrategias.estrategia e on e.id=p.estrategia_id join maestros.tutor t on t.id=p.tutor_id
                  where pd.docente_id=@d and pd.mes=@mes order by p.id", new { d = u.PersonaId, mes })).ToList();
            var ids = planes.Select(p => p.id).ToArray();
            var hitos = (await cn.QueryAsync<HitoFila>(
                @"select h.id, h.plan_id, h.public_id, h.numero_semana, h.semana_iso, h.fecha_inicio, h.fecha_fin, h.estado,
                         s.id seg_id, s.actividades, s.avances, s.registrado_en,
                         c.confirmado, c.observacion, c.creado_en confirmado_en
                  from acompanamiento.hito_semanal h
                  left join lateral (select * from acompanamiento.seguimiento_semanal ss where ss.hito_id=h.id order by ss.registrado_en desc limit 1) s on h.estado <> 'PROGRAMADA'
                  left join acompanamiento.confirmacion_docente c on c.hito_id=h.id and c.docente_id=@d
                  where h.plan_id = any(@ids) order by h.numero_semana", new { ids, d = u.PersonaId })).ToList();
            var segIds = hitos.Where(h => h.SegId != null).Select(h => h.SegId!.Value).ToArray();
            var asis = (await cn.QueryAsync<(long seguimiento_id, bool asistio)>(
                "select seguimiento_id, asistio from acompanamiento.asistencia_seguimiento where seguimiento_id = any(@s) and docente_id=@d", new { s = segIds, d = u.PersonaId })).ToDictionary(x => x.seguimiento_id, x => x.asistio);
            var evid = (await cn.QueryAsync<(long seguimiento_id, Guid public_id, string tipo_mime)>(
                "select seguimiento_id, public_id, tipo_mime from acompanamiento.evidencia where seguimiento_id = any(@s)", new { s = segIds })).ToLookup(x => x.seguimiento_id);
            await bit.RegistrarAsync(cn, null, "CONSULTAR_MI_ACOMPANAMIENTO", "plan_docente", u.PublicId.ToString(), null, new { mes });
            return Results.Ok(new
            {
                mes,
                semanas = SemanasIso.Del(mes!).Select(s => new { numero = s.Numero, semanaIso = s.SemanaIso, fechaInicio = s.Inicio, fechaFin = s.Fin }),
                planes = planes.Select(p => new
                {
                    id = p.public_id, estrategia = p.est_nombre, modalidad = p.modalidad, temas = p.temas, tutorLider = p.tutor,
                    hitos = hitos.Where(h => h.PlanId == p.id).Select(h => new
                    {
                        id = h.PublicId, numeroSemana = (int)h.NumeroSemana, semanaIso = h.SemanaIso, fechaInicio = h.FechaInicio, fechaFin = h.FechaFin, estado = h.Estado,
                        seguimiento = h.SegId == null ? null : new
                        {
                            actividades = h.Actividades, avances = h.Avances, asistio = asis.TryGetValue(h.SegId.Value, out var a) ? (bool?)a : null,
                            registradoEn = h.RegistradoEn == null ? (DateTime?)null : DateTime.SpecifyKind(h.RegistradoEn.Value, DateTimeKind.Utc),
                            evidencias = evid[h.SegId.Value].Select(e => new { id = e.public_id, tipoMime = e.tipo_mime })
                        },
                        miConfirmacion = h.Confirmado == null ? null : new { confirmado = h.Confirmado.Value, observacion = h.Observacion, enviadoEn = h.ConfirmadoEn == null ? (DateTime?)null : DateTime.SpecifyKind(h.ConfirmadoEn.Value, DateTimeKind.Utc) }
                    })
                })
            });
        });

        // RQ_016: {id} es el hito (semana) del plan. 201 nuevo, 200 si la Idempotency-Key ya se aplicó, 409 doble confirmación, 422 observación, 404 semana no registrada
        g.MapPost("/seguimientos/{id:guid}/confirmaciones", async (Guid id, ConfirmacionDto dto, HttpRequest req, NpgsqlDataSource ds, IUsuarioActual ua, IBitacora bit) =>
        {
            var u = await ua.ObtenerAsync(); if (u?.PersonaId == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            if (!Guid.TryParse(req.Headers["Idempotency-Key"].FirstOrDefault(), out var clave))
                return Problemas.Validacion(new Dictionary<string, string[]> { ["idempotencyKey"] = ["Falta la cabecera Idempotency-Key (UUID)."] });
            var obs = dto.Observacion?.Trim();
            if (dto.Confirmado == null) return Problemas.Validacion(new Dictionary<string, string[]> { ["confirmado"] = ["Indica si confirmas el acompañamiento."] });
            if (obs?.Length > MaxObservacion) return Problemas.De(422, "OBSERVACION_MUY_LARGA", $"La observación admite máximo {MaxObservacion} caracteres.");
            if (!dto.Confirmado.Value && string.IsNullOrEmpty(obs)) return Problemas.De(422, "OBSERVACION_REQUERIDA", "Escribe una observación si el encuentro no se realizó como quedó registrado.");
            if (string.IsNullOrEmpty(obs)) obs = null;

            await using var cn = await ds.OpenConnectionAsync(); await using var tx = await cn.BeginTransactionAsync();
            // Idempotencia: la clave se inserta primero; si ya existía devuelve la respuesta original con 200
            var nueva = await cn.ExecuteAsync("insert into acompanamiento.idempotencia (clave,endpoint,respuesta_status) values (@c,'seguimientos/confirmaciones',0) on conflict do nothing", new { c = clave }, tx);
            if (nueva == 0)
            {
                var previo = await cn.ExecuteScalarAsync<string?>("select respuesta_cuerpo::text from acompanamiento.idempotencia where clave=@c", new { c = clave }, tx);
                await tx.RollbackAsync();
                return Results.Ok(previo == null ? new { duplicado = true } : JsonSerializer.Deserialize<JsonElement>(previo, J));
            }
            var h = await cn.QuerySingleOrDefaultAsync<(long id, string estado)?>(
                @"select h.id, h.estado from acompanamiento.hito_semanal h join acompanamiento.plan_docente pd on pd.plan_id=h.plan_id
                  where h.public_id=@h and pd.docente_id=@d for update of h", new { h = id, d = u.PersonaId }, tx);
            if (h == null || h.Value.estado == "PROGRAMADA") return Problemas.NoEncontrado("SEMANA_NO_REGISTRADA", "La semana no existe o aún no está registrada por el Tutor Líder.");
            if (await cn.ExecuteScalarAsync<bool>("select exists(select 1 from acompanamiento.confirmacion_docente where hito_id=@h and docente_id=@d)", new { h = h.Value.id, d = u.PersonaId }, tx))
                return Problemas.De(409, "YA_CONFIRMADA", "Ya enviaste tu confirmación de esta semana.");
            var origen = req.Headers["X-Origen"].FirstOrDefault() == "SIN_CONEXION" ? "SIN_CONEXION" : "EN_LINEA";
            await cn.ExecuteAsync(@"insert into acompanamiento.confirmacion_docente (public_id,hito_id,docente_id,confirmado,observacion,origen,capturado_en) values (@p,@h,@d,@c,@o,@or,@cap)",
                new { p = Guid.CreateVersion7(), h = h.Value.id, d = u.PersonaId, c = dto.Confirmado, o = obs, or = origen, cap = (dto.CapturadoEn ?? DateTime.UtcNow).ToUniversalTime() }, tx);
            // En un plan grupal basta una observación para que la semana quede 'Observada'
            var estado = await cn.ExecuteScalarAsync<string>(
                @"update acompanamiento.hito_semanal set estado = case when exists(select 1 from acompanamiento.confirmacion_docente where hito_id=@h and not confirmado) then 'OBSERVADO' else 'CONFIRMADO' end,
                  actualizado_en=now() where id=@h returning estado", new { h = h.Value.id }, tx);
            await bit.RegistrarAsync(cn, tx, "CONFIRMAR_ACOMPANAMIENTO", "hito", id.ToString(), null, new { confirmado = dto.Confirmado, conObservacion = obs != null, origen });
            var cuerpo = new { estado, confirmado = dto.Confirmado, observacion = obs };
            await cn.ExecuteAsync("update acompanamiento.idempotencia set respuesta_status=201, respuesta_cuerpo=@b::jsonb where clave=@c", new { b = JsonSerializer.Serialize(cuerpo, J), c = clave }, tx);
            await tx.CommitAsync();
            return Results.Created($"/api/v1/seguimientos/{id}/confirmaciones", cuerpo);
        });

        // Las evidencias del seguimiento solo se sirven a docentes del plan
        g.MapGet("/mi-acompanamiento/evidencias/{id:guid}", async (Guid id, NpgsqlDataSource ds, IUsuarioActual ua, IConfiguration cfg) =>
        {
            var u = await ua.ObtenerAsync(); if (u?.PersonaId == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            await using var cn = await ds.OpenConnectionAsync();
            var mime = await cn.ExecuteScalarAsync<string?>(
                @"select ev.tipo_mime from acompanamiento.evidencia ev join acompanamiento.seguimiento_semanal s on s.id=ev.seguimiento_id
                  join acompanamiento.hito_semanal h on h.id=s.hito_id join acompanamiento.plan_docente pd on pd.plan_id=h.plan_id
                  where ev.public_id=@id and pd.docente_id=@d", new { id, d = u.PersonaId });
            if (mime == null) return Problemas.NoEncontrado("EVIDENCIA_NO_ENCONTRADA", "La evidencia no existe.");
            var ruta = Path.Combine(cfg["Evidencias:Ruta"] ?? Path.Combine(AppContext.BaseDirectory, "data", "evidencias"), $"{id}.{(mime == "image/webp" ? "webp" : "jpg")}");
            return File.Exists(ruta) ? Results.File(ruta, mime) : Problemas.NoEncontrado("EVIDENCIA_NO_ENCONTRADA", "La evidencia no existe.");
        });
    }

    sealed class HitoFila
    {
        public long Id { get; set; } public long PlanId { get; set; } public Guid PublicId { get; set; } public short NumeroSemana { get; set; } public string SemanaIso { get; set; } = "";
        public DateOnly FechaInicio { get; set; } public DateOnly FechaFin { get; set; } public string Estado { get; set; } = "";
        public long? SegId { get; set; } public string? Actividades { get; set; } public string? Avances { get; set; } public DateTime? RegistradoEn { get; set; }
        public bool? Confirmado { get; set; } public string? Observacion { get; set; } public DateTime? ConfirmadoEn { get; set; }
    }
}
