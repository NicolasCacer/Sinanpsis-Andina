using Dapper;
using Npgsql;
using System.Data;

namespace Sses.Api.Comun;

public static class Datos
{
    public static void Configurar()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
    }

    public static async Task<NpgsqlConnection> Abrir(this NpgsqlDataSource ds, CancellationToken ct = default) => await ds.OpenConnectionAsync(ct);

    sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter p, DateOnly v) { p.DbType = DbType.Date; p.Value = v.ToDateTime(TimeOnly.MinValue); }
        public override DateOnly Parse(object v) => v is DateTime d ? DateOnly.FromDateTime(d) : (DateOnly)v;
    }

    /// <summary>ETag débil a partir de xmin (concurrencia optimista).</summary>
    public static string Etag(long xmin) => $"W/\"{xmin}\"";
    public static long? ParseEtag(string? h)
    {
        if (string.IsNullOrWhiteSpace(h)) return null;
        var t = h.Trim(); if (t.StartsWith("W/")) t = t[2..]; t = t.Trim('"');
        return long.TryParse(t, out var v) ? v : null;
    }
}
