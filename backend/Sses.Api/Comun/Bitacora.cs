using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace Sses.Api.Comun;

/// <summary>RQ_005: inserta en auditoria.bitacora (solo inserción; el hash lo calcula el trigger de la BD).</summary>
public interface IBitacora
{
    Task RegistrarAsync(NpgsqlConnection cn, NpgsqlTransaction? tx, string accion, string? entidad = null, string? entidadId = null, object? antes = null, object? despues = null);
    Task RegistrarAsync(string accion, string? entidad = null, string? entidadId = null, object? antes = null, object? despues = null);
}

public sealed class Bitacora(IHttpContextAccessor http, NpgsqlDataSource ds) : IBitacora
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    public async Task RegistrarAsync(string accion, string? entidad = null, string? entidadId = null, object? antes = null, object? despues = null)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await RegistrarAsync(cn, null, accion, entidad, entidadId, antes, despues);
    }

    public Task RegistrarAsync(NpgsqlConnection cn, NpgsqlTransaction? tx, string accion, string? entidad = null, string? entidadId = null, object? antes = null, object? despues = null)
    {
        var ctx = http.HttpContext; var u = ctx?.User;
        var usuario = u?.FindFirstValue("preferred_username") ?? u?.FindFirstValue("email") ?? u?.FindFirstValue("sub") ?? "anonimo";
        var rol = u?.FindFirstValue(ClaimTypes.Role) ?? "SIN_ROL";
        var tp = Activity.Current?.Id ?? ctx?.Request.Headers["traceparent"].FirstOrDefault();
        return cn.ExecuteAsync(@"insert into auditoria.bitacora (usuario,rol,ip,accion,entidad,entidad_id,antes,despues,traceparent,hash)
            values (@usuario,@rol,@ip::inet,@accion,@entidad,@entidadId,@antes::jsonb,@despues::jsonb,@tp,'\x00'::bytea)",
            new
            {
                usuario, rol, ip = ctx?.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? ctx?.Connection.RemoteIpAddress?.ToString(),
                accion, entidad, entidadId,
                antes = antes == null ? null : JsonSerializer.Serialize(antes, J),
                despues = despues == null ? null : JsonSerializer.Serialize(despues, J), tp
            }, tx);
    }
}
