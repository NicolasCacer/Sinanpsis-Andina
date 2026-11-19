using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Sses.Api.Tests;

[Collection("sses")]
public class EstrategiasTests(SsesFixtureInicial fx)
{
    SsesFixture F => fx.F;
    static readonly Guid E1 = new("00000000-0000-7000-8000-0000000000e1"), E3 = new("00000000-0000-7000-8000-0000000000e3"), E4 = new("00000000-0000-7000-8000-0000000000e4");
    static object Cuerpo(string nombre, Guid? ent = null) => new { entidadId = ent ?? E1, nombre, objetivo = "Objetivo de prueba", poblacion = "Niños 8-12", fechaInicio = "2026-08-01", fechaFin = "2027-05-31" };

    async Task<(JsonElement dto, string etag)> Crear(HttpClient h, string nombre, Guid? ent = null)
    {
        var r = await h.PostAsJsonAsync("/api/v1/estrategias", Cuerpo(nombre, ent));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.NotNull(r.Headers.Location);
        return (await r.Content.ReadFromJsonAsync<JsonElement>(), r.Headers.ETag!.ToString());
    }
    static async Task<string> Code(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
    static HttpRequestMessage Put(string id, object body, string? etag)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/estrategias/{id}") { Content = JsonContent.Create(body) };
        if (etag != null) req.Headers.TryAddWithoutValidation("If-Match", etag);
        return req;
    }

    [Fact] public async Task Crear_deja_la_estrategia_Editable_con_contador_en_cero()
    {
        var (dto, etag) = await Crear(F.Gestor(), "Robótica " + Guid.NewGuid());
        Assert.Equal("EDITABLE", dto.GetProperty("estado").GetString()); Assert.True(dto.GetProperty("editable").GetBoolean());
        Assert.Equal(0, dto.GetProperty("estudiantesVinculados").GetInt32()); Assert.StartsWith("W/\"", etag);
    }

