using Dapper;
using FluentValidation;
using Npgsql;
using Sses.Api.Comun;

namespace Sses.Api.Estrategias;

public static class EstrategiasEndpoints
{
    sealed class Fila
    {
        public long Id { get; set; } public Guid PublicId { get; set; } public Guid EntidadPublicId { get; set; } public string EntidadNombre { get; set; } = "";
        public string Nombre { get; set; } = ""; public string Objetivo { get; set; } = ""; public string Poblacion { get; set; } = "";
        public DateOnly FechaInicio { get; set; } public DateOnly FechaFin { get; set; }
        public string Estado { get; set; } = ""; public int EstudiantesVinculados { get; set; } public long Version { get; set; }
        public DateTime CreadoEn { get; set; } public DateTime? ActualizadoEn { get; set; }
    }
    static object Dto(Fila f) => new
    {
        id = f.PublicId, entidad = new { id = f.EntidadPublicId, nombre = f.EntidadNombre },
        nombre = f.Nombre, objetivo = f.Objetivo, poblacion = f.Poblacion, fechaInicio = f.FechaInicio, fechaFin = f.FechaFin,
        estado = f.Estado, editable = f.Estado == "EDITABLE" && f.EstudiantesVinculados == 0, estudiantesVinculados = f.EstudiantesVinculados,
        creadoEn = DateTime.SpecifyKind(f.CreadoEn, DateTimeKind.Utc)
    };
    // Sin la parte de FOR UPDATE: la tabla de entidad se une aparte para poder bloquear solo la fila de estrategia
    const string Cols = @"e.id, e.public_id, en.public_id entidad_public_id, en.nombre entidad_nombre, e.nombre, e.objetivo, e.poblacion, e.fecha_inicio, e.fecha_fin,
        e.estado, e.estudiantes_vinculados, e.xmin::text::bigint version, e.creado_en, e.actualizado_en";
    const string Desde = "from estrategias.estrategia e join maestros.entidad en on en.id = e.entidad_id";

