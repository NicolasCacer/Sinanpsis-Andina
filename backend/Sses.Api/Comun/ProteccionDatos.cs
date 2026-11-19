using System.Security.Cryptography;
using System.Text;

namespace Sses.Api.Comun;

/// <summary>RQ_026: cifrado por columna (AES-256-GCM) y huella HMAC-SHA-256 para búsqueda.</summary>
public interface IProteccionDatos
{
    byte[] Cifrar(string texto);
    string Descifrar(byte[] cifrado);
    byte[] Huella(string tipoDocumento, string numeroDocumento);
}

public sealed class ProteccionDatos : IProteccionDatos
{
    private const int NonceLen = 12, TagLen = 16;
    private readonly byte[] _aes, _hmac;

    public ProteccionDatos(byte[] claveAes, byte[] claveHmac)
    {
        if (claveAes.Length != 32) throw new ArgumentException("SSES__CLAVE_AES debe ser de 32 bytes (base64).");
        if (claveHmac.Length < 32) throw new ArgumentException("SSES__CLAVE_HMAC debe ser de al menos 32 bytes (base64).");
        _aes = claveAes; _hmac = claveHmac;
    }

    public static ProteccionDatos DesdeConfiguracion(IConfiguration cfg) =>
        new(Convert.FromBase64String(cfg["SSES__CLAVE_AES"] ?? throw new InvalidOperationException("Falta SSES__CLAVE_AES")),
            Convert.FromBase64String(cfg["SSES__CLAVE_HMAC"] ?? throw new InvalidOperationException("Falta SSES__CLAVE_HMAC")));

    /// <summary>Formato: nonce(12) || ciphertext || tag(16). Nonce aleatorio por valor.</summary>
    public byte[] Cifrar(string texto)
    {
        var pt = Encoding.UTF8.GetBytes(texto);
        var salida = new byte[NonceLen + pt.Length + TagLen];
        var nonce = salida.AsSpan(0, NonceLen);
        RandomNumberGenerator.Fill(nonce);
        using var gcm = new AesGcm(_aes, TagLen);
        gcm.Encrypt(nonce, pt, salida.AsSpan(NonceLen, pt.Length), salida.AsSpan(NonceLen + pt.Length, TagLen));
        return salida;
    }

    public string Descifrar(byte[] cifrado)
    {
        if (cifrado.Length < NonceLen + TagLen) throw new CryptographicException("Dato cifrado inválido.");
        var ctLen = cifrado.Length - NonceLen - TagLen;
        var pt = new byte[ctLen];
        using var gcm = new AesGcm(_aes, TagLen);
        gcm.Decrypt(cifrado.AsSpan(0, NonceLen), cifrado.AsSpan(NonceLen, ctLen), cifrado.AsSpan(NonceLen + ctLen, TagLen), pt);
        return Encoding.UTF8.GetString(pt);
    }

    /// <summary>huella = HMAC-SHA-256(clave, upper(tipo) + ':' + numero normalizado)</summary>
    public byte[] Huella(string tipoDocumento, string numeroDocumento)
    {
        var tipo = tipoDocumento.Trim().ToUpperInvariant();
        var num = new string(numeroDocumento.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return HMACSHA256.HashData(_hmac, Encoding.UTF8.GetBytes(tipo + ":" + num));
    }
}
