using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa os dados de um empenho de uma licitação.
/// </summary>
public class EmpenhoData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("ano")] 
    public int? Ano { get; set; }
    
    [JsonPropertyName("data")] 
    public string? Data { get; set; }
    
    [JsonPropertyName("numero_empenho")] 
    public string? NumeroEmpenho { get; set; }
    
    [JsonPropertyName("cpf_cnpj_fornecedor")] 
    public string? CpfCnpjFornecedor { get; set; }
    
    [JsonPropertyName("fornecedor")] 
    public string? Fornecedor { get; set; }
    
    [JsonPropertyName("valor_global")] 
    public decimal? ValorGlobal { get; set; }
    
    [JsonPropertyName("objeto")] 
    public string? Objeto { get; set; }
    
    [JsonPropertyName("compra_id")] 
    public int? CompraId { get; set; }
    
    [JsonPropertyName("created_at")] 
    public string? CreatedAt { get; set; }
    
    [JsonPropertyName("updated_at")] 
    public string? UpdatedAt { get; set; }
    
    // Arquivos do empenho
    [JsonPropertyName("arquivos")] 
    public List<EmpenhoArquivoData> Arquivos { get; set; } = new();
}
