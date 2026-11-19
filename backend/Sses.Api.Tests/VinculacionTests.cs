using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sses.Api.Tests;

[Collection("sses")]
public class VinculacionTests(SsesFixtureInicial fx)
{
    SsesFixture F => fx.F;
    static readonly Guid E1 = new("00000000-0000-7000-8000-0000000000e1");
    static string Est(int i) => $"00000000-0000-7000-8000-00000000005{i}";

    async Task<string> NuevaEstrategia(string? fin = "2027-05-31")
    {
        var r = await F.Gestor().PostAsJsonAsync("/api/v1/estrategias", new { entidadId = E1, nombre = "Vinc " + Guid.NewGuid(), objetivo = "o", poblacion = "p", fechaInicio = "2026-08-01", fechaFin = fin });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    [Fact] public async Task CP_07_vincular_dos_veces_al_mismo_estudiante_no_duplica()
    {
        var id = await NuevaEstrategia(); var h = F.Gestor();
        var r1 = await h.PostAsJsonAsync($"/api/v1/estrategias/{id}/estudiantes", new { estudianteIds = new[] { Est(1), Est(2) } });
        var j1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode); Assert.Equal(2, j1.GetProperty("vinculados").GetInt32()); Assert.Equal(0, j1.GetProperty("yaVinculados").GetInt32());
        var r2 = await h.PostAsJsonAsync($"/api/v1/estrategias/{id}/estudiantes", new { estudianteIds = new[] { Est(2), Est(3) } });
        var j2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, j2.GetProperty("vinculados").GetInt32()); Assert.Equal(1, j2.GetProperty("yaVinculados").GetInt32());
        Assert.Equal(3, await F.Super<long>("select count(*) from estrategias.estrategia_estudiante ee join estrategias.estrategia e on e.id=ee.estrategia_id where e.public_id=@id", new { id = Guid.Parse(id) }));
        Assert.Equal(3, await F.Super<int>("select estudiantes_vinculados from estrategias.estrategia where public_id=@id", new { id = Guid.Parse(id) }));
    }

    [Fact] public async Task El_primer_vinculo_bloquea_la_estrategia_en_la_misma_transaccion()
    {
        var id = await NuevaEstrategia(); var h = F.Gestor();
        Assert.Equal("EDITABLE", await F.Super<string>("select estado from estrategias.estrategia where public_id=@id", new { id = Guid.Parse(id) }));
        var r = await h.PostAsJsonAsync($"/api/v1/estrategias/{id}/estudiantes", new { estudianteIds = new[] { Est(1) } });
        Assert.Equal("BLOQUEADA", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("estadoEstrategia").GetString());
        Assert.Equal("BLOQUEADA", await F.Super<string>("select estado from estrategias.estrategia where public_id=@id", new { id = Guid.Parse(id) }));
    }

    [Fact] public async Task CP_08_la_busqueda_devuelve_solo_nombre_sede_y_municipio()
    {
        var r = await F.Gestor().GetAsync("/api/v1/estudiantes?tipoDoc=TI&numDoc=1000000001");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["id", "municipio", "nombre", "sede"], j.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("Estudiante 1", j.GetProperty("nombre").GetString());
        var crudo = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("1000000001", crudo); Assert.DoesNotContain("Secreto", crudo); Assert.DoesNotContain("2012", crudo);
        Assert.True(r.Headers.Contains("Server-Timing"));
    }

    [Fact] public async Task Busqueda_inexistente_da_404_y_tipo_invalido_da_400()
    {
        var h = F.Gestor();
        Assert.Equal(HttpStatusCode.NotFound, (await h.GetAsync("/api/v1/estudiantes?tipoDoc=TI&numDoc=9999999999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.GetAsync("/api/v1/estudiantes?tipoDoc=XX&numDoc=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await F.Tutor().GetAsync("/api/v1/estudiantes?tipoDoc=TI&numDoc=1000000001")).StatusCode);
    }

    [Fact] public async Task La_busqueda_queda_en_bitacora_sin_el_documento()
    {
        await F.Gestor().GetAsync("/api/v1/estudiantes?tipoDoc=TI&numDoc=1000000004");
        var fila = await F.Super<string>("select despues::text || '|' || coalesce(entidad_id,'') from auditoria.bitacora where accion='BUSCAR_ESTUDIANTE' order by id desc limit 1");
        Assert.Contains("00000000-0000-7000-8000-000000000054", fila); Assert.DoesNotContain("1000000004", fila);
    }

    [Fact] public async Task Vincular_estrategia_ajena_da_404_y_estudiante_inexistente_da_422()
    {
        var id = await NuevaEstrategia();
        Assert.Equal(HttpStatusCode.NotFound, (await F.Gestor("gestorC").PostAsJsonAsync($"/api/v1/estrategias/{id}/estudiantes", new { estudianteIds = new[] { Est(1) } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await F.Gestor().PostAsJsonAsync($"/api/v1/estrategias/{id}/estudiantes", new { estudianteIds = new[] { Guid.NewGuid() } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await F.Gestor().PostAsJsonAsync($"/api/v1/estrategias/{id}/estudiantes", new { estudianteIds = Array.Empty<string>() })).StatusCode);
    }

    [Fact] public async Task Estrategia_fuera_de_vigencia_da_422()
    {
        var id = await NuevaEstrategia();
        await F.SuperExec("update estrategias.estrategia set fecha_inicio='2026-01-01', fecha_fin='2026-01-31' where public_id=@id", new { id = Guid.Parse(id) });
        var r = await F.Gestor().PostAsJsonAsync($"/api/v1/estrategias/{id}/estudiantes", new { estudianteIds = new[] { Est(1) } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("ESTRATEGIA_FUERA_DE_VIGENCIA", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact] public async Task El_listado_de_vinculados_pagina_por_llave_sin_duplicados()
    {
        var id = await NuevaEstrategia();
        await F.SuperExec(@"insert into maestros.estudiante (id,tipo_documento_cif,numero_documento_cif,nombres_cif,fecha_nacimiento_cif,nombre_visible,sede_id)
            select 1000+g, '\x00', '\x00', '\x00', '\x00', 'Masivo '||g, 1 from generate_series(1,120) g on conflict do nothing;
          insert into maestros.estudiante_huella select sha256(('m'||g)::bytea), 1000+g, gen_random_uuid() from generate_series(1,120) g on conflict do nothing;
          insert into estrategias.estrategia_estudiante select e.id, 1000+g, 1 from estrategias.estrategia e, generate_series(1,120) g where e.public_id=@id", new { id = Guid.Parse(id) });
        var h = F.Gestor(); var vistos = new HashSet<string>(); string? cursor = null; var paginas = 0;
        do
        {
            var j = await (await h.GetAsync($"/api/v1/estrategias/{id}/estudiantes" + (cursor == null ? "" : $"?despuesDe={cursor}"))).Content.ReadFromJsonAsync<JsonElement>();
            foreach (var it in j.GetProperty("items").EnumerateArray()) Assert.True(vistos.Add(it.GetProperty("id").GetString()!));
            cursor = j.GetProperty("siguiente").GetString(); paginas++;
        } while (cursor != null);
        Assert.Equal(120, vistos.Count); Assert.Equal(3, paginas);
    }
}
