using System.Security.Cryptography;
using Dapper;
using Npgsql;
using Sses.Api.Comun;

namespace Sses.Api.Acompanamiento;

/// <summary>D07: evidencias en volumen local (./data/evidencias) en lugar de Blob Storage.</summary>
public static class Evidencias
{
    public const int MaxBytes = 1_048_576;

    public static (string mime, string ext)? Detectar(ReadOnlySpan<byte> b)
    {
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ("image/jpeg", "jpg");
        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ("image/webp", "webp");
        return null;
    }

    /// <summary>Valida (tipo real y ≤ 1 MB), escribe el archivo y registra la fila. Devuelve (publicId, uri) o un error de negocio.</summary>
    public static async Task<(Guid id, string uri)?> Guardar(NpgsqlConnection cn, NpgsqlTransaction? tx, IConfiguration cfg, long participanteId, byte[] datos)
    {
        if (datos.Length == 0 || datos.Length > MaxBytes) return null;
        var tipo = Detectar(datos); if (tipo == null) return null;
        var pub = Guid.CreateVersion7(); var nombre = $"{pub}.{tipo.Value.ext}";
        var ruta = cfg["Evidencias:Ruta"] ?? Path.Combine(AppContext.BaseDirectory, "data", "evidencias");
        Directory.CreateDirectory(ruta);
        await File.WriteAllBytesAsync(Path.Combine(ruta, nombre), datos);
        var uri = $"/api/v1/evidencias/{pub}";
        await cn.ExecuteAsync(@"insert into acompanamiento.evidencia (public_id,subido_por,uri,tipo_mime,bytes,sha256) values (@p,@u,@uri,@m,@b,@h)",
            new { p = pub, u = participanteId, uri, m = tipo.Value.mime, b = datos.Length, h = Convert.ToHexString(SHA256.HashData(datos)).ToLowerInvariant() }, tx);
        return (pub, uri);
    }

    public static void MapEvidencias(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/evidencias").RequireAuthorization(Auth.PTutorLider);

        // RQ_011: 1 imagen ≤ 1 MB JPEG/WebP (el front la reduce antes de subir)
        g.MapPost("/", async (HttpRequest req, NpgsqlDataSource ds, IUsuarioActual ua, IConfiguration cfg) =>
        {
            var u = await ua.ObtenerAsync(); if (u == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            if (!req.HasFormContentType) return Problemas.Validacion(new Dictionary<string, string[]> { ["archivo"] = ["Envía la imagen como multipart/form-data."] });
            var form = await req.ReadFormAsync(); var f = form.Files.FirstOrDefault();
            if (f == null) return Problemas.Validacion(new Dictionary<string, string[]> { ["archivo"] = ["Falta la imagen."] });
            if (f.Length > MaxBytes) return Problemas.De(413, "IMAGEN_MUY_GRANDE", "La imagen supera 1 MB.");
            using var ms = new MemoryStream(); await f.CopyToAsync(ms);
            await using var cn = await ds.OpenConnectionAsync();
            var r = await Guardar(cn, null, cfg, u.ParticipanteId, ms.ToArray());
            return r == null ? Problemas.De(415, "IMAGEN_INVALIDA", "Solo se admiten imágenes JPEG o WebP de máximo 1 MB.") : Results.Created(r.Value.uri, new { evidenciaId = r.Value.id, uri = r.Value.uri });
        }).DisableAntiforgery();

        g.MapGet("/{id:guid}", async (Guid id, NpgsqlDataSource ds, IUsuarioActual ua, IConfiguration cfg) =>
        {
            var u = await ua.ObtenerAsync(); if (u == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            await using var cn = await ds.OpenConnectionAsync();
            var e = await cn.QuerySingleOrDefaultAsync<(string tipo_mime, long subido_por)?>(
                @"select tipo_mime, subido_por from acompanamiento.evidencia ev where public_id=@id and (subido_por=@u or exists (
                    select 1 from acompanamiento.seguimiento_semanal s join acompanamiento.hito_semanal h on h.id=s.hito_id join acompanamiento.plan p on p.id=h.plan_id
                    where s.id=ev.seguimiento_id and p.tutor_id=@t))", new { id, u = u.ParticipanteId, t = u.PersonaId });
            if (e == null) return Problemas.NoEncontrado("EVIDENCIA_NO_ENCONTRADA", "La evidencia no existe.");
            var ruta = Path.Combine(cfg["Evidencias:Ruta"] ?? Path.Combine(AppContext.BaseDirectory, "data", "evidencias"), $"{id}.{(e.Value.tipo_mime == "image/webp" ? "webp" : "jpg")}");
            return File.Exists(ruta) ? Results.File(ruta, e.Value.tipo_mime) : Problemas.NoEncontrado("EVIDENCIA_NO_ENCONTRADA", "La evidencia no existe.");
        });
    }
}
