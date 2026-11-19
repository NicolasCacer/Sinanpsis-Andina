using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sses.Api.Tests;

/// <summary>Atajos compartidos por las pruebas de acompañamiento/sincronización.</summary>
public sealed class Escenario
{
    public SsesFixture F { get; init; } = null!;
    public Guid Estrategia; public Guid[] Docentes = []; public string Mes = ""; public Guid[] Hitos = []; public long[] Versiones = [];
    static int _mes;
    static readonly Guid E1 = new("00000000-0000-7000-8000-0000000000e1");

    /// <summary>Crea estrategia, asigna docentes 1..3 al tutor 1, programa un plan grupal de 3 semanas en un mes único.</summary>
    public static async Task<Escenario> Crear(SsesFixture f, string tutor = "tutor1", int semanas = 3)
    {
        var e = new Escenario { F = f, Mes = new DateTime(2031, 1, 1).AddMonths(Interlocked.Increment(ref _mes)).ToString("yyyy-MM") };
        var r = await f.Gestor().PostAsJsonAsync("/api/v1/estrategias", new { entidadId = E1, nombre = "Esc " + Guid.NewGuid(), objetivo = "o", poblacion = "p", fechaInicio = "2026-08-01", fechaFin = "2027-05-31" });
        e.Estrategia = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await f.SuperExec("insert into acompanamiento.asignacion select 1, d, e.id from generate_series(1,3) d, estrategias.estrategia e where e.public_id=@id", new { id = e.Estrategia });
        var docs = (await f.Tutor().GetFromJsonAsync<JsonElement>("/api/v1/tutores/me/docentes")).GetProperty("estrategias").EnumerateArray()
            .First(x => x.GetProperty("estrategia").GetProperty("id").GetGuid() == e.Estrategia).GetProperty("docentes").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToArray();
        e.Docentes = docs;
        var p = await f.Tutor().PostAsJsonAsync("/api/v1/acompanamientos", new { mes = e.Mes, modalidad = "GRUPAL", estrategiaId = e.Estrategia, docenteIds = docs, temas = "Tema", semanas = Enumerable.Range(1, semanas).ToArray() });
        Assert.Equal(HttpStatusCode.Created, p.StatusCode);
        var hitos = (await p.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("planes")[0].GetProperty("hitos").EnumerateArray().ToArray();
        e.Hitos = hitos.Select(h => h.GetProperty("id").GetGuid()).ToArray(); e.Versiones = hitos.Select(h => h.GetProperty("version").GetInt64()).ToArray();
        return e;
    }

    public object Cuerpo(bool todosAsisten = true, string actividades = "Taller de lectura", string avances = "Avance en comprensión", Guid[]? evidencias = null) => new
    {
        asistencia = Docentes.Select((d, i) => new { docenteId = d, asistio = todosAsisten || i > 0 }), actividades, avances, evidenciaIds = evidencias ?? []
    };

    public static HttpRequestMessage Put(Guid hito, object cuerpo, long version)
    {
        var r = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/hitos/{hito}/seguimiento") { Content = JsonContent.Create(cuerpo) };
        r.Headers.TryAddWithoutValidation("If-Match", $"W/\"{version}\""); return r;
    }

    public async Task<JsonElement> Plan(string tutor = "tutor1") => await F.Tutor(tutor).GetFromJsonAsync<JsonElement>($"/api/v1/tutores/me/plan?mes={Mes}");
    public async Task<long> VersionActual(int i) => (await Plan()).GetProperty("planes")[0].GetProperty("hitos")[i].GetProperty("version").GetInt64();
}
