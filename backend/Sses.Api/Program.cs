using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapGet("/health/live", () => Results.Ok(new { estado = "vivo" }));

// /health/ready: base de datos + JWKS de Keycloak
app.MapGet("/health/ready", async (IConfiguration cfg, IHttpClientFactory http, CancellationToken ct) =>
{
    var problemas = new List<string>();
    try
    {
        await using var cn = new NpgsqlConnection(cfg.GetConnectionString("Sses"));
        await cn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select 1", cn);
        await cmd.ExecuteScalarAsync(ct);
    }
    catch (Exception ex) { problemas.Add("db: " + ex.GetType().Name); }
    try
    {
        var meta = cfg["Keycloak:MetadataAddress"];
        if (!string.IsNullOrEmpty(meta))
        {
            var r = await http.CreateClient().GetAsync(meta, ct);
            if (!r.IsSuccessStatusCode) problemas.Add("keycloak: " + (int)r.StatusCode);
        }
    }
    catch (Exception ex) { problemas.Add("keycloak: " + ex.GetType().Name); }
    return problemas.Count == 0
        ? Results.Ok(new { estado = "listo" })
        : Results.Json(new { estado = "no_listo", problemas }, statusCode: 503);
});

app.Run();

public partial class Program;
