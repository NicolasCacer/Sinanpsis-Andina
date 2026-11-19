using FluentValidation;

namespace Sses.Api.Estrategias;

public interface IEstrategiaDatos { string? Nombre { get; } string? Objetivo { get; } string? Poblacion { get; } DateOnly FechaInicio { get; } DateOnly FechaFin { get; } }
public sealed record EstrategiaCrear(Guid EntidadId, string? Nombre, string? Objetivo, string? Poblacion, DateOnly FechaInicio, DateOnly FechaFin) : IEstrategiaDatos;
public sealed record EstrategiaEditar(string? Nombre, string? Objetivo, string? Poblacion, DateOnly FechaInicio, DateOnly FechaFin) : IEstrategiaDatos;

/// <summary>Vigencia del programa piloto (fechas de la estrategia deben caer dentro).</summary>
public sealed class Vigencia(IConfiguration cfg)
{
    public DateOnly Inicio { get; } = DateOnly.Parse(cfg["Programa:Inicio"] ?? "2026-01-01");
    public DateOnly Fin { get; } = DateOnly.Parse(cfg["Programa:Fin"] ?? "2027-12-31");
}

// RQ_006: mismas reglas que el formulario del front (Zod)
public sealed class EstrategiaCrearValidator : AbstractValidator<EstrategiaCrear>
{
    public EstrategiaCrearValidator(Vigencia v) { RuleFor(x => x.EntidadId).NotEmpty().WithMessage("Selecciona una entidad."); this.ReglasComunes(v); }
}
public sealed class EstrategiaEditarValidator : AbstractValidator<EstrategiaEditar>
{
    public EstrategiaEditarValidator(Vigencia v) => this.ReglasComunes(v);
}

static class ReglasEstrategia
{
    public static void ReglasComunes<T>(this AbstractValidator<T> val, Vigencia v) where T : IEstrategiaDatos
    {
        val.RuleFor(x => x.Nombre).NotEmpty().WithMessage("El nombre es obligatorio.").MaximumLength(150).WithMessage("El nombre admite máximo 150 caracteres.");
        val.RuleFor(x => x.Objetivo).NotEmpty().WithMessage("El objetivo es obligatorio.").MaximumLength(2000).WithMessage("El objetivo admite máximo 2.000 caracteres.");
        val.RuleFor(x => x.Poblacion).NotEmpty().WithMessage("La población es obligatoria.").MaximumLength(300).WithMessage("La población admite máximo 300 caracteres.");
        val.RuleFor(x => x.FechaFin).GreaterThan(x => x.FechaInicio).WithMessage("La fecha de fin debe ser posterior a la de inicio.");
        val.RuleFor(x => x.FechaInicio).GreaterThanOrEqualTo(v.Inicio).WithMessage($"La fecha de inicio debe estar dentro de la vigencia del programa ({v.Inicio:yyyy-MM-dd} a {v.Fin:yyyy-MM-dd}).");
        val.RuleFor(x => x.FechaFin).LessThanOrEqualTo(v.Fin).WithMessage($"La fecha de fin debe estar dentro de la vigencia del programa ({v.Inicio:yyyy-MM-dd} a {v.Fin:yyyy-MM-dd}).");
    }
}
