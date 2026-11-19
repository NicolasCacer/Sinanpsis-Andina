using Dapper;
using Npgsql;
using Sses.Api.Comun;

namespace Sses.Api.Maestros;

/// <summary>Datos maestros del Ministerio: SOLO lectura (sin POST/PUT/DELETE).</summary>
public static class MaestrosEndpoints
{
    public static void MapMaestros(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").RequireAuthorization(Auth.PGestor);

        // RQ_006: entidades activas que administra el gestor
        g.MapGet("/entidades", async (NpgsqlDataSource ds, IUsuarioActual ua) =>
        {
            var u = await ua.ObtenerAsync(); if (u == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            await using var cn = await ds.OpenConnectionAsync();
            var filas = await cn.QueryAsync<(Guid public_id, string nombre, string municipio)>(
                @"select e.public_id, e.nombre, m.nombre municipio from maestros.entidad e
                  join seguridad.gestor_entidad ge on ge.entidad_id = e.id and ge.participante_id = @pid
                  join maestros.municipio m on m.id = e.municipio_id
                  where e.activa order by e.nombre", new { pid = u.ParticipanteId });
            return Results.Ok(filas.Select(f => new { id = f.public_id, nombre = f.nombre, municipio = f.municipio }));
        });
    }
}
