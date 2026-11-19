using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sses.Api.Acompanamiento;

namespace Sses.Api.Tests;

public class SemanasIsoTests
{
    [Theory]
    [InlineData("2026-11", 4, "2026-W45", "2026-W48")]
    [InlineData("2026-10", 5, "2026-W40", "2026-W44")]
    [InlineData("2026-12", 5, "2026-W49", "2026-W53")]
    [InlineData("2027-01", 4, "2027-W01", "2027-W04")]
    public void Un_mes_tiene_4_o_5_semanas_ISO_cuyo_jueves_cae_en_el_mes(string mes, int n, string primera, string ultima)
    {
        var s = SemanasIso.Del(mes);
        Assert.Equal(n, s.Count); Assert.Equal(primera, s[0].SemanaIso); Assert.Equal(ultima, s[^1].SemanaIso);
        Assert.All(s, x => { Assert.Equal(DayOfWeek.Monday, x.Inicio.DayOfWeek); Assert.Equal(DayOfWeek.Sunday, x.Fin.DayOfWeek); Assert.Equal(mes, x.Inicio.AddDays(3).ToString("yyyy-MM")); });
        Assert.Equal(Enumerable.Range(1, n), s.Select(x => x.Numero));
    }
    [Fact] public void Cumplimiento_es_registradas_sobre_programadas_con_dos_decimales()
    {
        Assert.Equal(0m, SemanasIso.Cumplimiento(0, 3)); Assert.Equal(33.33m, SemanasIso.Cumplimiento(1, 3)); Assert.Equal(66.67m, SemanasIso.Cumplimiento(2, 3));
        Assert.Equal(100m, SemanasIso.Cumplimiento(3, 3)); Assert.Equal(0m, SemanasIso.Cumplimiento(0, 0));
    }
    [Theory] [InlineData("2026-13")] [InlineData("2026-1")] [InlineData(null)] [InlineData("abcd-ef")]
    public void Meses_invalidos(string? mes) => Assert.False(SemanasIso.MesValido(mes));
}

[Collection("sses")]
public class PlanMensualTests(SsesFixtureInicial fx)
{
    SsesFixture F => fx.F;
    static readonly Guid E1 = new("00000000-0000-7000-8000-0000000000e1");

