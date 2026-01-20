using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa um resumo de licitação para listagens.
/// </summary>
public class LicitacaoResumo
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("numero_compra")] 
    public string? NumeroCompra { get; set; }
    
    [JsonPropertyName("processo")] 
    public string? Processo { get; set; }
    
    [JsonPropertyName("objeto")] 
    public string? Objeto { get; set; }
    
    [JsonPropertyName("modalidade")] 
    public string? Modalidade { get; set; }
    
    [JsonPropertyName("unidade")] 
    public string? Unidade { get; set; }
    
    [JsonPropertyName("status")] 
    public string? Status { get; set; }
    
    [JsonPropertyName("url")] 
    public string? Url { get; set; }
}
