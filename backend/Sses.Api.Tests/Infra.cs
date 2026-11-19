using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Sses.Api.Comun;
using Testcontainers.PostgreSql;
using Microsoft.AspNetCore.TestHost;

namespace Sses.Api.Tests;

/// <summary>Autenticación de pruebas: X-Test-Sub / X-Test-Role reemplazan al JWT de Keycloak (el JWT real se prueba en E2E).</summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Sub", out var sub)) return Task.FromResult(AuthenticateResult.NoResult());
        var rol = Request.Headers["X-Test-Role"].ToString();
        var claims = new List<Claim> { new("sub", sub!), new("preferred_username", sub + "@test.co"), new(ClaimTypes.Role, rol) };
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test")));
    }
}

public sealed class SsesFixture : IAsyncLifetime
{
    public static readonly byte[] ClaveAes = RandomNumberGenerator.GetBytes(32), ClaveHmac = RandomNumberGenerator.GetBytes(32);
    public ProteccionDatos Prot { get; } = new(ClaveAes, ClaveHmac);
    PostgreSqlContainer _pg = null!;
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string ConnSuper = "", ConnApi = "";

    static string RutaInfra()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "infra", "postgres"))) return Path.Combine(d.FullName, "infra", "postgres");
        throw new DirectoryNotFoundException("No se encontró infra/postgres");
    }

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true"); // sin pull de Ryuk; el fixture elimina el contenedor
        _pg = new PostgreSqlBuilder("postgres:17").WithPassword("pg").WithBindMount(RutaInfra(), "/docker-entrypoint-initdb.d").Build();
        await _pg.StartAsync();
        var host = _pg.Hostname; var port = _pg.GetMappedPublicPort(5432);
        ConnSuper = $"Host={host};Port={port};Database=sses;Username=postgres;Password=pg;Pooling=false";
        ConnApi = $"Host={host};Port={port};Database=sses;Username=sses_api;Password=sses_api_dev";
        // el servidor reinicia tras los scripts de init: esperar a que el esquema exista
        for (var i = 0; i < 90; i++)
        {
            try { await using var c = new NpgsqlConnection(ConnSuper); await c.OpenAsync(); if (await c.ExecuteScalarAsync<long>("select count(*) from information_schema.tables where table_schema='auditoria'") > 0 && await c.ExecuteScalarAsync<bool>("select has_table_privilege('sses_api','auditoria.bitacora','INSERT')")) break; }
            catch { }
            await Task.Delay(1000);
        }
        await Sembrar();
        Environment.SetEnvironmentVariable("ConnectionStrings__Sses", ConnApi);
        Environment.SetEnvironmentVariable("Evidencias__Ruta", Path.Combine(Path.GetTempPath(), "sses-evid-" + Guid.NewGuid()));
        Environment.SetEnvironmentVariable("SSES__CLAVE_AES", Convert.ToBase64String(ClaveAes));
        Environment.SetEnvironmentVariable("SSES__CLAVE_HMAC", Convert.ToBase64String(ClaveHmac));
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddAuthentication(o => { o.DefaultScheme = "Test"; o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; })
             .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { })));
    }

    async Task Sembrar()
    {
        await using var c = new NpgsqlConnection(ConnSuper); await c.OpenAsync();
        await c.ExecuteAsync(@"
          insert into maestros.region values (1,'Pacífico');
          insert into maestros.municipio values (1,1,'Tumaco','52835');
          insert into maestros.sede values (1,'I.E. Prueba Uno',1),(2,'I.E. Prueba Dos',1);");
    }
    public async Task InsertarBase()
    {
        await using var c = new NpgsqlConnection(ConnSuper); await c.OpenAsync();
        await c.ExecuteAsync(@"
          insert into maestros.entidad (public_id,nombre,municipio_id,activa) values
            ('00000000-0000-7000-8000-0000000000e1','Entidad Uno',1,true),('00000000-0000-7000-8000-0000000000e2','Entidad Dos',1,true),
            ('00000000-0000-7000-8000-0000000000e3','Entidad Inactiva',1,false),('00000000-0000-7000-8000-0000000000e4','Entidad Ajena',1,true);
          insert into seguridad.participante (keycloak_sub,correo,nombre,rol,persona_tipo,persona_id) values
            ('gestorA','gestorA@test.co','Gestora A','GESTOR_ESTRATEGIAS','FUNCIONARIO',null),
            ('gestorB','gestorB@test.co','Gestor B','GESTOR_ESTRATEGIAS','FUNCIONARIO',null),
            ('gestorC','gestorC@test.co','Gestor C','GESTOR_ESTRATEGIAS','FUNCIONARIO',null),
            ('tutor1','tutor1@test.co','Tutor Uno','TUTOR_LIDER','TUTOR',1),
            ('tutor2','tutor2@test.co','Tutor Dos','TUTOR_LIDER','TUTOR',2);
          insert into seguridad.gestor_entidad select p.id, e.id from seguridad.participante p, maestros.entidad e
            where (p.keycloak_sub in ('gestorA','gestorB') and e.nombre in ('Entidad Uno','Entidad Dos','Entidad Inactiva')) or (p.keycloak_sub='gestorC' and e.nombre='Entidad Ajena');
          insert into maestros.tutor (nombres,correo,municipio_id) values ('Tutor Uno','tutor1@test.co',1),('Tutor Dos','tutor2@test.co',1);
          insert into maestros.docente (nombres,correo,sede_id) values ('Doc A','a@t.co',1),('Doc B','b@t.co',1),('Doc C','c@t.co',2),('Doc D','d@t.co',2);");
        for (var i = 1; i <= 6; i++)
        {
            var num = (1000000000 + i).ToString();
            await c.ExecuteAsync(@"insert into maestros.estudiante (id,tipo_documento_cif,numero_documento_cif,nombres_cif,fecha_nacimiento_cif,nombre_visible,sede_id) values (@id,@t,@n,@nm,@f,@v,1);
                insert into maestros.estudiante_huella (huella,estudiante_id,public_id) values (@h,@id,@p)",
                new { id = (long)i, t = Prot.Cifrar("TI"), n = Prot.Cifrar(num), nm = Prot.Cifrar("Nombre Secreto " + i), f = Prot.Cifrar("2012-01-01"), v = $"Estudiante {i}", h = Prot.Huella("TI", num), p = new Guid($"00000000-0000-7000-8000-00000000005{i}") });
        }
    }

    public HttpClient Cliente(string sub, string rol)
    {
        var h = Factory.CreateClient();
        h.DefaultRequestHeaders.Add("X-Test-Sub", sub); h.DefaultRequestHeaders.Add("X-Test-Role", rol);
        return h;
    }
    public HttpClient Gestor(string sub = "gestorA") => Cliente(sub, "GESTOR_ESTRATEGIAS");
    public HttpClient Tutor(string sub = "tutor1") => Cliente(sub, "TUTOR_LIDER");

    public async Task<T> Super<T>(string sql, object? p = null) { await using var c = new NpgsqlConnection(ConnSuper); await c.OpenAsync(); return await c.ExecuteScalarAsync<T>(sql, p) ?? default!; }
    public async Task SuperExec(string sql, object? p = null) { await using var c = new NpgsqlConnection(ConnSuper); await c.OpenAsync(); await c.ExecuteAsync(sql, p); }

    public async Task DisposeAsync() { Factory?.Dispose(); await _pg.DisposeAsync(); }
}

[CollectionDefinition("sses")]
public sealed class SsesCollection : ICollectionFixture<SsesFixtureInicial>;

/// <summary>Fixture que además inserta los datos base una sola vez.</summary>
public sealed class SsesFixtureInicial : IAsyncLifetime
{
    public SsesFixture F { get; } = new();
    public async Task InitializeAsync() { await F.InitializeAsync(); await F.InsertarBase(); }
    public Task DisposeAsync() => F.DisposeAsync();
}