    [Fact] public async Task CP_03_nombre_duplicado_con_otra_capitalizacion_da_409()
    {
        var n = "Ajedrez " + Guid.NewGuid(); var h = F.Gestor();
        await Crear(h, n);
        var r = await h.PostAsJsonAsync("/api/v1/estrategias", Cuerpo(n.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal("NOMBRE_DUPLICADO", await Code(r));
    }

    [Fact] public async Task Fecha_fin_menor_o_igual_al_inicio_da_400()
    {
        var r = await F.Gestor().PostAsJsonAsync("/api/v1/estrategias", new { entidadId = E1, nombre = "X " + Guid.NewGuid(), objetivo = "o", poblacion = "p", fechaInicio = "2026-09-01", fechaFin = "2026-09-01" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("posterior", await r.Content.ReadAsStringAsync());
    }

    [Fact] public async Task Entidad_inactiva_da_422_y_entidad_ajena_da_404()
    {
        var h = F.Gestor();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await h.PostAsJsonAsync("/api/v1/estrategias", Cuerpo("I " + Guid.NewGuid(), E3))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.PostAsJsonAsync("/api/v1/estrategias", Cuerpo("A " + Guid.NewGuid(), E4))).StatusCode);
    }

    [Fact] public async Task Editar_con_cero_vinculados_funciona_y_cambia_el_ETag()
    {
        var h = F.Gestor(); var (dto, etag) = await Crear(h, "Editable " + Guid.NewGuid()); var id = dto.GetProperty("id").GetString()!;
        var r = await h.SendAsync(Put(id, new { nombre = "Nuevo " + Guid.NewGuid(), objetivo = "Otro", poblacion = "Otra", fechaInicio = "2026-08-01", fechaFin = "2027-06-30" }, etag));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); Assert.NotEqual(etag, r.Headers.ETag!.ToString());
    }

    [Fact] public async Task Editar_sin_IfMatch_da_428()
    {
        var h = F.Gestor(); var (dto, _) = await Crear(h, "S " + Guid.NewGuid());
        var r = await h.SendAsync(Put(dto.GetProperty("id").GetString()!, new { nombre = "n", objetivo = "o", poblacion = "p", fechaInicio = "2026-08-01", fechaFin = "2027-05-31" }, null));
        Assert.Equal((HttpStatusCode)428, r.StatusCode);
    }

    [Fact] public async Task Version_vieja_da_412()
    {
        var h = F.Gestor(); var (dto, etag) = await Crear(h, "V " + Guid.NewGuid()); var id = dto.GetProperty("id").GetString()!;
        var cuerpo = new { nombre = "V2 " + Guid.NewGuid(), objetivo = "o", poblacion = "p", fechaInicio = "2026-08-01", fechaFin = "2027-05-31" };
        Assert.Equal(HttpStatusCode.OK, (await h.SendAsync(Put(id, cuerpo, etag))).StatusCode);
        var r = await h.SendAsync(Put(id, cuerpo with { nombre = "V3 " + Guid.NewGuid() }, etag));
        Assert.Equal(HttpStatusCode.PreconditionFailed, r.StatusCode); Assert.Equal("VERSION_CAMBIADA", await Code(r));
    }

    [Fact] public async Task CP_04_editar_con_un_vinculado_da_409_y_la_ficha_queda_Bloqueada()
    {
        var h = F.Gestor(); var (dto, etag) = await Crear(h, "B " + Guid.NewGuid()); var id = dto.GetProperty("id").GetString()!;
        await F.SuperExec("insert into estrategias.estrategia_estudiante select e.id, 1, 1 from estrategias.estrategia e where e.public_id=@id", new { id = Guid.Parse(id) });
        var r = await h.SendAsync(Put(id, new { nombre = "Cambio", objetivo = "o", poblacion = "p", fechaInicio = "2026-08-01", fechaFin = "2027-05-31" }, etag));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal("ESTRATEGIA_BLOQUEADA", await Code(r));
        var ficha = await (await h.GetAsync($"/api/v1/estrategias/{id}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BLOQUEADA", ficha.GetProperty("estado").GetString()); Assert.False(ficha.GetProperty("editable").GetBoolean());
        Assert.Equal(1, ficha.GetProperty("estudiantesVinculados").GetInt32());
    }

    [Fact] public async Task CP_05_A_abre_edicion_B_vincula_A_guarda_da_409()
    {
        var a = F.Gestor("gestorA"); var b = F.Gestor("gestorB");
        var (dto, etagA) = await Crear(a, "Carrera " + Guid.NewGuid()); var id = dto.GetProperty("id").GetString()!;
        var rb = await b.PostAsJsonAsync($"/api/v1/estrategias/{id}/estudiantes", new { estudianteIds = new[] { "00000000-0000-7000-8000-000000000051" } });
        Assert.Equal(HttpStatusCode.Created, rb.StatusCode);
        var ra = await a.SendAsync(Put(id, new { nombre = "Tarde", objetivo = "o", poblacion = "p", fechaInicio = "2026-08-01", fechaFin = "2027-05-31" }, etagA));
        Assert.Equal(HttpStatusCode.Conflict, ra.StatusCode); Assert.Equal("ESTRATEGIA_BLOQUEADA", await Code(ra));
    }

    [Fact] public async Task CP_06_UPDATE_directo_por_SQL_en_estrategia_bloqueada_falla_con_P0001()
    {
        var (dto, _) = await Crear(F.Gestor(), "T " + Guid.NewGuid()); var id = Guid.Parse(dto.GetProperty("id").GetString()!);
        await F.SuperExec("insert into estrategias.estrategia_estudiante select e.id, 2, 1 from estrategias.estrategia e where e.public_id=@id", new { id });
        var ex = await Assert.ThrowsAsync<PostgresException>(() => F.SuperExec("update estrategias.estrategia set nombre='hack' where public_id=@id", new { id }));
        Assert.Equal("P0001", ex.SqlState); Assert.Contains("ESTRATEGIA_BLOQUEADA", ex.MessageText);
        // aun como el rol de la API
        await using var c = new NpgsqlConnection(F.ConnApi); await c.OpenAsync();
        var ex2 = await Assert.ThrowsAsync<PostgresException>(async () => { await using var cmd = new NpgsqlCommand("update estrategias.estrategia set objetivo='x' where public_id=@id", c); cmd.Parameters.AddWithValue("id", id); await cmd.ExecuteNonQueryAsync(); });
        Assert.Equal("P0001", ex2.SqlState);
    }

    [Fact] public async Task El_listado_solo_muestra_estrategias_de_las_entidades_del_gestor()
    {
        var (dto, _) = await Crear(F.Gestor("gestorA"), "Visible " + Guid.NewGuid());
        var lista = await (await F.Gestor("gestorA").GetAsync("/api/v1/estrategias")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(lista.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetString() == dto.GetProperty("id").GetString());
        var ajeno = await (await F.Gestor("gestorC").GetAsync("/api/v1/estrategias")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(ajeno.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetString() == dto.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await F.Gestor("gestorC").GetAsync($"/api/v1/estrategias/{dto.GetProperty("id").GetString()}")).StatusCode);
    }

    [Fact] public async Task CP_02_el_tutor_no_puede_crear_estrategias_y_queda_ACCESO_DENEGADO_en_bitacora()
    {
        var antes = await F.Super<long>("select count(*) from auditoria.bitacora where accion='ACCESO_DENEGADO'");
        var r = await F.Tutor().PostAsJsonAsync("/api/v1/estrategias", Cuerpo("No " + Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(antes + 1, await F.Super<long>("select count(*) from auditoria.bitacora where accion='ACCESO_DENEGADO'"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await F.Factory.CreateClient().GetAsync("/api/v1/estrategias")).StatusCode);
    }

    [Fact] public async Task Crear_y_editar_quedan_en_bitacora_y_la_cadena_es_valida()
    {
        var h = F.Gestor(); var (dto, etag) = await Crear(h, "Aud " + Guid.NewGuid()); var id = dto.GetProperty("id").GetString()!;
        await h.SendAsync(Put(id, new { nombre = "Aud2 " + Guid.NewGuid(), objetivo = "o", poblacion = "p", fechaInicio = "2026-08-01", fechaFin = "2027-05-31" }, etag));
        Assert.Equal(1, await F.Super<long>("select count(*) from auditoria.bitacora where accion='CREAR_ESTRATEGIA' and entidad_id=@id", new { id }));
        Assert.Equal(1, await F.Super<long>("select count(*) from auditoria.bitacora where accion='EDITAR_ESTRATEGIA' and entidad_id=@id and antes is not null and despues is not null", new { id }));
        Assert.True(await F.Super<bool>("select ok from auditoria.verificar_cadena()"));
    }
}
