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
    
    [JsonPropertyName("valor_total")] 
    public decimal ValorTotal { get; set; }
}
