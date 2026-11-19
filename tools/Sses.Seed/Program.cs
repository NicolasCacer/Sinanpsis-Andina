// Generador de datos sintéticos SSES (RQ_002, RQ_026). Determinista (semilla fija). Solo datos ficticios.
using System.Diagnostics;
using Npgsql;
using NpgsqlTypes;
using Sses.Api.Comun;

int n = 500_000;
string conn = Environment.GetEnvironmentVariable("SSES_SEED_CONN")
    ?? "Host=localhost;Port=5433;Database=sses;Username=sses_owner;Password=sses_owner_dev;Timeout=60;Command Timeout=0";
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--estudiantes") n = int.Parse(args[++i]);
    else if (args[i] == "--conn") conn = args[++i];
}
LeerDotEnv();
var prot = ProteccionDatos.DesdeConfiguracion(new ConfigurationBuilderLite());
var rnd = new Random(20261113);
var sw = Stopwatch.StartNew();

// Sub de Keycloak fijos (el realm importado usa los mismos id)
const string SubAdmin = "7a000000-0000-4000-8000-000000000001", SubGestor = "7a000000-0000-4000-8000-000000000002",
             SubTutor = "7a000000-0000-4000-8000-000000000003", SubDocente = "7a000000-0000-4000-8000-000000000004";

string[] nombres = ["Camila","Santiago","Valentina","Mateo","Sofía","Samuel","Isabella","Juan","Mariana","Daniel","Luciana","Nicolás","Sara","Emmanuel","Laura","Tomás","Gabriela","Andrés","Daniela","Julián","Manuela","Sebastián","Paula","David","Salomé","Felipe","Antonella","Miguel","Juliana","Esteban","Karen","Cristian","Yulieth","Brayan","Mayerly","Jhon","Dayana","Kevin","Luisa","Alexander"];
string[] apellidos = ["Rojas","Pérez","Gómez","Rodríguez","Martínez","López","García","Hernández","Torres","Ramírez","Castro","Mosquera","Valencia","Angulo","Caicedo","Quiñones","Cortés","Mendoza","Ortiz","Vargas","Moreno","Ríos","Jiménez","Arias","Campaz","Preciado","Cuero","Riascos","Ospina","Peña"];
(string reg, string[] muns)[] geo = [
  ("Caribe", ["Barranquilla","Cartagena","Santa Marta","Riohacha","Valledupar"]),
  ("Pacífico", ["Tumaco","Quibdó","Buenaventura","Guapi","Timbiquí"]),
  ("Andina", ["Medellín","Bucaramanga","Tunja","Manizales","Pasto"]),
  ("Orinoquía", ["Villavicencio","Yopal","Arauca","Puerto Carreño","Puerto López"]),
  ("Amazonía", ["Leticia","Florencia","Mocoa","Puerto Inírida","San José del Guaviare"]),
  ("Bogotá D. C.", ["Bosa","Kennedy","Usme","Ciudad Bolívar","Suba"])];
string[] prefEnt = ["Fundación","Corporación","Asociación","Cooperativa"];
string[] nomEnt = ["Semillas del","Raíces del","Horizontes del","Manos del","Voces del","Tejedores del","Caminos del","Guardianes del","Sueños del","Luz del"];

string Pick(string[] a) => a[rnd.Next(a.Length)];
string Nombre() => $"{Pick(nombres)} {Pick(apellidos)} {Pick(apellidos)}";

await using var cn = new NpgsqlConnection(conn);
await cn.OpenAsync();
async Task Exec(string sql, params (string, object)[] ps)
{
    await using var c = new NpgsqlCommand(sql, cn);
    foreach (var (k, v) in ps) c.Parameters.AddWithValue(k, v);
    await c.ExecuteNonQueryAsync();
}
async Task<long> Scalar(string sql) { await using var c = new NpgsqlCommand(sql, cn); return Convert.ToInt64(await c.ExecuteScalarAsync()); }

