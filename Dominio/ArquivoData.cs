using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa um arquivo anexo de uma licitação.
/// </summary>
public class ArquivoData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("tipo_documento")] 
    public int TipoDocumento { get; set; }
    
    [JsonPropertyName("titulo")] 
    public string? Titulo { get; set; }
    
    [JsonPropertyName("link_pncp")] 
    public string? LinkPncp { get; set; }
    
    [JsonPropertyName("created_at")] 
    public string? CreatedAt { get; set; }
}
