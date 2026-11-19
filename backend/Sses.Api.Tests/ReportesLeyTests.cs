using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace Sses.Api.Tests;

/// <summary>RQ_014 / CU_009: reportes de ley con huella SHA-256, verificación pública y aislamiento por organización (RLS).</summary>
[Collection("sses")]
public class ReportesLeyTests(SsesFixtureInicial fx)
{
    SsesFixture F => fx.F;

    async Task<Guid> Plantilla(HttpClient c, string nombre) =>
        (await c.GetFromJsonAsync<JsonElement>("/api/v1/reportes-ley/plantillas")).GetProperty("plantillas").EnumerateArray().First(p => p.GetProperty("nombre").GetString()!.StartsWith(nombre)).GetProperty("id").GetGuid();

    async Task<(Escenario e, JsonElement rep)> Emitir(HttpClient ente, string plantilla = "Cumplimiento")
    {
        var e = await Escenario.Crear(F);
        Assert.Equal(HttpStatusCode.OK, (await F.Tutor().SendAsync(Escenario.Put(e.Hitos[0], e.Cuerpo(), await e.VersionActual(0)))).StatusCode);
        await F.SuperExec("select analitica.refrescar()");
        var r = await ente.PostAsJsonAsync("/api/v1/reportes-ley", new { plantillaId = await Plantilla(ente, plantilla), vigencia = int.Parse(e.Mes[..4]), periodo = e.Mes });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (e, await r.Content.ReadFromJsonAsync<JsonElement>());
    }

