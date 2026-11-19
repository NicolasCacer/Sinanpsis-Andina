using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;
using Sses.Api.Comun;

namespace Sses.Api.Acompanamiento;

public sealed record ProgramarPlan(string? Mes, string? Modalidad, Guid EstrategiaId, Guid[]? DocenteIds, string? Temas, int[]? Semanas);

public static class AcompanamientoEndpoints
{
    public static void MapAcompanamiento(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").RequireAuthorization(Auth.PTutorLider);

        // RQ_010: docentes asignados al tutor, agrupados por estrategia
        g.MapGet("/tutores/me/docentes", async (NpgsqlDataSource ds, IUsuarioActual ua) =>
        {
            var u = await ua.ObtenerAsync(); if (u?.PersonaId == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            await using var cn = await ds.OpenConnectionAsync();
            var filas = await cn.QueryAsync<(Guid est_id, string est_nombre, string entidad, Guid doc_id, string doc_nombre)>(
                @"select e.public_id est_id, e.nombre est_nombre, en.nombre entidad, d.public_id doc_id, d.nombres doc_nombre
                  from acompanamiento.asignacion a join maestros.docente d on d.id=a.docente_id
                  join estrategias.estrategia e on e.id=a.estrategia_id join maestros.entidad en on en.id=e.entidad_id
                  where a.tutor_id=@t and a.vigente order by e.nombre, d.nombres", new { t = u.PersonaId });
            var grupos = filas.GroupBy(f => (f.est_id, f.est_nombre, f.entidad)).Select(gr => new
            {
                estrategia = new { id = gr.Key.est_id, nombre = gr.Key.est_nombre, entidad = gr.Key.entidad },
                docentes = gr.Select(x => new { id = x.doc_id, nombres = x.doc_nombre })
            });
            return Results.Ok(new { estrategias = grupos });
        });

        // RQ_010: programa el plan mensual (individual: 1 plan por docente; grupal: 1 plan con varios docentes) y un hito por semana
        g.MapPost("/acompanamientos", async (ProgramarPlan dto, NpgsqlDataSource ds, IUsuarioActual ua, IBitacora bit) =>
        {
            var u = await ua.ObtenerAsync(); if (u?.PersonaId == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            var errores = new Dictionary<string, string[]>();
            var modalidad = dto.Modalidad?.ToUpperInvariant();
            if (!SemanasIso.MesValido(dto.Mes)) errores["mes"] = ["Elige un mes válido (AAAA-MM)."];
            if (modalidad is not ("INDIVIDUAL" or "GRUPAL")) errores["modalidad"] = ["Elige Individual o Grupal."];
            var docIds = dto.DocenteIds?.Distinct().ToArray() ?? [];
            if (docIds.Length == 0) errores["docenteIds"] = ["Marca al menos un docente."];
            else if (modalidad == "GRUPAL" && docIds.Length < 2) errores["docenteIds"] = ["Un plan grupal necesita al menos 2 docentes."];
            if (string.IsNullOrWhiteSpace(dto.Temas)) errores["temas"] = ["Escribe los temas."]; else if (dto.Temas.Length > 2000) errores["temas"] = ["Los temas admiten máximo 2.000 caracteres."];
            var semanasSel = dto.Semanas?.Distinct().Order().ToArray() ?? [];
            if (semanasSel.Length == 0) errores["semanas"] = ["Selecciona al menos una semana."];
            if (errores.Count > 0) return Problemas.Validacion(errores);
            if (modalidad == "GRUPAL" && docIds.Length > 30) return Problemas.De(422, "GRUPO_EXCEDE_30", "Un plan grupal admite máximo 30 docentes.");
            var semanasMes = SemanasIso.Del(dto.Mes!);
            if (semanasSel.Any(s => s < 1 || s > semanasMes.Count)) return Problemas.De(422, "SEMANA_FUERA_DEL_MES", $"Ese mes tiene {semanasMes.Count} semanas (1 a {semanasMes.Count}).");

            await using var cn = await ds.OpenConnectionAsync(); await using var tx = await cn.BeginTransactionAsync();
            var asignados = (await cn.QueryAsync<(long id, Guid public_id, string nombres, long estrategia_id)>(
                @"select d.id, d.public_id, d.nombres, e.id estrategia_id from acompanamiento.asignacion a
                  join maestros.docente d on d.id=a.docente_id join estrategias.estrategia e on e.id=a.estrategia_id
                  where a.tutor_id=@t and a.vigente and e.public_id=@e and d.public_id = any(@ids)", new { t = u.PersonaId, e = dto.EstrategiaId, ids = docIds }, tx)).ToList();
            if (asignados.Count != docIds.Length) return Problemas.De(422, "DOCENTE_NO_ASIGNADO", "Alguno de los docentes no está asignado a ti en esa estrategia.");
            var repetidos = (await cn.QueryAsync<string>(
                @"select d.nombres from acompanamiento.plan_docente pd join maestros.docente d on d.id=pd.docente_id
                  where pd.tutor_id=@t and pd.mes=@mes and pd.docente_id = any(@ids)", new { t = u.PersonaId, mes = dto.Mes, ids = asignados.Select(a => a.id).ToArray() }, tx)).ToList();
            if (repetidos.Count > 0) return Problemas.De(409, "PLAN_DUPLICADO", $"Ya existe un plan de {dto.Mes} para: {string.Join(", ", repetidos)}.", new { docentes = repetidos });

            var grupos = modalidad == "INDIVIDUAL" ? asignados.Select(a => new[] { a }).ToList() : [asignados.ToArray()];
            var creados = new List<object>();
            try
            {
                foreach (var grupo in grupos)
                {
                    var planPub = Guid.CreateVersion7();
                    var planId = await cn.ExecuteScalarAsync<long>(@"insert into acompanamiento.plan (public_id,tutor_id,estrategia_id,mes,modalidad,temas)
                        values (@p,@t,@e,@mes,@mod,@temas) returning id", new { p = planPub, t = u.PersonaId, e = grupo[0].estrategia_id, mes = dto.Mes, mod = modalidad, temas = dto.Temas!.Trim() }, tx);
                    foreach (var d in grupo)
                        await cn.ExecuteAsync("insert into acompanamiento.plan_docente (plan_id,docente_id,tutor_id,mes) values (@p,@d,@t,@mes)", new { p = planId, d = d.id, t = u.PersonaId, mes = dto.Mes }, tx);
                    var hitos = new List<object>();
                    foreach (var n in semanasSel)
                    {
                        var s = semanasMes[n - 1]; var hp = Guid.CreateVersion7();
                        var ver = await cn.ExecuteScalarAsync<long>(@"insert into acompanamiento.hito_semanal (public_id,plan_id,numero_semana,semana_iso,fecha_inicio,fecha_fin)
                            values (@p,@pl,@n,@iso,@fi,@ff) returning xmin::text::bigint", new { p = hp, pl = planId, n = (short)n, iso = s.SemanaIso, fi = s.Inicio, ff = s.Fin }, tx);
                        hitos.Add(new { id = hp, numeroSemana = n, semanaIso = s.SemanaIso, fechaInicio = s.Inicio, fechaFin = s.Fin, estado = "PROGRAMADA", version = ver });
                    }
                    await bit.RegistrarAsync(cn, tx, "PROGRAMAR_PLAN", "plan", planPub.ToString(), null, new { dto.Mes, modalidad, estrategiaId = dto.EstrategiaId, docentes = grupo.Select(x => x.public_id), semanas = semanasSel });
                    creados.Add(new { id = planPub, modalidad, mes = dto.Mes, docentes = grupo.Select(x => new { id = x.public_id, nombres = x.nombres }), hitos });
                }
                await tx.CommitAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505") { return Problemas.De(409, "PLAN_DUPLICADO", "Ya existe un plan de ese mes para alguno de los docentes."); }
            return Results.Created("/api/v1/tutores/me/plan?mes=" + dto.Mes, new { planes = creados });
        });

        // RQ_010: plan completo del mes (lo que el front guarda en IndexedDB). ETag por versión de hitos.
        g.MapGet("/tutores/me/plan", async (string? mes, HttpContext ctx, NpgsqlDataSource ds, IUsuarioActual ua) =>
        {
            var u = await ua.ObtenerAsync(); if (u?.PersonaId == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            if (!SemanasIso.MesValido(mes)) return Problemas.Validacion(new Dictionary<string, string[]> { ["mes"] = ["Elige un mes válido (AAAA-MM)."] });
            await using var cn = await ds.OpenConnectionAsync();
            var plan = await PlanDelMes.Cargar(cn, u.PersonaId.Value, mes!);
            var etag = $"W/\"{Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(plan.Huella)))[..16]}\"";
            ctx.Response.Headers.ETag = etag;
            if (ctx.Request.Headers.IfNoneMatch.ToString() == etag) return Results.StatusCode(304);
            return Results.Ok(plan.Cuerpo);
        });

        // RQ_011: hito con docentes y versión (insumo del formulario de seguimiento)
        g.MapGet("/hitos/{id:guid}", async (Guid id, HttpContext ctx, NpgsqlDataSource ds, IUsuarioActual ua) =>
        {
            var u = await ua.ObtenerAsync(); if (u?.PersonaId == null) return Problemas.De(403, "USUARIO_NO_HABILITADO", "Tu usuario no está habilitado.");
            await using var cn = await ds.OpenConnectionAsync();
            var h = await PlanDelMes.CargarHito(cn, u.PersonaId.Value, id);
            if (h == null) return Problemas.NoEncontrado("HITO_NO_ENCONTRADO", "El hito no existe.");
            ctx.Response.Headers.ETag = Datos.Etag(h.Value.version);
            return Results.Ok(h.Value.cuerpo);
        });
    }
}
