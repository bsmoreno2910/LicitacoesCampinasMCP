using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa um arquivo anexo de um empenho.
/// </summary>
public class EmpenhoArquivoData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("nome")] 
    public string? Nome { get; set; }
    
    [JsonPropertyName("descricao")] 
    public string? Descricao { get; set; }
    
    [JsonPropertyName("tipo")] 
    public string? Tipo { get; set; }
    
    [JsonPropertyName("tamanho")] 
    public long? Tamanho { get; set; }
    
    [JsonPropertyName("empenho_id")] 
    public int? EmpenhoId { get; set; }
    
    [JsonPropertyName("created_at")] 
    public string? CreatedAt { get; set; }
    
    [JsonPropertyName("updated_at")] 
    public string? UpdatedAt { get; set; }
    
    [JsonPropertyName("download_url")] 
    public string? DownloadUrl { get; set; }
}
