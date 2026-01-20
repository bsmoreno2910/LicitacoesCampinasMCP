namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa os dados de um arquivo baixado.
/// </summary>
public class ArquivoDownload
{
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/octet-stream";
    public string FileName { get; set; } = "arquivo";
}