Console.WriteLine("Limpiando tablas maestras/transaccionales…");
await Exec(@"truncate maestros.region, maestros.municipio, maestros.entidad, maestros.sede, maestros.estudiante, maestros.estudiante_huella,
  maestros.tutor, maestros.docente, seguridad.participante, seguridad.gestor_entidad, estrategias.estrategia, estrategias.estrategia_estudiante,
  acompanamiento.asignacion, acompanamiento.plan, acompanamiento.plan_docente, acompanamiento.hito_semanal, acompanamiento.seguimiento_semanal,
  acompanamiento.asistencia_seguimiento, acompanamiento.evidencia, acompanamiento.idempotencia restart identity cascade");

// Regiones, municipios, sedes
int munId = 0; var munPorRegion = new Dictionary<int, List<int>>(); var munNombre = new Dictionary<int, string>();
for (int r = 0; r < geo.Length; r++)
{
    await Exec("insert into maestros.region values (@i,@n)", ("i", r + 1), ("n", geo[r].reg));
    munPorRegion[r + 1] = [];
    foreach (var m in geo[r].muns)
    {
        munId++; munPorRegion[r + 1].Add(munId); munNombre[munId] = m;
        await Exec("insert into maestros.municipio values (@i,@r,@n,@d)", ("i", munId), ("r", r + 1), ("n", m), ("d", (10000 + munId * 37).ToString("00000")));
    }
}
int tumaco = munNombre.First(k => k.Value == "Tumaco").Key;
const int NSedes = 300; var sedeMun = new int[NSedes + 1];
for (int s = 1; s <= NSedes; s++)
{
    int mid = s <= 20 ? munPorRegion[2][(s - 1) % 5] : rnd.Next(1, munId + 1); // las primeras sedes son del Pacífico
    sedeMun[s] = mid;
    await Exec("insert into maestros.sede values (@i,@n,@m)", ("i", s), ("n", $"I.E. {Pick(apellidos)} {Pick(["de la Esperanza","San José","Santa Rosa","Nuevo Amanecer","Simón Bolívar","Los Andes"])} #{s}"), ("m", mid));
}

// Entidades (40; las 3 primeras son del Pacífico y las administra Laura; la última está inactiva)
for (int e = 1; e <= 40; e++)
{
    int mid = e <= 3 ? munPorRegion[2][e - 1] : rnd.Next(1, munId + 1);
    string nom = e == 1 ? "Fundación Semillas del Pacífico" : e == 2 ? "Corporación Raíces del Litoral" : e == 3 ? "Asociación Manos de Tumaco"
        : $"{Pick(prefEnt)} {Pick(nomEnt)} {Pick(["Caribe","Llano","Pacífico","Andes","Amazonas","Sur","Norte"])} {e}";
    await Exec("insert into maestros.entidad (public_id,nombre,municipio_id,activa) values (@p,@n,@m,@a)", ("p", Guid.CreateVersion7()), ("n", nom), ("m", mid), ("a", e != 40));
}

// Tutores líderes (200) y docentes (3.000); id 1 = personajes demo
for (int t = 1; t <= 200; t++)
    await Exec("insert into maestros.tutor (public_id,nombres,correo,municipio_id) values (@p,@n,@c,@m)", ("p", Guid.CreateVersion7()),
        ("n", t == 1 ? "Andrés Tutor Líder" : Nombre()), ("c", t == 1 ? "tutor.lider@sinapsis.co" : $"tutor{t}@sinapsis.co"), ("m", t == 1 ? tumaco : rnd.Next(1, munId + 1)));
string[] docDemo = ["Marina Docente", "Luis Ángulo", "Rosa Caicedo", "Jorge Preciado", "Elena Cuero", "Pablo Riascos"];
for (int d = 1; d <= 3000; d++)
    await Exec("insert into maestros.docente (public_id,nombres,correo,sede_id) values (@p,@n,@c,@s)", ("p", Guid.CreateVersion7()),
        ("n", d <= 6 ? docDemo[d - 1] : Nombre()), ("c", d == 1 ? "tutor.docente@sinapsis.co" : $"docente{d}@sinapsis.co"), ("s", d <= 6 ? 1 + (d % 5) : rnd.Next(1, NSedes + 1)));

// Participantes demo
await Exec(@"insert into seguridad.participante (keycloak_sub,correo,nombre,rol,persona_tipo,persona_id) values
  (@a,'admin@sinapsis.co','Admin Funcional','ADMIN_FUNCIONAL','FUNCIONARIO',null),
  (@g,'gestor.demo@sinapsis.co','Laura Gestora','GESTOR_ESTRATEGIAS','FUNCIONARIO',null),
  (@t,'tutor.lider@sinapsis.co','Andrés Tutor Líder','TUTOR_LIDER','TUTOR',1),
  (@d,'tutor.docente@sinapsis.co','Marina Docente','TUTOR_DOCENTE','DOCENTE',1)",
    ("a", SubAdmin), ("g", SubGestor), ("t", SubTutor), ("d", SubDocente));
await Exec("insert into seguridad.gestor_entidad select (select id from seguridad.participante where rol='GESTOR_ESTRATEGIAS'), e from generate_series(1,3) e");

// Estudiantes (COPY binario por lotes)
Console.WriteLine($"Sembrando {n:N0} estudiantes…");
(string tipo, string num, string nom, int sede)[] conocidos = [("TI","1001234567","Camila Rojas Mosquera",1),("TI","1001234568","Santiago Angulo Caicedo",2),("RC","1102345678","Valentina Cuero Preciado",3)];
string[] tipos = ["TI","RC","NUIP"];
string Visible(string full) { var p = full.Split(' '); return $"{p[0]} {p[1][0]}."; }
await using var cn2 = new NpgsqlConnection(conn); await cn2.OpenAsync();
await using (var imp = await cn.BeginBinaryImportAsync("copy maestros.estudiante (id,tipo_documento_cif,numero_documento_cif,nombres_cif,fecha_nacimiento_cif,nombre_visible,sede_id) from stdin (format binary)"))
await using (var impH = await cn2.BeginBinaryImportAsync("copy maestros.estudiante_huella (huella,estudiante_id,public_id) from stdin (format binary)"))
{
    for (int i = 1; i <= n; i++)
    {
        string tipo, num, nom; int sede;
        if (i <= conocidos.Length) (tipo, num, nom, sede) = conocidos[i - 1];
        else { tipo = tipos[i % 3]; num = (1_200_000_000L + i).ToString(); nom = Nombre(); sede = 1 + rnd.Next(NSedes); }
        var nac = new DateTime(2008, 1, 1).AddDays(rnd.Next(0, 365 * 10));
        await imp.StartRowAsync();
        await imp.WriteAsync((long)i, NpgsqlDbType.Bigint);
        await imp.WriteAsync(prot.Cifrar(tipo), NpgsqlDbType.Bytea);
        await imp.WriteAsync(prot.Cifrar(num), NpgsqlDbType.Bytea);
        await imp.WriteAsync(prot.Cifrar(nom), NpgsqlDbType.Bytea);
        await imp.WriteAsync(prot.Cifrar(nac.ToString("yyyy-MM-dd")), NpgsqlDbType.Bytea);
        await imp.WriteAsync(Visible(nom), NpgsqlDbType.Text);
        await imp.WriteAsync(sede, NpgsqlDbType.Integer);
        await impH.StartRowAsync();
        await impH.WriteAsync(prot.Huella(tipo, num), NpgsqlDbType.Bytea);
        await impH.WriteAsync((long)i, NpgsqlDbType.Bigint);
        await impH.WriteAsync(Guid.CreateVersion7(), NpgsqlDbType.Uuid);
        if (i % 100_000 == 0) Console.WriteLine($"  {i:N0}…");
    }
    await imp.CompleteAsync(); await impH.CompleteAsync();
}

// Estrategias (60): 6 primeras en las entidades de Laura; ~mitad bloqueadas con vinculados
Console.WriteLine("Estrategias y asignaciones…");
string[] nomEstr = ["Semillero de Robótica","Ajedrez para Todos","Lectura en Voz Alta","Danza y Tradición","Huertas Escolares","Fútbol de Barrio","Teatro Comunitario","Ciencia Itinerante","Música del Pacífico","Cine Foro Escolar"];
long gestorId = await Scalar("select id from seguridad.participante where rol='GESTOR_ESTRATEGIAS'");
for (int e = 1; e <= 60; e++)
{
    int ent = e <= 6 ? (e - 1) / 2 + 1 : rnd.Next(1, 40);
    string nom = e <= 6 ? (e == 1 ? "Semillero de Robótica Pacífico" : e == 5 ? "Círculos de Lectura Pacífico" : e == 6 ? "Taller de Teatro Pacífico" : $"{nomEstr[e - 1]} {2026}") : $"{Pick(nomEstr)} {Pick(["Norte","Sur","Rural","Urbano","Ribereño","Andino"])} {e}";
    await Exec(@"insert into estrategias.estrategia (public_id,entidad_id,nombre,objetivo,poblacion,fecha_inicio,fecha_fin,creado_por)
      values (@p,@e,@n,@o,@po,'2026-08-01','2027-05-31',@c)", ("p", Guid.CreateVersion7()), ("e", (long)ent), ("n", nom),
      ("o", "Fortalecer habilidades socioemocionales y de aprendizaje de niñas, niños y adolescentes mediante actividades extracurriculares."),
      ("po", "Estudiantes de básica y media, 8 a 17 años"), ("c", gestorId));
}
// Estrategias bloqueadas: 2,4 (de Laura) y 7..30 pares → vinculados reales (el trigger actualiza contador y estado)
foreach (int e in new[] { 2, 4 }.Concat(Enumerable.Range(7, 24).Where(x => x % 2 == 0)))
{
    int k = 20 + rnd.Next(40);
    await Exec(@"insert into estrategias.estrategia_estudiante (estrategia_id,estudiante_id,vinculado_por)
      select @e, s, @g from (select distinct 4 + (random()*(@n-5))::bigint as s from generate_series(1,@k)) q", ("e", (long)e), ("g", gestorId), ("n", (long)n), ("k", k));
}
// Asignaciones TL–TD–estrategia: el TL demo (id 1) tiene docentes 1..3 en estrategia 5 y 4..6 en estrategia 6 (ambas EDITABLES, de Laura)
for (int d = 1; d <= 3000; d++)
{
    long tl, es;
    if (d <= 3) (tl, es) = (1, 5); else if (d <= 6) (tl, es) = (1, 6);
    else { tl = 2 + (d % 199); es = 7 + rnd.Next(54); }
    await Exec("insert into acompanamiento.asignacion (tutor_id,docente_id,estrategia_id) values (@t,@d,@e) on conflict do nothing", ("t", tl), ("d", (long)d), ("e", es));
}
await Exec("analyze");
Console.WriteLine($"Listo en {sw.Elapsed.TotalSeconds:F1} s. Estudiantes: {await Scalar("select count(*) from maestros.estudiante_huella"):N0}");

static void LeerDotEnv()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
    {
        var f = Path.Combine(dir.FullName, ".env");
        if (!File.Exists(f)) continue;
        foreach (var l in File.ReadAllLines(f))
        {
            var t = l.Trim(); if (t.StartsWith('#') || !t.Contains('=')) continue;
            var i = t.IndexOf('='); var k = t[..i]; if (Environment.GetEnvironmentVariable(k) == null) Environment.SetEnvironmentVariable(k, t[(i + 1)..]);
        }
        return;
    }
}

sealed class ConfigurationBuilderLite : Microsoft.Extensions.Configuration.IConfiguration
{
    public string? this[string key] { get => Environment.GetEnvironmentVariable(key); set { } }
    public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];
    public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();
    public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) => throw new NotSupportedException();
}
