using System.Text.Json;
using Dapper;
using Npgsql;
using Sses.Api.Acompanamiento;
using Sses.Api.Comun;

namespace Sses.Api.Sincronizacion;

public sealed record EvidenciaSync(string? Nombre, string? Base64);
public sealed record ItemSync(Guid IdempotencyKey, Guid HitoId, long VersionBase, DateTime CapturadoEn, AsistenciaDto[]? Asistencia, string? Actividades, string? Avances, EvidenciaSync[]? Evidencias, string? Resolucion);
public sealed record LoteSync(ItemSync[]? Items);
public sealed record ResolucionDto(Guid HitoId, string? Decision);

/// <summary>RQ_012: sincronización idempotente de seguimientos capturados sin conexión.</summary>
public static class SincronizacionEndpoints
{
    const int MaxLote = 50, MaxDias = 30;
    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    public static void MapSincronizacion(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/sincronizacion").RequireAuthorization(Auth.PTutorLider);

        g.MapPost("/seguimientos", async (LoteSync lote, NpgsqlDataSource ds, IUsuarioActual ua, IBitacora bit, IConfiguration cfg) =>
        {
            var u = await ua.ObtenerAsync(); if (u?.PersonaId == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            var items = lote.Items ?? [];
            if (items.Length == 0 || items.Length > MaxLote) return Problemas.Validacion(new Dictionary<string, string[]> { ["items"] = [$"Envía entre 1 y {MaxLote} registros por lote."] });
            await PurgarIdempotencia(ds);
            var res = new List<Dictionary<string, object?>>();
            foreach (var it in items) res.Add(await ProcesarItem(it, u, ds, bit, cfg));
            return Results.Ok(new { resultados = res });
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(250_000_000));

        // Resolución "conservar el del servidor": se descarta lo local y queda constancia en bitácora
        g.MapPost("/resoluciones", async (ResolucionDto dto, NpgsqlDataSource ds, IUsuarioActual ua, IBitacora bit) =>
        {
            var u = await ua.ObtenerAsync(); if (u?.PersonaId == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            if (dto.Decision != "SERVIDOR") return Problemas.Validacion(new Dictionary<string, string[]> { ["decision"] = ["Decisión inválida."] });
            await using var cn = await ds.OpenConnectionAsync();
            var ok = await cn.ExecuteScalarAsync<bool>("select exists(select 1 from acompanamiento.hito_semanal h join acompanamiento.plan p on p.id=h.plan_id where h.public_id=@h and p.tutor_id=@t)", new { h = dto.HitoId, t = u.PersonaId });
            if (!ok) return Problemas.NoEncontrado("HITO_NO_ENCONTRADO", "El hito no existe.");
            await bit.RegistrarAsync(cn, null, "CONFLICTO_RESUELTO", "hito", dto.HitoId.ToString(), null, new { decision = "CONSERVAR_SERVIDOR" });
            return Results.Ok(new { ok = true });
        });
    }

    // RQ_012: la clave de idempotencia se conserva 7 días. Como máximo una purga por hora; si falla no afecta la sincronización.
    static DateTime _ultimaPurga = DateTime.MinValue;
    static async Task PurgarIdempotencia(NpgsqlDataSource ds)
    {
        if (DateTime.UtcNow - _ultimaPurga < TimeSpan.FromHours(1)) return;
        _ultimaPurga = DateTime.UtcNow;
        try { await using var cn = await ds.OpenConnectionAsync(); await cn.ExecuteAsync("delete from acompanamiento.idempotencia where creado_en < now() - interval '7 days'"); }
        catch (PostgresException) { /* base sin el permiso de 03-mesa.sql: se purga en el siguiente reset */ }
    }

    static async Task<Dictionary<string, object?>> ProcesarItem(ItemSync it, Usuario u, NpgsqlDataSource ds, IBitacora bit, IConfiguration cfg)
    {
        Dictionary<string, object?> R(string estado, object? extra = null)
        {
            var d = new Dictionary<string, object?> { ["idempotencyKey"] = it.IdempotencyKey, ["estado"] = estado };
            if (extra != null) foreach (var p in extra.GetType().GetProperties()) d[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = p.GetValue(extra);
            return d;
        }
        await using var cn = await ds.OpenConnectionAsync(); await using var tx = await cn.BeginTransactionAsync();

        // 1) Idempotencia: la fila de la clave se inserta primero; si ya existía (aun de otra transacción concurrente) → DUPLICADO
        var nueva = await cn.ExecuteAsync("insert into acompanamiento.idempotencia (clave,endpoint,respuesta_status) values (@c,'sincronizacion/seguimientos',0) on conflict do nothing", new { c = it.IdempotencyKey }, tx);
        if (nueva == 0)
        {
            var guardado = await cn.ExecuteScalarAsync<string?>("select respuesta_cuerpo::text from acompanamiento.idempotencia where clave=@c", new { c = it.IdempotencyKey }, tx);
            await tx.RollbackAsync();
            var d = guardado == null ? new Dictionary<string, object?>() : JsonSerializer.Deserialize<Dictionary<string, object?>>(guardado, J)!;
            d["idempotencyKey"] = it.IdempotencyKey; d["estado"] = "DUPLICADO";
            return d;
        }

        async Task<Dictionary<string, object?>> Cerrar(Dictionary<string, object?> r, int status)
        {
            await cn.ExecuteAsync("update acompanamiento.idempotencia set respuesta_status=@s, respuesta_cuerpo=@b::jsonb where clave=@c", new { s = status, b = JsonSerializer.Serialize(r, J), c = it.IdempotencyKey }, tx);
            await tx.CommitAsync(); return r;
        }

        // 2) Más de 30 días → RECHAZADO
        if (DateTime.UtcNow - it.CapturadoEn.ToUniversalTime() > TimeSpan.FromDays(MaxDias))
            return await Cerrar(R("RECHAZADO", new { motivo = "El registro tiene más de 30 días y ya no puede sincronizarse." }), 422);

        var h = await Seguimientos.BloquearHito(cn, tx, u.PersonaId!.Value, it.HitoId);
        if (h == null) return await Cerrar(R("RECHAZADO", new { motivo = "El hito no existe o no es tuyo." }), 404);
        var dto = new SeguimientoDto(it.Asistencia, it.Actividades, it.Avances, null);
        var err = Seguimientos.Validar(dto, await Seguimientos.DocentesDelPlan(cn, tx, h.PlanId));
        if (err != null) return await Cerrar(R("RECHAZADO", new { motivo = string.Join(" ", err.Values.SelectMany(v => v)) }), 400);

        // 3) Conflicto: el hito cambió en el servidor y ya está registrado
        var sobrescribir = false;
        if (h.Estado != "PROGRAMADA")
        {
            if (it.Resolucion == "SOBRESCRIBIR" && it.VersionBase == h.Version) sobrescribir = true;
            else if (h.Version != it.VersionBase || it.Resolucion != "SOBRESCRIBIR")
            {
                var reg = await RegistroServidor(cn, tx, h.Id);
                return await Cerrar(R("CONFLICTO", new { versionServidor = h.Version, registroServidor = reg }), 409);
            }
        }

        // 4) Evidencias → archivos; luego se aplica como en RQ_011 con origen SIN_CONEXION
        var ids = new List<Guid>();
        foreach (var ev in (it.Evidencias ?? []).Take(5))
        {
            byte[] bytes; try { bytes = Convert.FromBase64String(ev.Base64 ?? ""); } catch { return await Cerrar(R("RECHAZADO", new { motivo = "Una de las fotos no es válida." }), 400); }
            var guardada = await Evidencias.Guardar(cn, tx, cfg, u.ParticipanteId, bytes);
            if (guardada == null) return await Cerrar(R("RECHAZADO", new { motivo = "Las fotos deben ser JPEG o WebP de máximo 1 MB." }), 400);
            ids.Add(guardada.Value.id);
        }
        dto = dto with { EvidenciaIds = ids.ToArray() };
        var (ver, antes) = await Seguimientos.Aplicar(cn, tx, u.ParticipanteId, h, dto, "SIN_CONEXION", it.CapturadoEn.ToUniversalTime(), sobrescribir);
        await bit.RegistrarAsync(cn, tx, sobrescribir ? "CONFLICTO_RESUELTO" : "SINCRONIZAR", "hito", it.HitoId.ToString(), antes,
            new { origen = "SIN_CONEXION", decision = sobrescribir ? "CONSERVAR_LOCAL" : null, asistentes = it.Asistencia!.Count(a => a.Asistio), evidencias = ids.Count, capturadoEn = it.CapturadoEn });
        var cump = await Seguimientos.Cumplimiento(cn, tx, u.PersonaId!.Value, h.Mes);
        return await Cerrar(R("APLICADO", new { cumplimientoMes = cump, versionServidor = ver }), 200);
    }

    static async Task<object?> RegistroServidor(NpgsqlConnection cn, NpgsqlTransaction tx, long hitoId)
    {
        var s = await cn.QuerySingleOrDefaultAsync<(long id, string actividades, string avances, string origen, DateTime registrado_en, string? por)?>(
            @"select s.id, s.actividades, s.avances, s.origen, s.registrado_en, p.nombre por from acompanamiento.seguimiento_semanal s
              left join seguridad.participante p on p.id = s.registrado_por where s.hito_id=@h order by s.registrado_en desc limit 1", new { h = hitoId }, tx);
        if (s == null) return null;
        var asis = await cn.QueryAsync<(Guid docente_id, string nombres, bool asistio)>(
            "select d.public_id, d.nombres, a.asistio from acompanamiento.asistencia_seguimiento a join maestros.docente d on d.id=a.docente_id where a.seguimiento_id=@s", new { s = s.Value.id }, tx);
        return new
        {
            actividades = s.Value.actividades, avances = s.Value.avances, origen = s.Value.origen, registradoPor = s.Value.por,
            registradoEn = DateTime.SpecifyKind(s.Value.registrado_en, DateTimeKind.Utc),
            asistencia = asis.Select(a => new { docenteId = a.docente_id, nombres = a.nombres, asistio = a.asistio })
        };
    }
}
