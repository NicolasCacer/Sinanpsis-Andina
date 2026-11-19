using Dapper;
using Npgsql;
using Sses.Api.Comun;

namespace Sses.Api.Mesa;

public sealed record IncidenteCrear(string? Tipo, string? Descripcion, string? Pantalla, string? Navegador, string? Traceparent);

/// <summary>RQ_020 / CU_013 / HU_011: botón 'Ayuda' disponible en todas las pantallas, para cualquier rol.</summary>
public static class IncidentesEndpoints
{
    // RQ_020: severidad inicial por tipo (Bloqueante = Alta, Funcional = Media, Consulta = Baja)
    static readonly Dictionary<string, string> Severidad = new() { ["BLOQUEANTE"] = "ALTA", ["FUNCIONAL"] = "MEDIA", ["CONSULTA"] = "BAJA" };

    sealed class Fila
    {
        public Guid PublicId { get; set; } public string Numero { get; set; } = ""; public string Tipo { get; set; } = ""; public string Severidad { get; set; } = "";
        public string Estado { get; set; } = ""; public string Descripcion { get; set; } = ""; public string? Pantalla { get; set; }
        public DateTime CreadoEn { get; set; } public DateTime? AtendidoEn { get; set; } public DateTime? ResueltoEn { get; set; }
    }
    static DateTime? Utc(DateTime? d) => d == null ? null : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc);
    static object Dto(Fila f) => new
    {
        id = f.PublicId, numero = f.Numero, tipo = f.Tipo, severidad = f.Severidad, estado = f.Estado, descripcion = f.Descripcion, pantalla = f.Pantalla,
        creadoEn = Utc(f.CreadoEn), atendidoEn = Utc(f.AtendidoEn), resueltoEn = Utc(f.ResueltoEn)
    };
    const string Cols = "public_id, numero, tipo, severidad, estado, descripcion, pantalla, creado_en, atendido_en, resuelto_en";

    static string? Recortar(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    public static void MapIncidentes(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/incidentes").RequireAuthorization(Auth.PAutenticado);

        // RQ_020: POST /api/v1/incidentes → 201 con número INC-AAAA-NNNNNN, estado ABIERTO
        g.MapPost("/", async (IncidenteCrear dto, HttpRequest req, NpgsqlDataSource ds, IUsuarioActual ua, IBitacora bit) =>
        {
            var u = await ua.ObtenerAsync(); if (u == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado en el SSES.");
            var tipo = dto.Tipo?.Trim().ToUpperInvariant() ?? "";
            var errores = new Dictionary<string, string[]>();
            if (!Severidad.ContainsKey(tipo)) errores["tipo"] = ["Elige el tipo de inconveniente."];
            if (string.IsNullOrWhiteSpace(dto.Descripcion)) errores["descripcion"] = ["Describe lo que pasó."];
            else if (dto.Descripcion.Trim().Length > 2000) errores["descripcion"] = ["La descripción admite máximo 2.000 caracteres."];
            if (errores.Count > 0) return Problemas.Validacion(errores);
            // Contexto técnico capturado por el aplicativo (CU_013 paso 1): pantalla, navegador e identificador de correlación
            var traceparent = Recortar(dto.Traceparent, 100) ?? Recortar(req.Headers["traceparent"].FirstOrDefault(), 100);
            var navegador = Recortar(dto.Navegador, 500) ?? Recortar(req.Headers.UserAgent.ToString(), 500);
            var pub = Guid.CreateVersion7();
            await using var cn = await ds.OpenConnectionAsync(); await using var tx = await cn.BeginTransactionAsync();
            var f = await cn.QuerySingleAsync<Fila>($@"insert into mesa.incidente (public_id, participante_id, rol, tipo, severidad, descripcion, pantalla, navegador, traceparent)
                values (@pub, @pid, @rol, @tipo, @sev, @desc, @pantalla, @nav, @tp) returning {Cols}",
                new { pub, pid = u.ParticipanteId, rol = u.Rol, tipo, sev = Severidad[tipo], desc = dto.Descripcion!.Trim(), pantalla = Recortar(dto.Pantalla, 300), nav = navegador, tp = traceparent }, tx);
            await bit.RegistrarAsync(cn, tx, "REGISTRAR_INCIDENTE", "incidente", f.Numero, null, new { tipo, severidad = f.Severidad, pantalla = f.Pantalla, traceparent });
            await tx.CommitAsync();
            return Results.Created($"/api/v1/incidentes/{pub}", Dto(f));
        });

        // RQ_020: GET /api/v1/incidentes/mios — 'Mis casos', solo los del usuario
        g.MapGet("/mios", async (NpgsqlDataSource ds, IUsuarioActual ua) =>
        {
            var u = await ua.ObtenerAsync(); if (u == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado en el SSES.");
            await using var cn = await ds.OpenConnectionAsync();
            var filas = await cn.QueryAsync<Fila>($"select {Cols} from mesa.incidente where participante_id=@pid order by id desc limit 50", new { pid = u.ParticipanteId });
            return Results.Ok(new { items = filas.Select(Dto) });
        });
    }
}
