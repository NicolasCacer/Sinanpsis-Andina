using Dapper;
using Npgsql;

namespace Sses.Api.Acompanamiento;

/// <summary>Lecturas del plan mensual del tutor (hitos con versión xmin, docentes, seguimiento, cumplimiento).</summary>
public static class PlanDelMes
{
    public sealed record Resultado(object Cuerpo, string Huella);

    public static async Task<Resultado> Cargar(NpgsqlConnection cn, long tutorId, string mes, NpgsqlTransaction? tx = null)
    {
        var planes = (await cn.QueryAsync<(long id, Guid public_id, Guid est_id, string est_nombre, string modalidad, string temas)>(
            @"select p.id, p.public_id, e.public_id est_id, e.nombre est_nombre, p.modalidad, p.temas
              from acompanamiento.plan p join estrategias.estrategia e on e.id=p.estrategia_id
              where p.tutor_id=@t and p.mes=@mes order by p.id", new { t = tutorId, mes }, tx)).ToList();
        var ids = planes.Select(p => p.id).ToArray();
        var docentes = (await cn.QueryAsync<(long plan_id, Guid public_id, string nombres)>(
            @"select pd.plan_id, d.public_id, d.nombres from acompanamiento.plan_docente pd join maestros.docente d on d.id=pd.docente_id
              where pd.plan_id = any(@ids) order by d.nombres", new { ids }, tx)).ToLookup(x => x.plan_id);
        var hitos = (await cn.QueryAsync<HitoFila>(
            @"select h.id, h.plan_id, h.public_id, h.numero_semana, h.semana_iso, h.fecha_inicio, h.fecha_fin, h.estado, h.xmin::text::bigint version,
                     s.id seg_id, s.actividades, s.avances, s.origen, s.registrado_en, s.capturado_en, pa.nombre registrado_por
              from acompanamiento.hito_semanal h
              left join lateral (select * from acompanamiento.seguimiento_semanal ss where ss.hito_id = h.id order by ss.registrado_en desc limit 1) s on true
              left join seguridad.participante pa on pa.id = s.registrado_por
              where h.plan_id = any(@ids) order by h.numero_semana", new { ids }, tx)).ToList();
        var segIds = hitos.Where(h => h.SegId != null).Select(h => h.SegId!.Value).ToArray();
        var asistencia = (await cn.QueryAsync<(long seguimiento_id, Guid docente_id, bool asistio)>(
            @"select a.seguimiento_id, d.public_id docente_id, a.asistio from acompanamiento.asistencia_seguimiento a join maestros.docente d on d.id=a.docente_id
              where a.seguimiento_id = any(@s)", new { s = segIds }, tx)).ToLookup(x => x.seguimiento_id);
        var evid = (await cn.QueryAsync<(long seguimiento_id, Guid public_id, string tipo_mime, int bytes)>(
            "select seguimiento_id, public_id, tipo_mime, bytes from acompanamiento.evidencia where seguimiento_id = any(@s)", new { s = segIds }, tx)).ToLookup(x => x.seguimiento_id);

        object Hito(HitoFila h) => new
        {
            id = h.PublicId, numeroSemana = (int)h.NumeroSemana, semanaIso = h.SemanaIso, fechaInicio = h.FechaInicio, fechaFin = h.FechaFin, estado = h.Estado, version = h.Version,
            seguimiento = h.SegId == null ? null : new
            {
                actividades = h.Actividades, avances = h.Avances, origen = h.Origen, registradoPor = h.RegistradoPor,
                registradoEn = h.RegistradoEn == null ? (DateTime?)null : DateTime.SpecifyKind(h.RegistradoEn.Value, DateTimeKind.Utc),
                asistencia = asistencia[h.SegId.Value].Select(a => new { docenteId = a.docente_id, asistio = a.asistio }),
                evidencias = evid[h.SegId.Value].Select(e => new { id = e.public_id, tipoMime = e.tipo_mime, bytes = e.bytes })
            }
        };
        var registradas = hitos.Count(h => h.Estado != "PROGRAMADA"); var total = hitos.Count;
        var cuerpo = new
        {
            mes,
            semanas = SemanasIso.Del(mes).Select(s => new { numero = s.Numero, semanaIso = s.SemanaIso, fechaInicio = s.Inicio, fechaFin = s.Fin }),
            planes = planes.Select(p => new
            {
                id = p.public_id, estrategia = new { id = p.est_id, nombre = p.est_nombre }, modalidad = p.modalidad, temas = p.temas,
                docentes = docentes[p.id].Select(d => new { id = d.public_id, nombres = d.nombres }),
                hitos = hitos.Where(h => h.PlanId == p.id).Select(Hito)
            }),
            cumplimiento = new { registradas, programadas = total, porcentaje = SemanasIso.Cumplimiento(registradas, total) }
        };
        return new Resultado(cuerpo, mes + "|" + string.Join(",", hitos.Select(h => $"{h.Id}:{h.Version}:{h.SegId}")));
    }

    public static async Task<(object cuerpo, long version)?> CargarHito(NpgsqlConnection cn, long tutorId, Guid hitoId, NpgsqlTransaction? tx = null)
    {
        var h = await cn.QuerySingleOrDefaultAsync<(long id, long plan_id, Guid public_id, short numero_semana, string semana_iso, DateOnly fecha_inicio, DateOnly fecha_fin, string estado, long version, string mes, Guid est_id, string est_nombre, string temas)?>(
            @"select h.id, h.plan_id, h.public_id, h.numero_semana, h.semana_iso, h.fecha_inicio, h.fecha_fin, h.estado, h.xmin::text::bigint version, p.mes, e.public_id est_id, e.nombre est_nombre, p.temas
              from acompanamiento.hito_semanal h join acompanamiento.plan p on p.id=h.plan_id join estrategias.estrategia e on e.id=p.estrategia_id
              where h.public_id=@id and p.tutor_id=@t", new { id = hitoId, t = tutorId }, tx);
        if (h == null) return null;
        var docs = await cn.QueryAsync<(Guid id, string nombres)>(
            "select d.public_id, d.nombres from acompanamiento.plan_docente pd join maestros.docente d on d.id=pd.docente_id where pd.plan_id=@p order by d.nombres", new { p = h.Value.plan_id }, tx);
        return (new
        {
            id = h.Value.public_id, mes = h.Value.mes, numeroSemana = (int)h.Value.numero_semana, semanaIso = h.Value.semana_iso, fechaInicio = h.Value.fecha_inicio, fechaFin = h.Value.fecha_fin,
            estado = h.Value.estado, version = h.Value.version, temas = h.Value.temas, estrategia = new { id = h.Value.est_id, nombre = h.Value.est_nombre },
            docentes = docs.Select(d => new { id = d.id, nombres = d.nombres })
        }, h.Value.version);
    }

    sealed class HitoFila
    {
        public long Id { get; set; } public long PlanId { get; set; } public Guid PublicId { get; set; } public short NumeroSemana { get; set; } public string SemanaIso { get; set; } = "";
        public DateOnly FechaInicio { get; set; } public DateOnly FechaFin { get; set; } public string Estado { get; set; } = ""; public long Version { get; set; }
        public long? SegId { get; set; } public string? Actividades { get; set; } public string? Avances { get; set; } public string? Origen { get; set; }
        public DateTime? RegistradoEn { get; set; } public DateTime? CapturadoEn { get; set; } public string? RegistradoPor { get; set; }
    }
}
