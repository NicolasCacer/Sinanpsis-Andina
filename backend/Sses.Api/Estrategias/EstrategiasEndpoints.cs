using Sses.Api.Comun;

namespace Sses.Api.Estrategias;

public static class EstrategiasEndpoints
{
    public static void MapEstrategias(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/estrategias").RequireAuthorization(Auth.PGestor);
        g.MapGet("/", () => Results.Ok(Array.Empty<object>()));
        g.MapPost("/", () => Results.StatusCode(501)); // provisional hasta F3
    }
}
