using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sses.Api.Tests;

/// <summary>RQ_016 / CP_019: confirmación del acompañamiento por el Tutor Docente.</summary>
[Collection("sses")]
public class ConfirmacionDocenteTests(SsesFixtureInicial fx)
{
    SsesFixture F => fx.F;
    static Task<HttpResponseMessage> Confirmar(HttpClient h, Guid hito, object cuerpo, Guid? key = null)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/seguimientos/{hito}/confirmaciones") { Content = JsonContent.Create(cuerpo) };
        r.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString()); return h.SendAsync(r);
    }
    async Task Registrar(Escenario e, int i) =>
        Assert.Equal(HttpStatusCode.OK, (await F.Tutor().SendAsync(Escenario.Put(e.Hitos[i], e.Cuerpo(), await e.VersionActual(i)))).StatusCode);
    async Task<string> Estado(Escenario e, int i) => (await e.Plan()).GetProperty("planes")[0].GetProperty("hitos")[i].GetProperty("estado").GetString()!;

    [Fact] public async Task Confirmar_semana_registrada_da_201_y_estado_CONFIRMADO_con_bitacora()
    {
        var e = await Escenario.Crear(F); await Registrar(e, 0);
        var r = await Confirmar(F.Docente(), e.Hitos[0], new { confirmado = true });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("CONFIRMADO", await Estado(e, 0));
        Assert.Equal(1, await F.Super<long>("select count(*) from auditoria.bitacora where accion='CONFIRMAR_ACOMPANAMIENTO' and entidad_id=@i", new { i = e.Hitos[0].ToString() }));
    }

    [Fact] public async Task Misma_Idempotency_Key_da_200_sin_filas_nuevas()
    {
        var e = await Escenario.Crear(F); await Registrar(e, 0); var k = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Confirmar(F.Docente(), e.Hitos[0], new { confirmado = true }, k)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Confirmar(F.Docente(), e.Hitos[0], new { confirmado = true }, k)).StatusCode);
        Assert.Equal(1, await F.Super<long>("select count(*) from acompanamiento.confirmacion_docente c join acompanamiento.hito_semanal h on h.id=c.hito_id where h.public_id=@h", new { h = e.Hitos[0] }));
    }

    [Fact] public async Task Segunda_confirmacion_del_mismo_docente_con_otra_clave_da_409()
    {
        var e = await Escenario.Crear(F); await Registrar(e, 0);
        await Confirmar(F.Docente(), e.Hitos[0], new { confirmado = true });
        Assert.Equal(HttpStatusCode.Conflict, (await Confirmar(F.Docente(), e.Hitos[0], new { confirmado = true })).StatusCode);
    }

    [Fact] public async Task Observacion_de_mas_de_500_da_422_y_sin_observacion_al_no_confirmar_da_422()
    {
        var e = await Escenario.Crear(F); await Registrar(e, 0);
        Assert.Equal((HttpStatusCode)422, (await Confirmar(F.Docente(), e.Hitos[0], new { confirmado = false, observacion = new string('x', 501) })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await Confirmar(F.Docente(), e.Hitos[0], new { confirmado = false })).StatusCode);
    }

    [Fact] public async Task Semana_PROGRAMADA_da_404()
    {
        var e = await Escenario.Crear(F);
        Assert.Equal(HttpStatusCode.NotFound, (await Confirmar(F.Docente(), e.Hitos[1], new { confirmado = true })).StatusCode);
    }

    [Fact] public async Task Observacion_deja_la_semana_OBSERVADO_y_basta_una_en_plan_grupal()
    {
        var e = await Escenario.Crear(F); await Registrar(e, 0);
        Assert.Equal(HttpStatusCode.Created, (await Confirmar(F.Docente("docente1"), e.Hitos[0], new { confirmado = false, observacion = "No hubo encuentro" })).StatusCode);
        Assert.Equal("OBSERVADO", await Estado(e, 0));
        Assert.Equal(HttpStatusCode.Created, (await Confirmar(F.Docente("docente2"), e.Hitos[0], new { confirmado = true })).StatusCode);
        Assert.Equal("OBSERVADO", await Estado(e, 0));
    }

    [Fact] public async Task Un_docente_ajeno_al_plan_no_ve_ni_confirma()
    {
        var e = await Escenario.Crear(F); await Registrar(e, 0);
        Assert.Equal(HttpStatusCode.NotFound, (await Confirmar(F.Docente("docente4"), e.Hitos[0], new { confirmado = true })).StatusCode);
        var m = await F.Docente("docente4").GetFromJsonAsync<JsonElement>($"/api/v1/mi-acompanamiento?mes={e.Mes}");
        Assert.Equal(0, m.GetProperty("planes").GetArrayLength());
    }

    [Fact] public async Task Mi_acompanamiento_muestra_seguimiento_solo_si_esta_registrado_y_solo_a_TUTOR_DOCENTE()
    {
        var e = await Escenario.Crear(F); await Registrar(e, 0);
        var m = await F.Docente().GetFromJsonAsync<JsonElement>($"/api/v1/mi-acompanamiento?mes={e.Mes}");
        var hitos = m.GetProperty("planes")[0].GetProperty("hitos");
        Assert.Equal("Taller de lectura", hitos[0].GetProperty("seguimiento").GetProperty("actividades").GetString());
        Assert.Equal(JsonValueKind.Null, hitos[1].GetProperty("seguimiento").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await F.Tutor().GetAsync($"/api/v1/mi-acompanamiento?mes={e.Mes}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await F.Docente().GetAsync("/api/v1/tutores/me/plan?mes=" + e.Mes)).StatusCode);
    }
}
