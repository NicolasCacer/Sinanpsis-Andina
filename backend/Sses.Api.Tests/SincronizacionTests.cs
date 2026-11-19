using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sses.Api.Tests;

[Collection("sses")]
public class SincronizacionTests(SsesFixtureInicial fx)
{
    SsesFixture F => fx.F;
    static object Item(Escenario e, int i, Guid? key = null, long? version = null, string act = "Registro hecho sin señal", DateTime? capturado = null, string? resolucion = null, string[]? fotos = null) => new
    {
        idempotencyKey = key ?? Guid.NewGuid(), hitoId = e.Hitos[i], versionBase = version ?? e.Versiones[i], capturadoEn = capturado ?? DateTime.UtcNow.AddHours(-2),
        asistencia = e.Docentes.Select((d, k) => new { docenteId = d, asistio = k != 1 }), actividades = act, avances = "Avance offline",
        evidencias = (fotos ?? []).Select(f => new { nombre = "f.jpg", base64 = f }), resolucion
    };
    async Task<JsonElement> Sync(HttpClient h, params object[] items)
    {
        var r = await h.PostAsJsonAsync("/api/v1/sincronizacion/seguimientos", new { items });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resultados");
    }
    static string Estado(JsonElement r, int i) => r[i].GetProperty("estado").GetString()!;
    Task<long> Filas(Escenario e, int hito) => F.Super<long>("select count(*) from acompanamiento.seguimiento_semanal s join acompanamiento.hito_semanal h on h.id=s.hito_id where h.public_id=@h", new { h = e.Hitos[hito] });
    static string Jpeg64() { var b = new byte[300]; b[0] = 0xFF; b[1] = 0xD8; b[2] = 0xFF; return Convert.ToBase64String(b); }

    [Fact] public async Task CP_11_registro_offline_y_reconexion_queda_Sincronizado_con_una_sola_fila()
    {
        var e = await Escenario.Crear(F); var h = F.Tutor();
        var r = await Sync(h, Item(e, 0, fotos: [Jpeg64()]));
        Assert.Equal("APLICADO", Estado(r, 0)); Assert.Equal(33.33m, r[0].GetProperty("cumplimientoMes").GetProperty("porcentaje").GetDecimal());
        Assert.Equal(1, await Filas(e, 0));
        var hito = (await e.Plan()).GetProperty("planes")[0].GetProperty("hitos")[0];
        Assert.Equal("REGISTRADA", hito.GetProperty("estado").GetString()); Assert.Equal("SIN_CONEXION", hito.GetProperty("seguimiento").GetProperty("origen").GetString());
        Assert.Equal(1, hito.GetProperty("seguimiento").GetProperty("evidencias").GetArrayLength());
        Assert.Equal(1, await F.Super<long>("select count(*) from auditoria.bitacora where accion='SINCRONIZAR' and entidad_id=@id", new { id = e.Hitos[0].ToString() }));
    }

    [Fact] public async Task CP_12_reenviar_el_mismo_lote_da_DUPLICADO_sin_filas_nuevas()
    {
        var e = await Escenario.Crear(F); var h = F.Tutor(); var k0 = Guid.NewGuid(); var k1 = Guid.NewGuid();
        var lote = new[] { Item(e, 0, k0), Item(e, 1, k1) };
        var r1 = await Sync(h, lote); Assert.Equal("APLICADO", Estado(r1, 0)); Assert.Equal("APLICADO", Estado(r1, 1));
        var bit = await F.Super<long>("select count(*) from auditoria.bitacora where accion='SINCRONIZAR'");
        var r2 = await Sync(h, lote);
        Assert.Equal("DUPLICADO", Estado(r2, 0)); Assert.Equal("DUPLICADO", Estado(r2, 1));
        Assert.Equal(66.67m, r2[1].GetProperty("cumplimientoMes").GetProperty("porcentaje").GetDecimal());
        Assert.Equal(1, await Filas(e, 0)); Assert.Equal(1, await Filas(e, 1));
        Assert.Equal(bit, await F.Super<long>("select count(*) from auditoria.bitacora where accion='SINCRONIZAR'"));
    }

