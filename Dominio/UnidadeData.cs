using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa uma unidade administrativa (secretaria, departamento, etc.).
/// </summary>
public class UnidadeData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("pncp_codigo_IBGE")] 
    public string? PncpCodigoIBGE { get; set; }
    
    [JsonPropertyName("pncp_codigo_unidade")] 
    public string? PncpCodigoUnidade { get; set; }
    
    [JsonPropertyName("pncp_nome_unidade")] 
    public string? PncpNomeUnidade { get; set; }
    
    [JsonPropertyName("orgao_id")] 
    public int? OrgaoId { get; set; }
    
    [JsonPropertyName("created_at")] 
    public string? CreatedAt { get; set; }
    
    [JsonPropertyName("updated_at")] 
    public string? UpdatedAt { get; set; }
}
