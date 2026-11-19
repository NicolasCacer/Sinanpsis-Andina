using System.Security.Claims;
using Dapper;
using Npgsql;

namespace Sses.Api.Comun;

public sealed record Usuario(long ParticipanteId, Guid PublicId, string Sub, string Correo, string Nombre, string Rol, long? PersonaId);

/// <summary>Resuelve al participante (tabla seguridad.participante) a partir del `sub` del token.</summary>
public interface IUsuarioActual { Task<Usuario?> ObtenerAsync(CancellationToken ct = default); }

public sealed class UsuarioActual(IHttpContextAccessor http, NpgsqlDataSource ds) : IUsuarioActual
{
    private Usuario? _cache; private bool _cargado;
    public async Task<Usuario?> ObtenerAsync(CancellationToken ct = default)
    {
        if (_cargado) return _cache;
        var sub = http.HttpContext?.User.FindFirstValue("sub") ?? http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (sub != null)
        {
            await using var cn = await ds.OpenConnectionAsync(ct);
            var r = await cn.QuerySingleOrDefaultAsync<(long id, Guid public_id, string correo, string nombre, string rol, long? persona_id)?>(
                "select id, public_id, correo, nombre, rol, persona_id from seguridad.participante where keycloak_sub=@sub", new { sub });
            if (r != null) _cache = new Usuario(r.Value.id, r.Value.public_id, sub, r.Value.correo, r.Value.nombre, r.Value.rol, r.Value.persona_id);
        }
        _cargado = true; return _cache;
    }
}