    [Fact] public async Task Genera_pdf_con_huella_y_el_archivo_descargado_coincide()
    {
        var ente = F.Ente(); var (_, rep) = await Emitir(ente);
        Assert.Matches("^[0-9a-f]{64}$", rep.GetProperty("huella").GetString());
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", rep.GetProperty("codigo").GetString());
        var fila = rep.GetProperty("filas")[0].EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(["Pacífico", "3", "1", "33,3 %"], [fila[0], fila[3], fila[4], fila[5]]); // región, programadas, registradas, cumplimiento
        var pdf = await ente.GetAsync(rep.GetProperty("descarga").GetString());
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes[..4]));
        Assert.Equal(rep.GetProperty("huellaArchivo").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Assert.Equal(1, await F.Super<long>("select count(*) from auditoria.bitacora where accion='GENERAR_REPORTE_LEY' and entidad_id=@i", new { i = rep.GetProperty("id").GetString() }));
        Assert.Equal(1, await F.Super<long>("select count(*) from auditoria.bitacora where accion='DESCARGAR_REPORTE_LEY' and entidad_id=@i", new { i = rep.GetProperty("id").GetString() }));
    }

    [Fact] public async Task Verificacion_publica_sin_sesion_valida_codigo_y_huella_del_archivo()
    {
        var (_, rep) = await Emitir(F.Ente());
        var anon = F.Factory.CreateClient(); var cod = rep.GetProperty("codigo").GetString()!;
        var ok = await anon.GetFromJsonAsync<JsonElement>($"/api/v1/reportes-ley/verificar/{cod.ToLowerInvariant()}");
        Assert.True(ok.GetProperty("valido").GetBoolean()); Assert.Equal("Contraloría (demo)", ok.GetProperty("organizacion").GetString());
        Assert.Equal(rep.GetProperty("huella").GetString(), ok.GetProperty("huella").GetString());
        Assert.Equal(JsonValueKind.Null, ok.GetProperty("coincideArchivo").ValueKind);
        var bien = await anon.GetFromJsonAsync<JsonElement>($"/api/v1/reportes-ley/verificar/{cod}?sha256={rep.GetProperty("huellaArchivo").GetString()!.ToUpperInvariant()}");
        Assert.True(bien.GetProperty("coincideArchivo").GetBoolean());
        var mal = await anon.GetFromJsonAsync<JsonElement>($"/api/v1/reportes-ley/verificar/{cod}?sha256={new string('0', 64)}");
        Assert.False(mal.GetProperty("coincideArchivo").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/v1/reportes-ley/verificar/AAAA-BBBB-CCCC")).StatusCode);
    }

    [Fact] public async Task Plantilla_no_autorizada_para_la_organizacion_responde_403_con_bitacora()
    {
        var proc = F.Ente("procuraduria1"); var idConfirmacion = await Plantilla(proc, "Confirmación");
        var contr = F.Ente();
        var lista = (await contr.GetFromJsonAsync<JsonElement>("/api/v1/reportes-ley/plantillas")).GetProperty("plantillas");
        Assert.Equal(1, lista.GetArrayLength());
        var r = await contr.PostAsJsonAsync("/api/v1/reportes-ley", new { plantillaId = idConfirmacion, vigencia = 2026 });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("PLANTILLA_NO_AUTORIZADA", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.True(await F.Super<long>("select count(*) from auditoria.bitacora where accion='ACCESO_DENEGADO' and entidad='plantilla' and entidad_id=@i", new { i = idConfirmacion.ToString() }) >= 1);
    }

    [Fact] public async Task Cada_ente_ve_solo_sus_emisiones_y_no_puede_descargar_las_ajenas()
    {
        var (_, repC) = await Emitir(F.Ente());
        var (_, repP) = await Emitir(F.Ente("procuraduria1"), "Confirmación");
        var propias = (await F.Ente("contraloria2").GetFromJsonAsync<JsonElement>("/api/v1/reportes-ley")).GetProperty("reportes").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(repC.GetProperty("id").GetGuid(), propias); Assert.DoesNotContain(repP.GetProperty("id").GetGuid(), propias);
        Assert.Equal(HttpStatusCode.NotFound, (await F.Ente("contraloria2").GetAsync($"/api/v1/reportes-ley/{repP.GetProperty("id").GetGuid()}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await F.Ente("contraloria2").GetAsync($"/api/v1/reportes-ley/{repC.GetProperty("id").GetGuid()}/pdf")).StatusCode);
    }

    [Fact] public async Task La_politica_RLS_filtra_en_la_base_aunque_la_consulta_no_tenga_where()
    {
        await Emitir(F.Ente()); await Emitir(F.Ente("procuraduria1"), "Confirmación");
        await using var cn = new NpgsqlConnection(F.ConnApi); await cn.OpenAsync();
        Assert.Equal(0, await cn.ExecuteScalarAsync<long>("select count(*) from reportes.emision")); // sin organización de sesión: nada
        await using var tx = await cn.BeginTransactionAsync();
        await cn.ExecuteAsync("select set_config('sses.organizacion_id', (select id::text from reportes.organizacion_control where nombre like 'Procuraduría%'), true)", transaction: tx);
        var org = await cn.ExecuteScalarAsync<long>("select count(distinct organizacion_id) from reportes.emision", transaction: tx);
        var total = await cn.ExecuteScalarAsync<long>("select count(*) from reportes.emision", transaction: tx);
        Assert.Equal(1, org); Assert.True(total >= 1);
        Assert.True(total < await F.Super<long>("select count(*) from reportes.emision"));
        // la emisión es inmutable para la API: no puede modificar ni borrar
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cn.ExecuteAsync("delete from reportes.emision", transaction: tx));
        Assert.Equal("42501", ex.SqlState);
    }

    [Fact] public async Task Solo_el_ente_de_control_con_organizacion_accede_y_valida_parametros()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await F.Tutor().GetAsync("/api/v1/reportes-ley/plantillas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await F.Analista().GetAsync("/api/v1/reportes-ley")).StatusCode);
        var sin = await F.Ente("ente_sin_org").GetAsync("/api/v1/reportes-ley/plantillas");
        Assert.Equal(HttpStatusCode.Forbidden, sin.StatusCode); Assert.Equal("SIN_ORGANIZACION", (await sin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var c = F.Ente(); var p = await Plantilla(c, "Cumplimiento");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/reportes-ley", new { plantillaId = p })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/reportes-ley", new { plantillaId = p, vigencia = 2026, periodo = "2025-03" })).StatusCode);
    }
}
