using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa um arquivo anexo de uma licitação ou empenho.
/// </summary>
public class ArquivoData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("tipo_documento")] 
    public int TipoDocumento { get; set; }
    
    [JsonPropertyName("titulo")] 
    public string? Titulo { get; set; }
    
    [JsonPropertyName("descricao")] 
    public string? Descricao { get; set; }
    
    [JsonPropertyName("nome")] 
    public string? Nome { get; set; }
    
    [JsonPropertyName("tipo")] 
    public string? Tipo { get; set; }
    
    [JsonPropertyName("tamanho")] 
    public long? Tamanho { get; set; }
    
    [JsonPropertyName("download_url")] 
    public string? DownloadUrl { get; set; }
    
    [JsonPropertyName("link_pncp")] 
    public string? LinkPncp { get; set; }
    
    [JsonPropertyName("created_at")] 
    public string? CreatedAt { get; set; }
    
    [JsonPropertyName("updated_at")] 
    public string? UpdatedAt { get; set; }
    
    // Campos para identificar a origem do arquivo
    [JsonPropertyName("origem")] 
    public string Origem { get; set; } = "licitacao"; // "licitacao" ou "empenho"
    
    [JsonPropertyName("empenho_id")] 
    public int? EmpenhoId { get; set; }
    
    [JsonPropertyName("numero_empenho")] 
    public string? NumeroEmpenho { get; set; }
}
