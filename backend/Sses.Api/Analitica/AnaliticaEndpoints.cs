using ClosedXML.Excel;
using Dapper;
using Npgsql;
using Sses.Api.Acompanamiento;
using Sses.Api.Comun;

namespace Sses.Api.Analitica;

/// <summary>HU_007 / CU_008 / RQ_013: tablero de desempeño de tutores sobre indicadores preagregados (analitica.mv_*), en transacciones de solo lectura.</summary>
public static class AnaliticaEndpoints
{
    sealed record Filtro(string Mes, int? Region, long? EntidadId, long? EstrategiaId);
    sealed class FilaTutor
    {
        public long TutorId { get; set; } public Guid PublicId { get; set; } public string Nombres { get; set; } = ""; public string Region { get; set; } = "";
        public int Programadas { get; set; } public int Registradas { get; set; } public int Confirmadas { get; set; } public int ATiempo { get; set; } public int Docentes { get; set; }
    }

    const string SqlTutores = @"
        select t.id tutor_id, t.public_id, t.nombres, r.nombre region,
               sum(c.programadas)::int programadas, sum(c.registradas)::int registradas, sum(c.confirmadas)::int confirmadas, sum(c.a_tiempo)::int a_tiempo,
               coalesce(sum(d.docentes_atendidos),0)::int docentes
        from analitica.mv_cumplimiento_tutor c
        join maestros.tutor t on t.id = c.tutor_id
        join maestros.region r on r.id = c.region_id
        left join analitica.mv_docentes_atendidos d on d.tutor_id=c.tutor_id and d.estrategia_id=c.estrategia_id and d.mes=c.mes
        where c.mes=@mes and (@region::int is null or c.region_id=@region) and (@entidad::bigint is null or c.entidad_id=@entidad) and (@est::bigint is null or c.estrategia_id=@est)
        group by t.id, t.public_id, t.nombres, r.nombre";

    static decimal Pct(int a, int b) => b == 0 ? 0 : Math.Round(100m * a / b, 2);
    static object Fila(FilaTutor f) => new
    {
        id = f.PublicId, nombres = f.Nombres, region = f.Region, programadas = f.Programadas, registradas = f.Registradas, confirmadas = f.Confirmadas,
        cumplimiento = Pct(f.Registradas, f.Programadas), regularidad = Pct(f.ATiempo, f.Registradas), docentesAtendidos = f.Docentes
    };

    static async Task<(Filtro? f, IResult? error)> LeerFiltro(string? mes, int? region, Guid? entidadId, Guid? estrategiaId, NpgsqlConnection cn, NpgsqlTransaction tx)
    {
        mes ??= DateTime.UtcNow.ToString("yyyy-MM");
        if (!SemanasIso.MesValido(mes)) return (null, Problemas.Validacion(new Dictionary<string, string[]> { ["mes"] = ["Elige un mes válido (AAAA-MM)."] }));
        long? ent = null, est = null;
        if (entidadId != null) { ent = await cn.ExecuteScalarAsync<long?>("select id from maestros.entidad where public_id=@i", new { i = entidadId }, tx); if (ent == null) return (null, Problemas.NoEncontrado("ENTIDAD_NO_ENCONTRADA", "La entidad no existe.")); }
        if (estrategiaId != null) { est = await cn.ExecuteScalarAsync<long?>("select id from estrategias.estrategia where public_id=@i", new { i = estrategiaId }, tx); if (est == null) return (null, Problemas.NoEncontrado("ESTRATEGIA_NO_ENCONTRADA", "La estrategia no existe.")); }
        return (new Filtro(mes, region, ent, est), null);
    }
    static object P(Filtro f) => new { mes = f.Mes, region = f.Region, entidad = f.EntidadId, est = f.EstrategiaId };

    static async Task<(NpgsqlConnection cn, NpgsqlTransaction tx)> SoloLectura(NpgsqlDataSource ds)
    {
        var cn = await ds.OpenConnectionAsync(); var tx = await cn.BeginTransactionAsync();
        await cn.ExecuteAsync("set transaction read only", transaction: tx); // RQ_013: la consulta nunca escribe en la operación
        return (cn, tx);
    }

