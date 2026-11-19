using System.Diagnostics;
using Dapper;
using Npgsql;
using Sses.Api.Comun;

namespace Sses.Api.Estrategias;

public sealed record VincularEstudiantes(Guid[]? EstudianteIds);

public static class VinculacionEndpoints
{
    public static void MapVinculacion(this RouteGroupBuilder estrategias)
    {
        // Lista de vinculados (keyset 50)
        estrategias.MapGet("/{id:guid}/estudiantes", async (Guid id, string? despuesDe, NpgsqlDataSource ds, IUsuarioActual ua) =>
        {
            var u = await ua.ObtenerAsync(); if (u == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            long? cursor = long.TryParse(despuesDe, out var c) ? c : null;
            await using var cn = await ds.OpenConnectionAsync();
            var eid = await cn.QuerySingleOrDefaultAsync<long?>("select e.id from estrategias.estrategia e where e.public_id=@id and " + Acceso.FiltroEntidadGestor, new { id, pid = u.ParticipanteId });
            if (eid == null) return Problemas.NoEncontrado("ESTRATEGIA_NO_ENCONTRADA", "La estrategia no existe.");
            var filas = (await cn.QueryAsync<(long estudiante_id, Guid public_id, string nombre, string sede, string municipio, DateTime vinculado_en)>(
                @"select ee.estudiante_id, h.public_id, s.nombre_visible nombre, sd.nombre sede, m.nombre municipio, ee.vinculado_en
                  from estrategias.estrategia_estudiante ee
                  join maestros.estudiante s on s.id = ee.estudiante_id
                  join maestros.estudiante_huella h on h.estudiante_id = ee.estudiante_id
                  join maestros.sede sd on sd.id = s.sede_id join maestros.municipio m on m.id = sd.municipio_id
                  where ee.estrategia_id=@e and (@cursor::bigint is null or ee.estudiante_id > @cursor)
                  order by ee.estudiante_id limit 51", new { e = eid, cursor })).ToList();
            var hayMas = filas.Count > 50; if (hayMas) filas.RemoveAt(50);
            return Results.Ok(new
            {
                items = filas.Select(f => new { id = f.public_id, nombre = f.nombre, sede = f.sede, municipio = f.municipio, vinculadoEn = DateTime.SpecifyKind(f.vinculado_en, DateTimeKind.Utc) }),
                siguiente = hayMas ? filas[^1].estudiante_id.ToString() : null
            });
        });

        // RQ_008 / RQ_007: vinculación en una transacción; el primer vínculo bloquea la estrategia (trigger)
        estrategias.MapPost("/{id:guid}/estudiantes", async (Guid id, VincularEstudiantes dto, NpgsqlDataSource ds, IUsuarioActual ua, IBitacora bit) =>
        {
            var u = await ua.ObtenerAsync(); if (u == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            var ids = dto.EstudianteIds?.Distinct().ToArray() ?? [];
            if (ids.Length == 0 || ids.Length > 200)
                return Problemas.Validacion(new Dictionary<string, string[]> { ["estudianteIds"] = ["Envía entre 1 y 200 estudiantes."] });
            await using var cn = await ds.OpenConnectionAsync(); await using var tx = await cn.BeginTransactionAsync();
            var e = await cn.QuerySingleOrDefaultAsync<(long id, DateOnly fecha_fin)?>(
                "select e.id, e.fecha_fin from estrategias.estrategia e where e.public_id=@id and " + Acceso.FiltroEntidadGestor + " for update of e", new { id, pid = u.ParticipanteId }, tx);
            if (e == null) return Problemas.NoEncontrado("ESTRATEGIA_NO_ENCONTRADA", "La estrategia no existe.");
            if (DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5)) > e.Value.fecha_fin)
                return Problemas.De(422, "ESTRATEGIA_FUERA_DE_VIGENCIA", "La estrategia ya terminó su vigencia.");
            var res = (await cn.QueryAsync<(long estudiante_id, Guid public_id)>(
                "select estudiante_id, public_id from maestros.estudiante_huella where public_id = any(@ids)", new { ids }, tx)).ToList();
            if (res.Count != ids.Length) return Problemas.De(422, "ESTUDIANTE_NO_ENCONTRADO", "Alguno de los estudiantes no existe.");
            var nuevos = (await cn.QueryAsync<long>(
                @"insert into estrategias.estrategia_estudiante (estrategia_id, estudiante_id, vinculado_por)
                  select @e, x, @por from unnest(@ests) x on conflict do nothing returning estudiante_id",
                new { e = e.Value.id, por = u.ParticipanteId, ests = res.Select(r => r.estudiante_id).ToArray() }, tx)).ToHashSet();
            var estado = await cn.QuerySingleAsync<(string estado, int vinc)>("select estado, estudiantes_vinculados from estrategias.estrategia where id=@e", new { e = e.Value.id }, tx);
            var publicNuevos = res.Where(r => nuevos.Contains(r.estudiante_id)).Select(r => r.public_id).ToArray();
            if (publicNuevos.Length > 0)
                await bit.RegistrarAsync(cn, tx, "VINCULAR", "estrategia", id.ToString(), null, new { vinculados = publicNuevos.Length, estudiantes = publicNuevos, estadoEstrategia = estado.estado });
            await tx.CommitAsync();
            return Results.Created($"/api/v1/estrategias/{id}/estudiantes",
                new { vinculados = nuevos.Count, yaVinculados = res.Count - nuevos.Count, estadoEstrategia = estado.estado, estudiantesVinculados = estado.vinc });
        });
    }

    /// <summary>RQ_008: GET /api/v1/estudiantes?tipoDoc=&numDoc= — búsqueda por huella HMAC; devuelve SOLO nombre visible, sede y municipio (RQ_026).</summary>
    public static void MapBusquedaEstudiantes(this IEndpointRouteBuilder app) =>
        app.MapGroup("/api/v1/estudiantes").RequireAuthorization(Auth.PGestor).MapGet("/", Buscar);

    static async Task<IResult> Buscar(string? tipoDoc, string? numDoc, HttpContext ctx, NpgsqlDataSource ds, IProteccionDatos prot, IBitacora bit)
    {
        var tipo = tipoDoc?.Trim().ToUpperInvariant() ?? "";
        var errores = new Dictionary<string, string[]>();
        if (!Acceso.Tipos.Contains(tipo)) errores["tipoDoc"] = [$"Tipo de documento inválido. Usa: {Acceso.TiposDocumento.Replace(",", ", ")}."];
        if (string.IsNullOrWhiteSpace(numDoc) || numDoc.Length > 20) errores["numDoc"] = ["Escribe el número de documento."];
        if (errores.Count > 0) return Problemas.Validacion(errores);
        var huella = prot.Huella(tipo, numDoc!);
        var sw = Stopwatch.StartNew();
        await using var cn = await ds.OpenConnectionAsync();
        var r = await cn.QuerySingleOrDefaultAsync<(Guid public_id, string nombre, string sede, string municipio)?>(
            @"select h.public_id, e.nombre_visible nombre, s.nombre sede, m.nombre municipio
              from maestros.estudiante_huella h join maestros.estudiante e on e.id = h.estudiante_id
              join maestros.sede s on s.id = e.sede_id join maestros.municipio m on m.id = s.municipio_id where h.huella=@huella", new { huella });
        sw.Stop();
        ctx.Response.Headers["Server-Timing"] = $"db;dur={sw.Elapsed.TotalMilliseconds:F1}";
        // Consulta de dato personal: queda en bitácora con el public_id (nunca con el documento)
        await bit.RegistrarAsync(cn, null, "BUSCAR_ESTUDIANTE", "estudiante", r?.public_id.ToString(), null, new { encontrado = r != null });
        return r == null
            ? Problemas.NoEncontrado("ESTUDIANTE_NO_ENCONTRADO", "No encontramos un estudiante con ese documento.")
            : Results.Ok(new { id = r.Value.public_id, nombre = r.Value.nombre, sede = r.Value.sede, municipio = r.Value.municipio });
    }
}