    public static void MapEstrategias(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/estrategias").RequireAuthorization(Auth.PGestor);

        // RQ_006: listado keyset (50 por página), solo entidades del gestor
        g.MapGet("/", async (NpgsqlDataSource ds, IUsuarioActual ua, Guid? entidadId, string? estado, string? despuesDe) =>
        {
            var u = await ua.ObtenerAsync(); if (u == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            if (estado != null && estado != "EDITABLE" && estado != "BLOQUEADA") return Problemas.Validacion(new Dictionary<string, string[]> { ["estado"] = ["Estado inválido."] });
            long? cursor = long.TryParse(despuesDe, out var c) ? c : null;
            await using var cn = await ds.OpenConnectionAsync();
            var filas = (await cn.QueryAsync<Fila>($@"select {Cols} {Desde}
                where {Acceso.FiltroEntidadGestor}
                  and (@entidad::uuid is null or en.public_id = @entidad) and (@estado::text is null or e.estado = @estado)
                  and (@cursor::bigint is null or e.id < @cursor)
                order by e.id desc limit 51", new { pid = u.ParticipanteId, entidad = entidadId, estado, cursor })).ToList();
            var hayMas = filas.Count > 50; if (hayMas) filas.RemoveAt(50);
            return Results.Ok(new { items = filas.Select(Dto), siguiente = hayMas ? filas[^1].Id.ToString() : null });
        });

        // RQ_006
        g.MapPost("/", async (EstrategiaCrear dto, IValidator<EstrategiaCrear> val, NpgsqlDataSource ds, IUsuarioActual ua, IBitacora bit) =>
        {
            var u = await ua.ObtenerAsync(); if (u == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            var v = await val.ValidateAsync(dto);
            if (!v.IsValid) return Problemas.Validacion(v.ToDictionary());
            await using var cn = await ds.OpenConnectionAsync(); await using var tx = await cn.BeginTransactionAsync();
            var ent = await cn.QuerySingleOrDefaultAsync<(long id, bool activa)?>(
                @"select e.id, e.activa from maestros.entidad e where e.public_id=@p and e.id in (select entidad_id from seguridad.gestor_entidad where participante_id = @pid)", new { p = dto.EntidadId, pid = u.ParticipanteId }, tx);
            if (ent == null) return Problemas.NoEncontrado("ENTIDAD_NO_ENCONTRADA", "La entidad no existe o no la administras.");
            if (!ent.Value.activa) return Problemas.De(422, "ENTIDAD_INACTIVA", "La entidad está inactiva.");
            var pub = Guid.CreateVersion7();
            try
            {
                await cn.ExecuteAsync(@"insert into estrategias.estrategia (public_id,entidad_id,nombre,objetivo,poblacion,fecha_inicio,fecha_fin,creado_por)
                    values (@pub,@ent,@nombre,@objetivo,@poblacion,@fi,@ff,@por)",
                    new { pub, ent = ent.Value.id, nombre = dto.Nombre!.Trim(), objetivo = dto.Objetivo!.Trim(), poblacion = dto.Poblacion!.Trim(), fi = dto.FechaInicio, ff = dto.FechaFin, por = u.ParticipanteId }, tx);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            { return Problemas.De(409, "NOMBRE_DUPLICADO", "Ya existe una estrategia con ese nombre en la entidad."); }
            var f = await cn.QuerySingleAsync<Fila>($"select {Cols} {Desde} where e.public_id=@pub", new { pub }, tx);
            await bit.RegistrarAsync(cn, tx, "CREAR_ESTRATEGIA", "estrategia", pub.ToString(), null, new { dto.Nombre, entidadId = dto.EntidadId, dto.FechaInicio, dto.FechaFin });
            await tx.CommitAsync();
            return Con(Results.Created($"/api/v1/estrategias/{pub}", Dto(f)), Datos.Etag(f.Version));
        });

        g.MapGet("/{id:guid}", async (Guid id, NpgsqlDataSource ds, IUsuarioActual ua) =>
        {
            var u = await ua.ObtenerAsync(); if (u == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            await using var cn = await ds.OpenConnectionAsync();
            var f = await cn.QuerySingleOrDefaultAsync<Fila>($"select {Cols} {Desde} where e.public_id=@id and {Acceso.FiltroEntidadGestor}", new { id, pid = u.ParticipanteId });
            return f == null ? Problemas.NoEncontrado("ESTRATEGIA_NO_ENCONTRADA", "La estrategia no existe.") : Con(Results.Ok(Dto(f)), Datos.Etag(f.Version));
        });

        // RQ_007: edición solo con 0 vinculados. API (FOR UPDATE + If-Match) y trigger en BD.
        g.MapPut("/{id:guid}", async (Guid id, EstrategiaEditar dto, HttpRequest req, IValidator<EstrategiaEditar> val, NpgsqlDataSource ds, IUsuarioActual ua, IBitacora bit) =>
        {
            var u = await ua.ObtenerAsync(); if (u == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            var ifMatch = req.Headers.IfMatch.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(ifMatch)) return Problemas.De(428, "IF_MATCH_REQUERIDO", "Falta la cabecera If-Match.");
            var v = await val.ValidateAsync(dto);
            if (!v.IsValid) return Problemas.Validacion(v.ToDictionary());
            await using var cn = await ds.OpenConnectionAsync(); await using var tx = await cn.BeginTransactionAsync();
            var f = await cn.QuerySingleOrDefaultAsync<Fila>($@"select {Cols} {Desde} where e.public_id=@id and {Acceso.FiltroEntidadGestor} for update of e", new { id, pid = u.ParticipanteId }, tx);
            if (f == null) return Problemas.NoEncontrado("ESTRATEGIA_NO_ENCONTRADA", "La estrategia no existe.");
            var bloqueada = f.Estado == "BLOQUEADA" || f.EstudiantesVinculados > 0 || await cn.ExecuteScalarAsync<bool>("select exists(select 1 from estrategias.estrategia_estudiante where estrategia_id=@i)", new { i = f.Id }, tx);
            if (bloqueada) return Problemas.De(409, "ESTRATEGIA_BLOQUEADA", "Bloqueada para edición: la estrategia ya tiene estudiantes vinculados.");
            if (Datos.ParseEtag(ifMatch) != f.Version) return Problemas.De(412, "VERSION_CAMBIADA", "La estrategia fue modificada por otra persona, recarga.");
            long nuevaVersion;
            try
            {
                nuevaVersion = await cn.ExecuteScalarAsync<long>(@"update estrategias.estrategia set nombre=@n, objetivo=@o, poblacion=@p, fecha_inicio=@fi, fecha_fin=@ff, actualizado_en=now()
                    where id=@i returning xmin::text::bigint", new { n = dto.Nombre!.Trim(), o = dto.Objetivo!.Trim(), p = dto.Poblacion!.Trim(), fi = dto.FechaInicio, ff = dto.FechaFin, i = f.Id }, tx);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505") { return Problemas.De(409, "NOMBRE_DUPLICADO", "Ya existe una estrategia con ese nombre en la entidad."); }
            catch (PostgresException ex) when (ex.SqlState == "P0001") { return Problemas.De(409, "ESTRATEGIA_BLOQUEADA", "Bloqueada para edición: la estrategia ya tiene estudiantes vinculados."); }
            await bit.RegistrarAsync(cn, tx, "EDITAR_ESTRATEGIA", "estrategia", id.ToString(),
                new { f.Nombre, f.Objetivo, f.Poblacion, f.FechaInicio, f.FechaFin }, new { dto.Nombre, dto.Objetivo, dto.Poblacion, dto.FechaInicio, dto.FechaFin });
            await tx.CommitAsync();
            await using var cn2 = await ds.OpenConnectionAsync();
            var nueva = await cn2.QuerySingleAsync<Fila>($"select {Cols} {Desde} where e.public_id=@id", new { id });
            return Con(Results.Ok(Dto(nueva)), Datos.Etag(nuevaVersion));
        });

        g.MapVinculacion();
    }

    /// <summary>Añade cabecera ETag a una respuesta.</summary>
    internal static IResult Con(IResult r, string etag) => new ConEtag(r, etag);
    sealed class ConEtag(IResult inner, string etag) : IResult
    {
        public Task ExecuteAsync(HttpContext ctx) { ctx.Response.Headers.ETag = etag; return inner.ExecuteAsync(ctx); }
    }
}
