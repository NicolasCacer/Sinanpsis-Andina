using Dapper;
using Npgsql;
using Sses.Api.Comun;
using System.Security.Claims;

namespace Sses.Api.Auditoria;

public static class AuditoriaEndpoints
{
    public static void MapAuditoria(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/auditoria").RequireAuthorization(Auth.PAutenticado);

        // RQ_005: verificación de la cadena de hashes (momento de demo)
        g.MapGet("/verificacion", async (NpgsqlDataSource ds) =>
        {
            await using var cn = await ds.OpenConnectionAsync();
            var r = await cn.QuerySingleAsync<(bool ok, long filas, long? primer_id_roto)>("select ok, filas, primer_id_roto from auditoria.verificar_cadena()");
            return Results.Ok(new { ok = r.ok, filas = r.filas, primerIdRoto = r.primer_id_roto });
        });

        // Últimos eventos del propio usuario
        g.MapGet("/recientes", async (ClaimsPrincipal u, NpgsqlDataSource ds, int? limite) =>
        {
            var usuario = u.FindFirstValue("preferred_username") ?? "";
            await using var cn = await ds.OpenConnectionAsync();
            var filas = await cn.QueryAsync<(long id, DateTime fecha_utc, string accion, string? entidad, string? entidad_id, string hash)>(
                "select id, fecha_utc, accion, entidad, entidad_id, encode(hash,'hex') hash from auditoria.bitacora where usuario=@usuario order by id desc limit @n",
                new { usuario, n = Math.Clamp(limite ?? 20, 1, 100) });
            return Results.Ok(filas.Select(f => new { f.id, fechaUtc = DateTime.SpecifyKind(f.fecha_utc, DateTimeKind.Utc), f.accion, f.entidad, entidadId = f.entidad_id, hash = f.hash[..12] }));
        });
    }
}