    public static void MapAnalitica(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/analitica").RequireAuthorization(Auth.PAnalista);

        g.MapGet("/filtros", async (NpgsqlDataSource ds) =>
        {
            await using var cn = await ds.OpenConnectionAsync();
            var regiones = await cn.QueryAsync<(int id, string nombre)>("select id, nombre from maestros.region order by nombre");
            var entidades = await cn.QueryAsync<(Guid id, string nombre)>("select public_id id, nombre from maestros.entidad where activa order by nombre");
            var estrategias = await cn.QueryAsync<(Guid id, string nombre, Guid entidad_id)>("select e.public_id id, e.nombre, en.public_id entidad_id from estrategias.estrategia e join maestros.entidad en on en.id=e.entidad_id order by e.nombre");
            return Results.Ok(new { regiones = regiones.Select(r => new { r.id, r.nombre }), entidades = entidades.Select(e => new { e.id, e.nombre }), estrategias = estrategias.Select(e => new { e.id, e.nombre, entidadId = e.entidad_id }) });
        });

        // RQ_013: indicadores del mes; cada consulta queda en la bitácora
        g.MapGet("/tutores", async (string? mes, int? region, Guid? entidadId, Guid? estrategiaId, NpgsqlDataSource ds, IBitacora bit) =>
        {
            var (cn, tx) = await SoloLectura(ds); await using var _c = cn; await using var _t = tx;
            var (f, err) = await LeerFiltro(mes, region, entidadId, estrategiaId, cn, tx); if (err != null) return err;
            var filas = (await cn.QueryAsync<FilaTutor>(SqlTutores, P(f!), tx)).ToList();
            var prog = filas.Sum(x => x.Programadas); var reg = filas.Sum(x => x.Registradas);
            var porRegion = filas.GroupBy(x => x.Region).Select(gr => new { region = gr.Key, tutores = gr.Count(), cumplimiento = Pct(gr.Sum(x => x.Registradas), gr.Sum(x => x.Programadas)), regularidad = Pct(gr.Sum(x => x.ATiempo), gr.Sum(x => x.Registradas)) }).OrderBy(x => x.region);
            var ranking = filas.OrderByDescending(x => Pct(x.Registradas, x.Programadas)).ThenByDescending(x => x.Docentes).ThenBy(x => x.Nombres).Take(100).Select(Fila);
            await tx.CommitAsync();
            await bit.RegistrarAsync("CONSULTAR_TABLERO", "analitica", f!.Mes, null, new { f.Mes, f.Region, entidad = entidadId, estrategia = estrategiaId, tutores = filas.Count });
            return Results.Ok(new
            {
                mes = f.Mes,
                resumen = new { tutores = filas.Count, programadas = prog, registradas = reg, cumplimiento = Pct(reg, prog), regularidad = Pct(filas.Sum(x => x.ATiempo), reg), docentesAtendidos = filas.Sum(x => x.Docentes) },
                porRegion, ranking
            });
        });

        // Detalle semana a semana de un tutor (programadas, registradas, docentes atendidos)
        g.MapGet("/tutores/{id:guid}/detalle", async (Guid id, string? mes, Guid? entidadId, Guid? estrategiaId, NpgsqlDataSource ds, IBitacora bit) =>
        {
            var (cn, tx) = await SoloLectura(ds); await using var _c = cn; await using var _t = tx;
            var (f, err) = await LeerFiltro(mes, null, entidadId, estrategiaId, cn, tx); if (err != null) return err;
            var t = await cn.QuerySingleOrDefaultAsync<(long id, string nombres, string region)?>("select t.id, t.nombres, r.nombre region from maestros.tutor t join maestros.municipio m on m.id=t.municipio_id join maestros.region r on r.id=m.region_id where t.public_id=@id", new { id }, tx);
            if (t == null) return Problemas.NoEncontrado("TUTOR_NO_ENCONTRADO", "El tutor no existe.");
            var semanas = await cn.QueryAsync<(short numero, string iso, DateOnly ini, DateOnly fin, int prog, int reg, int conf, int docentes)>(
                @"select h.numero_semana, h.semana_iso, min(h.fecha_inicio), min(h.fecha_fin), count(*)::int,
                         count(*) filter (where h.estado <> 'PROGRAMADA')::int, count(*) filter (where h.estado in ('CONFIRMADO','OBSERVADO'))::int,
                         (select count(distinct a.docente_id)::int from acompanamiento.asistencia_seguimiento a join acompanamiento.seguimiento_semanal s on s.id=a.seguimiento_id
                            join acompanamiento.hito_semanal h2 on h2.id=s.hito_id join acompanamiento.plan p2 on p2.id=h2.plan_id
                            where a.asistio and p2.tutor_id=@t and p2.mes=@mes and h2.numero_semana=h.numero_semana and (@est::bigint is null or p2.estrategia_id=@est) and (@entidad::bigint is null or p2.estrategia_id in (select id from estrategias.estrategia where entidad_id=@entidad)))
                  from acompanamiento.hito_semanal h join acompanamiento.plan p on p.id=h.plan_id join estrategias.estrategia e on e.id=p.estrategia_id
                  where p.tutor_id=@t and p.mes=@mes and (@est::bigint is null or p.estrategia_id=@est) and (@entidad::bigint is null or e.entidad_id=@entidad)
                  group by h.numero_semana, h.semana_iso order by h.numero_semana",
                new { t = t.Value.id, mes = f!.Mes, est = f.EstrategiaId, entidad = f.EntidadId }, tx);
            await tx.CommitAsync();
            await bit.RegistrarAsync("CONSULTAR_TABLERO", "tutor", id.ToString(), null, new { f.Mes, detalle = true });
            var lista = semanas.ToList();
            return Results.Ok(new
            {
                tutor = new { id, nombres = t.Value.nombres, region = t.Value.region }, mes = f.Mes,
                semanas = lista.Select(s => new { numero = (int)s.numero, semanaIso = s.iso, fechaInicio = s.ini, fechaFin = s.fin, programadas = s.prog, registradas = s.reg, confirmadas = s.conf, docentesAtendidos = s.docentes, cumplimiento = Pct(s.reg, s.prog) }),
                cumplimiento = Pct(lista.Sum(s => s.reg), lista.Sum(s => s.prog))
            });
        });

        // HU_007: 'Exportar' genera la tabla filtrada (XLSX con ClosedXML)
        g.MapGet("/tutores/exportar", async (string? mes, int? region, Guid? entidadId, Guid? estrategiaId, NpgsqlDataSource ds, IBitacora bit) =>
        {
            var (cn, tx) = await SoloLectura(ds); await using var _c = cn; await using var _t = tx;
            var (f, err) = await LeerFiltro(mes, region, entidadId, estrategiaId, cn, tx); if (err != null) return err;
            var filas = (await cn.QueryAsync<FilaTutor>(SqlTutores, P(f!), tx)).OrderByDescending(x => Pct(x.Registradas, x.Programadas)).ThenBy(x => x.Nombres).Take(50_000).ToList();
            await tx.CommitAsync();
            using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Desempeño de tutores");
            string[] cab = ["Puesto", "Tutor", "Región", "Semanas programadas", "Semanas registradas", "Cumplimiento %", "Regularidad %", "Docentes atendidos"];
            for (var i = 0; i < cab.Length; i++) { var c = ws.Cell(1, i + 1); c.Value = cab[i]; c.Style.Font.Bold = true; }
            for (var i = 0; i < filas.Count; i++)
            {
                var x = filas[i]; var r = i + 2;
                ws.Cell(r, 1).Value = i + 1; ws.Cell(r, 2).Value = x.Nombres; ws.Cell(r, 3).Value = x.Region; ws.Cell(r, 4).Value = x.Programadas; ws.Cell(r, 5).Value = x.Registradas;
                ws.Cell(r, 6).Value = Pct(x.Registradas, x.Programadas); ws.Cell(r, 7).Value = Pct(x.ATiempo, x.Registradas); ws.Cell(r, 8).Value = x.Docentes;
            }
            ws.Columns().AdjustToContents();
            using var ms = new MemoryStream(); wb.SaveAs(ms);
            await bit.RegistrarAsync("EXPORTAR_TABLERO", "analitica", f!.Mes, null, new { f.Mes, f.Region, entidad = entidadId, estrategia = estrategiaId, filas = filas.Count });
            return Results.File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"desempeno-tutores-{f.Mes}.xlsx");
        });
    }
}

/// <summary>RQ_013 (D22): refresca los indicadores cada 15 minutos con REFRESH MATERIALIZED VIEW CONCURRENTLY (sustituye a pg_cron).</summary>
public sealed class RefrescoAnalitica(NpgsqlDataSource ds, ILogger<RefrescoAnalitica> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var t = new PeriodicTimer(TimeSpan.FromMinutes(15));
        try
        {
            while (await t.WaitForNextTickAsync(ct))
            {
                try { await using var cn = await ds.OpenConnectionAsync(ct); await cn.ExecuteAsync("select analitica.refrescar()"); }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "No se pudo refrescar la analítica"); }
            }
        }
        catch (OperationCanceledException) { }
    }
}
