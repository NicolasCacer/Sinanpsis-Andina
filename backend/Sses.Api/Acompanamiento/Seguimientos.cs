using System.Text.Json;
using Dapper;
using FluentValidation;
using Npgsql;
using Sses.Api.Comun;

namespace Sses.Api.Acompanamiento;

public sealed record AsistenciaDto(Guid DocenteId, bool Asistio);
public sealed record SeguimientoDto(AsistenciaDto[]? Asistencia, string? Actividades, string? Avances, Guid[]? EvidenciaIds);

/// <summary>RQ_011: lógica única de registro de seguimiento (en línea, sincronizado y resolución de conflictos).</summary>
public static class Seguimientos
{
    public sealed record HitoBloqueado(long Id, Guid PublicId, long PlanId, string Estado, long Version, string Mes, long TutorId);

    public static Task<HitoBloqueado?> BloquearHito(NpgsqlConnection cn, NpgsqlTransaction tx, long tutorId, Guid hitoPublicId) =>
        cn.QuerySingleOrDefaultAsync<HitoBloqueado?>(
            @"select h.id, h.public_id, h.plan_id, h.estado, h.xmin::text::bigint version, p.mes, p.tutor_id
              from acompanamiento.hito_semanal h join acompanamiento.plan p on p.id = h.plan_id
              where h.public_id=@id and p.tutor_id=@t for update of h", new { id = hitoPublicId, t = tutorId }, tx);

    public static Dictionary<string, string[]>? Validar(SeguimientoDto dto, Guid[] docentesDelPlan)
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(dto.Actividades)) e["actividades"] = ["Describe las actividades."]; else if (dto.Actividades.Length > 4000) e["actividades"] = ["Las actividades admiten máximo 4.000 caracteres."];
        if (string.IsNullOrWhiteSpace(dto.Avances)) e["avances"] = ["Describe los avances."]; else if (dto.Avances.Length > 4000) e["avances"] = ["Los avances admiten máximo 4.000 caracteres."];
        var asis = dto.Asistencia ?? [];
        if (docentesDelPlan.Except(asis.Select(a => a.DocenteId)).Any() || asis.Select(a => a.DocenteId).Except(docentesDelPlan).Any() || asis.Select(a => a.DocenteId).Distinct().Count() != asis.Length)
            e["asistencia"] = ["Marca la asistencia de cada docente del plan."];
        if ((dto.EvidenciaIds?.Length ?? 0) > 5) e["evidenciaIds"] = ["Máximo 5 fotos."];
        return e.Count == 0 ? null : e;
    }

    public static async Task<Guid[]> DocentesDelPlan(NpgsqlConnection cn, NpgsqlTransaction tx, long planId) =>
        (await cn.QueryAsync<Guid>("select d.public_id from acompanamiento.plan_docente pd join maestros.docente d on d.id=pd.docente_id where pd.plan_id=@p", new { p = planId }, tx)).ToArray();

    /// <summary>Aplica el seguimiento. Si sobrescribir, reemplaza el existente (resolución de conflicto "conservar el mío"). Devuelve la nueva versión del hito.</summary>
    public static async Task<(long version, object? antes)> Aplicar(NpgsqlConnection cn, NpgsqlTransaction tx, long participanteId, HitoBloqueado h, SeguimientoDto dto,
        string origen, DateTime capturadoEn, bool sobrescribir)
    {
        object? antes = null;
        var docs = (await cn.QueryAsync<(long id, Guid public_id)>(
            "select d.id, d.public_id from acompanamiento.plan_docente pd join maestros.docente d on d.id=pd.docente_id where pd.plan_id=@p", new { p = h.PlanId }, tx)).ToDictionary(x => x.public_id, x => x.id);
        long segId;
        if (sobrescribir)
        {
            var prev = await cn.QuerySingleOrDefaultAsync<(long id, string actividades, string avances, string origen)?>(
                "select id, actividades, avances, origen from acompanamiento.seguimiento_semanal where hito_id=@h order by registrado_en desc limit 1", new { h = h.Id }, tx);
            if (prev != null)
            {
                antes = new { prev.Value.actividades, prev.Value.avances, prev.Value.origen };
                segId = prev.Value.id;
                await cn.ExecuteAsync("delete from acompanamiento.asistencia_seguimiento where seguimiento_id=@s", new { s = segId }, tx);
                await cn.ExecuteAsync("update acompanamiento.evidencia set seguimiento_id = null where seguimiento_id=@s", new { s = segId }, tx);
                await cn.ExecuteAsync(@"update acompanamiento.seguimiento_semanal set actividades=@a, avances=@v, origen=@o, capturado_en=@c, registrado_por=@r where id=@s and hito_id=@h",
                    new { a = dto.Actividades!.Trim(), v = dto.Avances!.Trim(), o = origen, c = capturadoEn, r = participanteId, s = segId, h = h.Id }, tx);
                goto detalles;
            }
        }
        segId = await cn.ExecuteScalarAsync<long>(@"insert into acompanamiento.seguimiento_semanal (public_id,hito_id,actividades,avances,origen,capturado_en,registrado_por)
            values (@p,@h,@a,@v,@o,@c,@r) returning id", new { p = Guid.CreateVersion7(), h = h.Id, a = dto.Actividades!.Trim(), v = dto.Avances!.Trim(), o = origen, c = capturadoEn, r = participanteId }, tx);
    detalles:
        foreach (var a in dto.Asistencia!)
            await cn.ExecuteAsync("insert into acompanamiento.asistencia_seguimiento (seguimiento_id,docente_id,asistio) values (@s,@d,@a)", new { s = segId, d = docs[a.DocenteId], a = a.Asistio }, tx);
        if (dto.EvidenciaIds is { Length: > 0 })
            await cn.ExecuteAsync("update acompanamiento.evidencia set seguimiento_id=@s where public_id = any(@ids) and subido_por=@u", new { s = segId, ids = dto.EvidenciaIds, u = participanteId }, tx);
        var ver = await cn.ExecuteScalarAsync<long>("update acompanamiento.hito_semanal set estado='REGISTRADA', actualizado_en=now() where id=@h returning xmin::text::bigint", new { h = h.Id }, tx);
        return (ver, antes);
    }

    public static async Task<object> Cumplimiento(NpgsqlConnection cn, NpgsqlTransaction? tx, long tutorId, string mes)
    {
        var r = await cn.QuerySingleAsync<(int registradas, int total)>(
            @"select count(*) filter (where h.estado <> 'PROGRAMADA')::int, count(*)::int from acompanamiento.hito_semanal h
              join acompanamiento.plan p on p.id=h.plan_id where p.tutor_id=@t and p.mes=@mes", new { t = tutorId, mes }, tx);
        return new { registradas = r.registradas, programadas = r.total, porcentaje = SemanasIso.Cumplimiento(r.registradas, r.total) };
    }

    public static void MapSeguimientos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/hitos").RequireAuthorization(Auth.PTutorLider);

        // RQ_011: registra el seguimiento de la semana; If-Match con la versión del hito (412 si cambió)
        g.MapPut("/{id:guid}/seguimiento", async (Guid id, SeguimientoDto dto, HttpRequest req, NpgsqlDataSource ds, IUsuarioActual ua, IBitacora bit) =>
        {
            var u = await ua.ObtenerAsync(); if (u?.PersonaId == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            var ifMatch = req.Headers.IfMatch.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(ifMatch)) return Problemas.De(428, "IF_MATCH_REQUERIDO", "Falta la cabecera If-Match.");
            await using var cn = await ds.OpenConnectionAsync(); await using var tx = await cn.BeginTransactionAsync();
            var h = await BloquearHito(cn, tx, u.PersonaId.Value, id);
            if (h == null) return Problemas.NoEncontrado("HITO_NO_ENCONTRADO", "El hito no existe.");
            var err = Validar(dto, await DocentesDelPlan(cn, tx, h.PlanId));
            if (err != null) return Problemas.Validacion(err);
            if (Datos.ParseEtag(ifMatch) != h.Version) return Problemas.De(412, "VERSION_CAMBIADA", "La semana cambió mientras la editabas, recarga.");
            if (h.Estado != "PROGRAMADA") return Problemas.De(409, "SEMANA_CERRADA", "Esta semana ya tiene un seguimiento registrado.");
            var (ver, _) = await Aplicar(cn, tx, u.ParticipanteId, h, dto, "EN_LINEA", DateTime.UtcNow, false);
            await bit.RegistrarAsync(cn, tx, "REGISTRAR_SEGUIMIENTO", "hito", id.ToString(), null,
                new { origen = "EN_LINEA", asistentes = dto.Asistencia!.Count(a => a.Asistio), docentes = dto.Asistencia!.Length, evidencias = dto.EvidenciaIds?.Length ?? 0 });
            var cump = await Cumplimiento(cn, tx, u.PersonaId.Value, h.Mes);
            await tx.CommitAsync();
            req.HttpContext.Response.Headers.ETag = Datos.Etag(ver);
            return Results.Ok(new { estado = "REGISTRADA", version = ver, cumplimientoMes = cump });
        });
    }
}