    /// <summary>Crea una estrategia y asigna los docentes 1..n (ids internos) al tutor 1.</summary>
    async Task<Guid> Estrategia(int docentes = 4, long tutor = 1)
    {
        var r = await F.Gestor().PostAsJsonAsync("/api/v1/estrategias", new { entidadId = E1, nombre = "Plan " + Guid.NewGuid(), objetivo = "o", poblacion = "p", fechaInicio = "2026-08-01", fechaFin = "2027-05-31" });
        var id = Guid.Parse((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!);
        await F.SuperExec("insert into acompanamiento.asignacion select @t, d, e.id from generate_series(1,@n) d, estrategias.estrategia e where e.public_id=@id", new { t = tutor, n = docentes, id });
        return id;
    }
    async Task<Guid[]> Docentes() => (await F.Tutor().GetFromJsonAsync<JsonElement>("/api/v1/tutores/me/docentes")).GetProperty("estrategias").EnumerateArray()
        .SelectMany(e => e.GetProperty("docentes").EnumerateArray()).Select(d => d.GetProperty("id").GetGuid()).ToArray();
    static string Mes(int offset) => new DateTime(2030, 1, 1).AddMonths(offset).ToString("yyyy-MM"); // meses lejanos para no chocar entre pruebas
    static object Plan(string mes, string mod, Guid est, IEnumerable<Guid> docs, int[] semanas) => new { mes, modalidad = mod, estrategiaId = est, docenteIds = docs, temas = "Evaluación formativa", semanas };

    [Fact] public async Task Plan_grupal_de_3_docentes_y_3_semanas_crea_1_plan_y_3_hitos_y_repetir_da_409()
    {
        var est = await Estrategia(); var docs = (await DocentesDe(est)).Take(3).ToArray(); var mes = Mes(0);
        var h = F.Tutor();
        var r = await h.PostAsJsonAsync("/api/v1/acompanamientos", Plan(mes, "GRUPAL", est, docs, [1, 2, 3]));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>(); var planes = j.GetProperty("planes");
        Assert.Equal(1, planes.GetArrayLength());
        Assert.Equal(3, planes[0].GetProperty("hitos").GetArrayLength()); Assert.Equal(3, planes[0].GetProperty("docentes").GetArrayLength());
        Assert.All(planes[0].GetProperty("hitos").EnumerateArray(), x => Assert.Equal("PROGRAMADA", x.GetProperty("estado").GetString()));
        var r2 = await h.PostAsJsonAsync("/api/v1/acompanamientos", Plan(mes, "GRUPAL", est, docs.Take(2), [1]));
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode); Assert.Equal("PLAN_DUPLICADO", (await r2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
    async Task<Guid[]> DocentesDe(Guid est)
    {
        var j = await F.Tutor().GetFromJsonAsync<JsonElement>("/api/v1/tutores/me/docentes");
        return j.GetProperty("estrategias").EnumerateArray().First(e => e.GetProperty("estrategia").GetProperty("id").GetGuid() == est).GetProperty("docentes").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToArray();
    }

    [Fact] public async Task Plan_individual_crea_un_plan_por_docente()
    {
        var est = await Estrategia(); var docs = (await DocentesDe(est)).Take(2).ToArray();
        var j = await (await F.Tutor().PostAsJsonAsync("/api/v1/acompanamientos", Plan(Mes(1), "INDIVIDUAL", est, docs, [1, 2]))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, j.GetProperty("planes").GetArrayLength());
        Assert.All(j.GetProperty("planes").EnumerateArray(), p => { Assert.Equal(1, p.GetProperty("docentes").GetArrayLength()); Assert.Equal(2, p.GetProperty("hitos").GetArrayLength()); });
    }

    [Fact] public async Task Docente_ajeno_da_422()
    {
        var est = await Estrategia(2); var docs = await DocentesDe(est);
        var ajeno = await F.Super<Guid>("select public_id from maestros.docente where id=4"); // docente 4 no asignado a esta estrategia
        var r = await F.Tutor().PostAsJsonAsync("/api/v1/acompanamientos", Plan(Mes(2), "INDIVIDUAL", est, [ajeno], [1]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode); Assert.Equal("DOCENTE_NO_ASIGNADO", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        // el tutor 2 tampoco puede programar con docentes del tutor 1
        var r2 = await F.Tutor("tutor2").PostAsJsonAsync("/api/v1/acompanamientos", Plan(Mes(2), "INDIVIDUAL", est, docs.Take(1), [1]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r2.StatusCode);
    }

    [Fact] public async Task Validaciones_de_semanas_grupo_y_campos()
    {
        var est = await Estrategia(); var docs = (await DocentesDe(est)).Take(3).ToArray(); var h = F.Tutor();
        var r = await h.PostAsJsonAsync("/api/v1/acompanamientos", Plan("2026-11", "GRUPAL", est, docs, [5])); // noviembre 2026 tiene 4 semanas
        Assert.Equal("SEMANA_FUERA_DEL_MES", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var grande = Enumerable.Range(0, 31).Select(_ => Guid.NewGuid());
        Assert.Equal("GRUPO_EXCEDE_30", (await (await h.PostAsJsonAsync("/api/v1/acompanamientos", Plan(Mes(3), "GRUPAL", est, grande, [1]))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await h.PostAsJsonAsync("/api/v1/acompanamientos", new { mes = "2030-99", modalidad = "X", estrategiaId = est, docenteIds = Array.Empty<Guid>(), temas = "", semanas = Array.Empty<int>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.PostAsJsonAsync("/api/v1/acompanamientos", Plan(Mes(3), "GRUPAL", est, docs.Take(1), [1]))).StatusCode);
    }

    [Fact] public async Task El_plan_del_mes_trae_hitos_con_version_cumplimiento_cero_y_ETag_con_304()
    {
        var est = await Estrategia(); var docs = (await DocentesDe(est)).Take(3).ToArray(); var mes = Mes(4); var h = F.Tutor();
        await h.PostAsJsonAsync("/api/v1/acompanamientos", Plan(mes, "GRUPAL", est, docs, [1, 2, 3]));
        var r = await h.GetAsync($"/api/v1/tutores/me/plan?mes={mes}");
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0m, j.GetProperty("cumplimiento").GetProperty("porcentaje").GetDecimal()); Assert.Equal(3, j.GetProperty("cumplimiento").GetProperty("programadas").GetInt32());
        Assert.True(j.GetProperty("planes")[0].GetProperty("hitos")[0].GetProperty("version").GetInt64() > 0);
        var etag = r.Headers.ETag!.ToString();
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tutores/me/plan?mes={mes}"); req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.NotModified, (await h.SendAsync(req)).StatusCode);
        // otro tutor no ve ese plan
        var otro = await (await F.Tutor("tutor2").GetAsync($"/api/v1/tutores/me/plan?mes={mes}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, otro.GetProperty("planes").GetArrayLength());
    }

    [Fact] public async Task El_gestor_no_puede_programar_y_programar_queda_en_bitacora()
    {
        var est = await Estrategia(); var docs = (await DocentesDe(est)).Take(2).ToArray();
        Assert.Equal(HttpStatusCode.Forbidden, (await F.Gestor().PostAsJsonAsync("/api/v1/acompanamientos", Plan(Mes(5), "GRUPAL", est, docs, [1]))).StatusCode);
        var antes = await F.Super<long>("select count(*) from auditoria.bitacora where accion='PROGRAMAR_PLAN'");
        await F.Tutor().PostAsJsonAsync("/api/v1/acompanamientos", Plan(Mes(5), "GRUPAL", est, docs, [1]));
        Assert.Equal(antes + 1, await F.Super<long>("select count(*) from auditoria.bitacora where accion='PROGRAMAR_PLAN'"));
    }
}
