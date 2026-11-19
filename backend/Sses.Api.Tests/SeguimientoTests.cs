using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sses.Api.Tests;

[Collection("sses")]
public class SeguimientoTests(SsesFixtureInicial fx)
{
    SsesFixture F => fx.F;
    static async Task<string> Code(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
    static byte[] Jpeg(int n = 200) { var b = new byte[n]; b[0] = 0xFF; b[1] = 0xD8; b[2] = 0xFF; b[3] = 0xE0; return b; }

    [Fact] public async Task Registrar_la_semana_la_pone_Registrada_y_calcula_el_cumplimiento()
    {
        var e = await Escenario.Crear(F);
        var r = await F.Tutor().SendAsync(Escenario.Put(e.Hitos[0], e.Cuerpo(), e.Versiones[0]));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(33.33m, j.GetProperty("cumplimientoMes").GetProperty("porcentaje").GetDecimal());
        var plan = await e.Plan(); var hitos = plan.GetProperty("planes")[0].GetProperty("hitos");
        Assert.Equal("REGISTRADA", hitos[0].GetProperty("estado").GetString()); Assert.Equal("PROGRAMADA", hitos[1].GetProperty("estado").GetString());
        Assert.Equal("EN_LINEA", hitos[0].GetProperty("seguimiento").GetProperty("origen").GetString());
        Assert.Equal(3, hitos[0].GetProperty("seguimiento").GetProperty("asistencia").GetArrayLength());
        Assert.Equal(33.33m, plan.GetProperty("cumplimiento").GetProperty("porcentaje").GetDecimal());
        Assert.Equal(1, await F.Super<long>("select count(*) from auditoria.bitacora where accion='REGISTRAR_SEGUIMIENTO' and entidad_id=@id", new { id = e.Hitos[0].ToString() }));
        Assert.True(await F.Super<bool>("select ok from auditoria.verificar_cadena()"));
    }

    [Fact] public async Task CP_10_guardar_con_version_vieja_da_412()
    {
        var e = await Escenario.Crear(F);
        var r = await F.Tutor().SendAsync(Escenario.Put(e.Hitos[0], e.Cuerpo(), e.Versiones[0] - 1));
        Assert.Equal(HttpStatusCode.PreconditionFailed, r.StatusCode); Assert.Equal("VERSION_CAMBIADA", await Code(r));
        Assert.Equal("PROGRAMADA", (await e.Plan()).GetProperty("planes")[0].GetProperty("hitos")[0].GetProperty("estado").GetString());
    }

    [Fact] public async Task Semana_ya_registrada_da_409_SEMANA_CERRADA_y_sin_IfMatch_da_428()
    {
        var e = await Escenario.Crear(F); var h = F.Tutor();
        Assert.Equal(HttpStatusCode.OK, (await h.SendAsync(Escenario.Put(e.Hitos[0], e.Cuerpo(), e.Versiones[0]))).StatusCode);
        var r = await h.SendAsync(Escenario.Put(e.Hitos[0], e.Cuerpo(), await e.VersionActual(0)));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal("SEMANA_CERRADA", await Code(r));
        var sin = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/hitos/{e.Hitos[1]}/seguimiento") { Content = JsonContent.Create(e.Cuerpo()) };
        Assert.Equal((HttpStatusCode)428, (await h.SendAsync(sin)).StatusCode);
    }

    [Fact] public async Task Validaciones_asistencia_incompleta_y_textos_vacios_dan_400()
    {
        var e = await Escenario.Crear(F);
        var incompleta = new { asistencia = new[] { new { docenteId = e.Docentes[0], asistio = true } }, actividades = "a", avances = "b", evidenciaIds = Array.Empty<Guid>() };
        Assert.Equal(HttpStatusCode.BadRequest, (await F.Tutor().SendAsync(Escenario.Put(e.Hitos[0], incompleta, e.Versiones[0]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await F.Tutor().SendAsync(Escenario.Put(e.Hitos[0], e.Cuerpo(actividades: "", avances: ""), e.Versiones[0]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await F.Tutor().SendAsync(Escenario.Put(e.Hitos[0], e.Cuerpo(actividades: new string('x', 4001)), e.Versiones[0]))).StatusCode);
    }

    [Fact] public async Task Otro_tutor_no_puede_registrar_ni_ver_el_hito()
    {
        var e = await Escenario.Crear(F);
        Assert.Equal(HttpStatusCode.NotFound, (await F.Tutor("tutor2").SendAsync(Escenario.Put(e.Hitos[0], e.Cuerpo(), e.Versiones[0]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await F.Tutor("tutor2").GetAsync($"/api/v1/hitos/{e.Hitos[0]}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await F.Tutor().GetAsync($"/api/v1/hitos/{e.Hitos[0]}")).StatusCode);
    }

    [Fact] public async Task Evidencias_se_suben_se_adjuntan_y_se_validan()
    {
        var e = await Escenario.Crear(F); var h = F.Tutor();
        async Task<HttpResponseMessage> Subir(byte[] b) { var f = new MultipartFormDataContent(); f.Add(new ByteArrayContent(b), "archivo", "foto.jpg"); return await h.PostAsync("/api/v1/evidencias", f); }
        var ok = await Subir(Jpeg()); Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var evId = (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("evidenciaId").GetGuid();
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Subir(Jpeg(1_048_577))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Subir(new byte[] { 1, 2, 3, 4 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.SendAsync(Escenario.Put(e.Hitos[0], e.Cuerpo(evidencias: [evId]), e.Versiones[0]))).StatusCode);
        var seg = (await e.Plan()).GetProperty("planes")[0].GetProperty("hitos")[0].GetProperty("seguimiento");
        Assert.Equal(1, seg.GetProperty("evidencias").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await h.GetAsync($"/api/v1/evidencias/{evId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await F.Tutor("tutor2").GetAsync($"/api/v1/evidencias/{evId}")).StatusCode);
    }
}
