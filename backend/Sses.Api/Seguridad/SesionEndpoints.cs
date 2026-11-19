using Dapper;
using Npgsql;
using Sses.Api.Comun;
using System.Security.Claims;

namespace Sses.Api.Seguridad;

public static class SesionEndpoints
{
    record ItemMenu(string Clave, string Etiqueta, string Ruta, bool Habilitado, string? Nota);

    static ItemMenu[] MenuPara(string rol) => rol switch
    {
        Roles.Gestor => [new("estrategias", "Estrategias", "/estrategias", true, null)],
        Roles.TutorLider => [new("acompanamientos", "Acompañamientos", "/acompanamientos", true, null),
                             new("pilotaje", "Pilotaje", "/pilotaje", false, "Próximo sprint"),
                             new("ayuda", "Ayuda", "/ayuda", true, null)], // RQ_020: mesa de ayuda del pilotaje (Línea B)
        Roles.TutorDocente => [new("mi-acompanamiento", "Mi acompañamiento", "/mi-acompanamiento", false, "Próximo sprint")],
        _ => []
    };
    static readonly string[] RolesMvp = [Roles.Admin, Roles.Gestor, Roles.TutorLider, Roles.TutorDocente];
    static string? RolDe(ClaimsPrincipal u) => u.FindAll(ClaimTypes.Role).Select(c => c.Value).FirstOrDefault(r => RolesMvp.Contains(r));
    static string Etiqueta(string rol) => rol switch
    {
        Roles.Gestor => "Gestor de Estrategias", Roles.TutorLider => "Tutor Líder", Roles.TutorDocente => "Tutor Docente", Roles.Admin => "Administrador Funcional", _ => rol
    };

    public static void MapSesion(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").RequireAuthorization(Auth.PAutenticado);

        // RQ_003 / RQ_005: registra LOGIN y actualiza el último ingreso
        g.MapPost("/sesion/inicio", async (ClaimsPrincipal u, NpgsqlDataSource ds, IUsuarioActual ua, IBitacora bit) =>
        {
            var us = await ua.ObtenerAsync();
            var rol = RolDe(u);
            if (us == null || rol == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado en el SSES.");
            await using var cn = await ds.OpenConnectionAsync(); await using var tx = await cn.BeginTransactionAsync();
            var ultimo = await cn.QuerySingleAsync<DateTime?>(
                @"update seguridad.participante set ultimo_ingreso = ingreso_actual, ingreso_actual = now() where id=@id returning ultimo_ingreso", new { id = us.ParticipanteId }, tx);
            await bit.RegistrarAsync(cn, tx, "LOGIN", "participante", us.PublicId.ToString());
            await tx.CommitAsync();
            return Results.Ok(new { nombre = us.Nombre, rol, rolEtiqueta = Etiqueta(rol), ultimoIngreso = ultimo == null ? (DateTime?)null : DateTime.SpecifyKind(ultimo.Value, DateTimeKind.Utc), menu = MenuPara(rol) });
        });

        g.MapGet("/me", async (ClaimsPrincipal u, NpgsqlDataSource ds, IUsuarioActual ua) =>
        {
            var us = await ua.ObtenerAsync(); var rol = RolDe(u);
            if (us == null || rol == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado en el SSES.");
            await using var cn = await ds.OpenConnectionAsync();
            var ultimo = await cn.QuerySingleAsync<DateTime?>("select ultimo_ingreso from seguridad.participante where id=@id", new { id = us.ParticipanteId });
            return Results.Ok(new { nombre = us.Nombre, rol, rolEtiqueta = Etiqueta(rol), ultimoIngreso = ultimo == null ? (DateTime?)null : DateTime.SpecifyKind(ultimo.Value, DateTimeKind.Utc), menu = MenuPara(rol) });
        });
    }
}