    [Fact] public async Task Envios_concurrentes_con_la_misma_clave_aplican_una_sola_vez()
    {
        var e = await Escenario.Crear(F); var k = Guid.NewGuid(); var item = Item(e, 0, k);
        var rs = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Sync(F.Tutor(), item)));
        var estados = rs.Select(r => Estado(r, 0)).ToList();
        Assert.Equal(1, estados.Count(x => x == "APLICADO")); Assert.Equal(4, estados.Count(x => x == "DUPLICADO"));
        Assert.Equal(1, await Filas(e, 0));
    }

    [Fact] public async Task CP_13_conflicto_de_version_devuelve_ambas_versiones_y_se_resuelve_conservando_el_mio()
    {
        var e = await Escenario.Crear(F); var h = F.Tutor();
        // otro dispositivo registra en línea la misma semana
        Assert.Equal(HttpStatusCode.OK, (await h.SendAsync(Escenario.Put(e.Hitos[0], e.Cuerpo(actividades: "Versión del servidor"), e.Versiones[0]))).StatusCode);
        var vServidor = await e.VersionActual(0);
        var r = await Sync(h, Item(e, 0, act: "Versión del dispositivo")); // versionBase vieja
        Assert.Equal("CONFLICTO", Estado(r, 0));
        Assert.Equal(vServidor, r[0].GetProperty("versionServidor").GetInt64());
        var srv = r[0].GetProperty("registroServidor");
        Assert.Equal("Versión del servidor", srv.GetProperty("actividades").GetString()); Assert.Equal("Tutor Uno", srv.GetProperty("registradoPor").GetString());
        Assert.Equal(1, await Filas(e, 0));
        // conservar el mío: nueva clave + versionBase del servidor
        var r2 = await Sync(h, Item(e, 0, version: vServidor, act: "Versión del dispositivo", resolucion: "SOBRESCRIBIR"));
        Assert.Equal("APLICADO", Estado(r2, 0)); Assert.Equal(1, await Filas(e, 0));
        var seg = (await e.Plan()).GetProperty("planes")[0].GetProperty("hitos")[0].GetProperty("seguimiento");
        Assert.Equal("Versión del dispositivo", seg.GetProperty("actividades").GetString());
        var fila = await F.Super<string>("select antes::text || '|' || despues::text from auditoria.bitacora where accion='CONFLICTO_RESUELTO' and entidad_id=@id order by id desc limit 1", new { id = e.Hitos[0].ToString() });
        Assert.Contains("Versión del servidor", fila); Assert.Contains("CONSERVAR_LOCAL", fila);
        Assert.True(await F.Super<bool>("select ok from auditoria.verificar_cadena()"));
    }

    [Fact] public async Task Conflicto_resuelto_con_el_servidor_queda_en_bitacora()
    {
        var e = await Escenario.Crear(F); var h = F.Tutor();
        await h.SendAsync(Escenario.Put(e.Hitos[0], e.Cuerpo(), e.Versiones[0]));
        Assert.Equal("CONFLICTO", Estado(await Sync(h, Item(e, 0)), 0));
        var r = await h.PostAsJsonAsync("/api/v1/sincronizacion/resoluciones", new { hitoId = e.Hitos[0], decision = "SERVIDOR" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(1, await F.Super<long>("select count(*) from auditoria.bitacora where accion='CONFLICTO_RESUELTO' and entidad_id=@id and despues::text like '%CONSERVAR_SERVIDOR%'", new { id = e.Hitos[0].ToString() }));
    }

    [Fact] public async Task Mas_de_30_dias_se_rechaza_y_el_hito_ajeno_tambien()
    {
        var e = await Escenario.Crear(F);
        var r = await Sync(F.Tutor(), Item(e, 0, capturado: DateTime.UtcNow.AddDays(-31)), Item(e, 1));
        Assert.Equal("RECHAZADO", Estado(r, 0)); Assert.Equal("APLICADO", Estado(r, 1));
        Assert.Equal(0, await Filas(e, 0));
        var ajeno = await Sync(F.Tutor("tutor2"), Item(e, 2));
        Assert.Equal("RECHAZADO", Estado(ajeno, 0));
    }

    [Fact] public async Task Lote_vacio_o_de_mas_de_50_da_400_y_foto_invalida_se_rechaza()
    {
        var e = await Escenario.Crear(F);
        Assert.Equal(HttpStatusCode.BadRequest, (await F.Tutor().PostAsJsonAsync("/api/v1/sincronizacion/seguimientos", new { items = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await F.Tutor().PostAsJsonAsync("/api/v1/sincronizacion/seguimientos", new { items = Enumerable.Range(0, 51).Select(_ => Item(e, 0)).ToArray() })).StatusCode);
        var r = await Sync(F.Tutor(), Item(e, 0, fotos: [Convert.ToBase64String(new byte[] { 1, 2, 3 })]));
        Assert.Equal("RECHAZADO", Estado(r, 0)); Assert.Equal(0, await Filas(e, 0));
        Assert.Equal(HttpStatusCode.Forbidden, (await F.Gestor().PostAsJsonAsync("/api/v1/sincronizacion/seguimientos", new { items = new[] { Item(e, 0) } })).StatusCode);
    }
}
