using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sses.Api.Tests;

/// <summary>RQ_013 / CU_008: tablero de desempeño de tutores sobre vistas materializadas.</summary>
[Collection("sses")]
public class AnaliticaTests(SsesFixtureInicial fx)
{
    SsesFixture F => fx.F;
    async Task<Escenario> ConUnaSemanaRegistrada()
    {
        var e = await Escenario.Crear(F);
        Assert.Equal(HttpStatusCode.OK, (await F.Tutor().SendAsync(Escenario.Put(e.Hitos[0], e.Cuerpo(), await e.VersionActual(0)))).StatusCode);
        await F.SuperExec("select analitica.refrescar()");
        return e;
    }

    [Fact] public async Task Indicadores_por_tutor_region_y_ranking_con_bitacora()
    {
        var e = await ConUnaSemanaRegistrada();
        var j = await F.Analista().GetFromJsonAsync<JsonElement>($"/api/v1/analitica/tutores?mes={e.Mes}");
        var r = j.GetProperty("ranking")[0];
        Assert.Equal("Tutor Uno", r.GetProperty("nombres").GetString());
        Assert.Equal(3, r.GetProperty("programadas").GetInt32()); Assert.Equal(1, r.GetProperty("registradas").GetInt32());
        Assert.Equal(33.33m, r.GetProperty("cumplimiento").GetDecimal()); Assert.Equal(3, r.GetProperty("docentesAtendidos").GetInt32());
        Assert.Equal("Pacífico", j.GetProperty("porRegion")[0].GetProperty("region").GetString());
        Assert.Equal(33.33m, j.GetProperty("resumen").GetProperty("cumplimiento").GetDecimal());
        Assert.True(await F.Super<long>("select count(*) from auditoria.bitacora where accion='CONSULTAR_TABLERO' and entidad_id=@m", new { m = e.Mes }) >= 1);
    }

    [Fact] public async Task Filtros_por_region_y_estrategia_excluyen_lo_que_no_coincide()
    {
        var e = await ConUnaSemanaRegistrada();
        var otra = await F.Analista().GetFromJsonAsync<JsonElement>($"/api/v1/analitica/tutores?mes={e.Mes}&region=99");
        Assert.Equal(0, otra.GetProperty("ranking").GetArrayLength());
        var est = await F.Analista().GetFromJsonAsync<JsonElement>($"/api/v1/analitica/tutores?mes={e.Mes}&estrategiaId={e.Estrategia}");
        Assert.Equal(1, est.GetProperty("ranking").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await F.Analista().GetAsync("/api/v1/analitica/tutores?mes=2026-13")).StatusCode);
    }

    [Fact] public async Task Detalle_semana_a_semana_del_tutor()
    {
        var e = await ConUnaSemanaRegistrada();
        var tutor = (await F.Analista().GetFromJsonAsync<JsonElement>($"/api/v1/analitica/tutores?mes={e.Mes}")).GetProperty("ranking")[0].GetProperty("id").GetGuid();
        var d = await F.Analista().GetFromJsonAsync<JsonElement>($"/api/v1/analitica/tutores/{tutor}/detalle?mes={e.Mes}");
        Assert.Equal(3, d.GetProperty("semanas").GetArrayLength());
        Assert.Equal(1, d.GetProperty("semanas")[0].GetProperty("registradas").GetInt32()); Assert.Equal(0, d.GetProperty("semanas")[1].GetProperty("registradas").GetInt32());
        Assert.Equal(3, d.GetProperty("semanas")[0].GetProperty("docentesAtendidos").GetInt32());
    }

    [Fact] public async Task Exportar_entrega_un_xlsx_y_lo_registra()
    {
        var e = await ConUnaSemanaRegistrada();
        var r = await F.Analista().GetAsync($"/api/v1/analitica/tutores/exportar?mes={e.Mes}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", r.Content.Headers.ContentType!.MediaType);
        var b = await r.Content.ReadAsByteArrayAsync(); Assert.True(b.Length > 1000); Assert.Equal((byte)'P', b[0]); Assert.Equal((byte)'K', b[1]);
        Assert.Equal(1, await F.Super<long>("select count(*) from auditoria.bitacora where accion='EXPORTAR_TABLERO' and entidad_id=@m", new { m = e.Mes }));
    }

    [Fact] public async Task Solo_ANALISTA_MEN_accede()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await F.Tutor().GetAsync("/api/v1/analitica/tutores")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await F.Gestor().GetAsync("/api/v1/analitica/filtros")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await F.Docente().GetAsync("/api/v1/analitica/tutores/exportar")).StatusCode);
    }
}
