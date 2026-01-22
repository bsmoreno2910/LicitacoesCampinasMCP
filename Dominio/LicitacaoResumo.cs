using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa um resumo de licitação para listagens e buscas.
/// </summary>
public class LicitacaoResumo
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("pncp_ano_compra")] 
    public int? PncpAnoCompra { get; set; }
    
    [JsonPropertyName("pncp_numero_compra")] 
    public string? PncpNumeroCompra { get; set; }
    
    [JsonPropertyName("pncp_numero_processo")] 
    public string? PncpNumeroProcesso { get; set; }
    
    [JsonPropertyName("pncp_objeto_compra")] 
    public string? PncpObjetoCompra { get; set; }
    
    [JsonPropertyName("pncp_srp")] 
    public bool? PncpSrp { get; set; }
    
    [JsonPropertyName("pncp_data_abertura_proposta")] 
    public string? PncpDataAberturaProposta { get; set; }
    
    [JsonPropertyName("pncp_data_encerramento_proposta")] 
    public string? PncpDataEncerramentoProposta { get; set; }
    
    [JsonPropertyName("pncp_codigo_unidade_compradora")] 
    public string? PncpCodigoUnidadeCompradora { get; set; }
    
    [JsonPropertyName("orgao_id")] 
    public int? OrgaoId { get; set; }
    
    [JsonPropertyName("situacao")] 
    public string? Situacao { get; set; }
    
    [JsonPropertyName("status")] 
    public string? Status { get; set; }
    
    [JsonPropertyName("created_at")] 
    public string? CreatedAt { get; set; }
    
    [JsonPropertyName("updated_at")] 
    public string? UpdatedAt { get; set; }
    
    [JsonPropertyName("numero_controle_pncp")] 
    public string? NumeroControlePncp { get; set; }
    
    // Relacionamentos simplificados
    [JsonPropertyName("unidade")] 
    public UnidadeData? Unidade { get; set; }
    
    [JsonPropertyName("modalidade")] 
    public DominioData? Modalidade { get; set; }
    
    [JsonPropertyName("situacao_compra")] 
    public DominioData? SituacaoCompra { get; set; }
    
    [JsonPropertyName("amparo_legal")] 
    public DominioData? AmparoLegal { get; set; }
    
    [JsonPropertyName("instrumento_convocatorio")] 
    public DominioData? InstrumentoConvocatorio { get; set; }
    
    [JsonPropertyName("modo_disputa")] 
    public DominioData? ModoDisputa { get; set; }
    
    [JsonPropertyName("url")] 
    public string? Url { get; set; }
}
