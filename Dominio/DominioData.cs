using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa um item de domínio genérico (modalidade, amparo legal, situação, etc.).
/// </summary>
public class DominioData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("ref_dominio")] 
    public string? RefDominio { get; set; }
    
    [JsonPropertyName("descricao_dominio")] 
    public string? DescricaoDominio { get; set; }
    
    [JsonPropertyName("item_id")] 
    public string? ItemId { get; set; }
    
    [JsonPropertyName("item_titulo")] 
    public string? ItemTitulo { get; set; }
    
    [JsonPropertyName("item_descricao")] 
    public string? ItemDescricao { get; set; }
    
    [JsonPropertyName("observacao")] 
    public string? Observacao { get; set; }
    
    [JsonPropertyName("ativo")] 
    public bool? Ativo { get; set; }
    
    [JsonPropertyName("created_at")] 
    public string? CreatedAt { get; set; }
    
    [JsonPropertyName("updated_at")] 
    public string? UpdatedAt { get; set; }
}
