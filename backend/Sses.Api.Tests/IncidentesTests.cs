using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sses.Api.Tests;

/// <summary>CP_023 / RQ_020: incidente desde el botón 'Ayuda' con captura de contexto.</summary>
[Collection("sses")]
public class IncidentesTests(SsesFixtureInicial fx)
{
    SsesFixture F => fx.F;
    const string Tp = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    [Fact] public async Task Registra_incidente_con_numero_severidad_y_contexto()
    {
        var r = await F.Tutor().PostAsJsonAsync("/api/v1/incidentes", new { tipo = "BLOQUEANTE", descripcion = "No carga el plan", pantalla = "/acompanamientos", navegador = "Chrome Android", traceparent = Tp });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var b = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Matches(new Regex(@"^INC-\d{4}-\d{6}$"), b.GetProperty("numero").GetString());
        Assert.Equal("ALTA", b.GetProperty("severidad").GetString());
        Assert.Equal("ABIERTO", b.GetProperty("estado").GetString());
        var fila = await F.Super<string>("select pantalla || '|' || navegador || '|' || traceparent from mesa.incidente where numero=@n", new { n = b.GetProperty("numero").GetString() });
        Assert.Equal($"/acompanamientos|Chrome Android|{Tp}", fila);
        Assert.Equal(1L, await F.Super<long>("select count(*) from auditoria.bitacora where accion='REGISTRAR_INCIDENTE' and entidad_id=@n", new { n = b.GetProperty("numero").GetString() }));
    }

    [Fact] public async Task Severidad_por_tipo_y_validacion()
    {
        var f = await (await F.Gestor().PostAsJsonAsync("/api/v1/incidentes", new { tipo = "funcional", descripcion = "x" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MEDIA", f.GetProperty("severidad").GetString());
        var c = await (await F.Gestor().PostAsJsonAsync("/api/v1/incidentes", new { tipo = "CONSULTA", descripcion = "x" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BAJA", c.GetProperty("severidad").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await F.Gestor().PostAsJsonAsync("/api/v1/incidentes", new { tipo = "OTRO", descripcion = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await F.Gestor().PostAsJsonAsync("/api/v1/incidentes", new { tipo = "CONSULTA", descripcion = " " })).StatusCode);
    }

    [Fact] public async Task Mis_casos_solo_muestra_los_propios()
    {
        var mio = (await (await F.Tutor("tutor2").PostAsJsonAsync("/api/v1/incidentes", new { tipo = "CONSULTA", descripcion = "caso de tutor2" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("numero").GetString();
        var propios = (await F.Tutor("tutor2").GetFromJsonAsync<JsonElement>("/api/v1/incidentes/mios")).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("numero").GetString()).ToList();
        var ajenos = (await F.Tutor("tutor1").GetFromJsonAsync<JsonElement>("/api/v1/incidentes/mios")).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("numero").GetString()).ToList();
        Assert.Contains(mio, propios); Assert.DoesNotContain(mio, ajenos);
    }

    [Fact] public async Task Sin_sesion_da_401()
    {
        var anon = F.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/v1/incidentes", new { tipo = "CONSULTA", descripcion = "x" })).StatusCode);
    }
}
