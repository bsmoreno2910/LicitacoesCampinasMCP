using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa um item de uma licitação.
/// </summary>
public class ItemData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("numero_item")] 
    public int NumeroItem { get; set; }
    
    [JsonPropertyName("codigo_reduzido")] 
    public string? CodigoReduzido { get; set; }
    
    [JsonPropertyName("descricao")] 
    public string? Descricao { get; set; }
    
    [JsonPropertyName("quantidade")] 
    public decimal Quantidade { get; set; }
    
    [JsonPropertyName("unidade_medida")] 
    public string? UnidadeMedida { get; set; }
    
    [JsonPropertyName("valor_unitario_estimado")] 
    public decimal ValorUnitarioEstimado { get; set; }
    
    [JsonPropertyName("valor_total_estimado")] 
    public decimal ValorTotalEstimado { get; set; }
    
    [JsonPropertyName("valor_unitario_homologado")] 
    public decimal? ValorUnitarioHomologado { get; set; }
    
    [JsonPropertyName("valor_total_homologado")] 
    public decimal? ValorTotalHomologado { get; set; }
    
    [JsonPropertyName("situacao_item")] 
    public string? SituacaoItem { get; set; }
    
    [JsonPropertyName("compra_id")] 
    public int? CompraId { get; set; }
    
    [JsonPropertyName("created_at")] 
    public string? CreatedAt { get; set; }
    
    [JsonPropertyName("updated_at")] 
    public string? UpdatedAt { get; set; }
}
