using Dapper;
using Npgsql;

namespace Sses.Api.Comun;

public static class Acceso
{
    public const string TiposDocumento = "RC,TI,CC,CE,PPT,NUIP";
    public static readonly HashSet<string> Tipos = new(TiposDocumento.Split(','));

    /// <summary>Filas visibles para el gestor: solo sus entidades (seguridad.gestor_entidad). Otra entidad → no existe (404).</summary>
    public const string FiltroEntidadGestor = "e.entidad_id in (select entidad_id from seguridad.gestor_entidad where participante_id = @pid)";

    public static async Task<IResult?> UsuarioOError(IUsuarioActual ua, Action<Usuario> cuando)
    {
        var u = await ua.ObtenerAsync();
        if (u == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado en el SSES.");
        cuando(u); return null;
    }
}
