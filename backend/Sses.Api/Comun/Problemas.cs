namespace Sses.Api.Comun;

/// <summary>Errores Problem Details (RFC 9457) con `code` de negocio.</summary>
public static class Problemas
{
    public static IResult De(int status, string code, string titulo, object? extra = null)
    {
        var ext = new Dictionary<string, object?> { ["code"] = code };
        if (extra != null) foreach (var p in extra.GetType().GetProperties()) ext[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = p.GetValue(extra);
        return Results.Problem(statusCode: status, title: titulo, type: $"https://sses.sinapsis.co/errores/{code}", extensions: ext);
    }
    public static IResult Validacion(IDictionary<string, string[]> errores) =>
        Results.Problem(statusCode: 400, title: "Hay datos inválidos.", type: "https://sses.sinapsis.co/errores/VALIDACION",
            extensions: new Dictionary<string, object?> { ["code"] = "VALIDACION", ["errores"] = errores });
    public static IResult NoEncontrado(string code = "NO_ENCONTRADO", string titulo = "No encontrado.") => De(404, code, titulo);
}
