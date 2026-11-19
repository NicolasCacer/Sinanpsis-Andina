using System.Globalization;

namespace Sses.Api.Acompanamiento;

public sealed record SemanaMes(int Numero, string SemanaIso, DateOnly Inicio, DateOnly Fin);

/// <summary>RQ_010 (D14): semanas ISO 8601 (lun–dom) de un mes = las que tienen su jueves dentro del mes; 4 o 5 por mes, numeradas 1..5.</summary>
public static class SemanasIso
{
    public static bool MesValido(string? mes) => mes is { Length: 7 } && DateOnly.TryParseExact(mes + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static IReadOnlyList<SemanaMes> Del(string mes)
    {
        var primero = DateOnly.ParseExact(mes + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var ultimo = primero.AddMonths(1).AddDays(-1);
        var res = new List<SemanaMes>();
        for (var d = primero; d <= ultimo; d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Thursday) continue;
            var dt = d.ToDateTime(TimeOnly.MinValue);
            var iso = $"{ISOWeek.GetYear(dt):0000}-W{ISOWeek.GetWeekOfYear(dt):00}";
            res.Add(new SemanaMes(res.Count + 1, iso, d.AddDays(-3), d.AddDays(3)));
        }
        return res;
    }

    /// <summary>cumplimiento = registradas / programadas, 2 decimales (porcentaje 0..100).</summary>
    public static decimal Cumplimiento(int registradas, int total) => total == 0 ? 0m : Math.Round(100m * registradas / total, 2);
}
