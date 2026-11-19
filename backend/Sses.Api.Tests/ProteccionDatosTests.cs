using System.Security.Cryptography;
using Sses.Api.Comun;

namespace Sses.Api.Tests;

public class ProteccionDatosTests
{
    private static ProteccionDatos Crear() => new(RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Cifrar_Descifrar_ida_y_vuelta()
    {
        var p = Crear();
        var c = p.Cifrar("Juan Pérez 2012-03-04");
        Assert.Equal("Juan Pérez 2012-03-04", p.Descifrar(c));
    }

    [Fact]
    public void Cifrado_es_no_determinista_y_no_deja_texto_legible()
    {
        var p = Crear();
        var a = p.Cifrar("1001234567"); var b = p.Cifrar("1001234567");
        Assert.NotEqual(a, b);
        Assert.DoesNotContain("1001234567", System.Text.Encoding.UTF8.GetString(a));
    }

    [Fact]
    public void Dato_alterado_falla_la_autenticacion()
    {
        var p = Crear(); var c = p.Cifrar("secreto"); c[14] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => p.Descifrar(c));
    }

    [Fact]
    public void Huella_es_estable_normalizada_y_depende_del_tipo()
    {
        var p = Crear();
        Assert.Equal(p.Huella("TI", "1001234567"), p.Huella(" ti ", "1.001.234.567"));
        Assert.NotEqual(p.Huella("TI", "1001234567"), p.Huella("RC", "1001234567"));
        Assert.Equal(32, p.Huella("TI", "1").Length);
    }

    [Fact]
    public void Huella_depende_de_la_clave()
    {
        Assert.NotEqual(Crear().Huella("TI", "1"), Crear().Huella("TI", "1"));
    }
}
