using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa os dados completos de uma licitação/compra pública.
/// </summary>
public class LicitacaoData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("numero_compra")] 
    public string? NumeroCompra { get; set; }
    
    [JsonPropertyName("processo")] 
    public string? Processo { get; set; }
    
    [JsonPropertyName("objeto")] 
    public string? Objeto { get; set; }
    
    [JsonPropertyName("informacao_complementar")] 
    public string? InformacaoComplementar { get; set; }
    
    [JsonPropertyName("data_abertura_proposta")] 
    public string? DataAberturaProposta { get; set; }
    
    [JsonPropertyName("data_encerramento_proposta")] 
    public string? DataEncerramentoProposta { get; set; }
    
    [JsonPropertyName("link_sistema_origem")] 
    public string? LinkSistemaOrigem { get; set; }
    
    [JsonPropertyName("sequencial_compra")] 
    public int? SequencialCompra { get; set; }
    
    [JsonPropertyName("numero_controle_pncp")] 
    public string? NumeroControlePncp { get; set; }
    
    [JsonPropertyName("status")] 
    public string? Status { get; set; }
    
    [JsonPropertyName("updated_at")] 
    public string? UpdatedAt { get; set; }
    
    // Relacionamentos
    [JsonPropertyName("unidade")] 
    public UnidadeData? Unidade { get; set; }
    
    [JsonPropertyName("modalidade")] 
    public DominioData? Modalidade { get; set; }
    
    [JsonPropertyName("amparo_legal")] 
    public DominioData? AmparoLegal { get; set; }
    
    [JsonPropertyName("instrumento_convocatorio")] 
    public DominioData? InstrumentoConvocatorio { get; set; }
    
    [JsonPropertyName("modo_disputa")] 
    public DominioData? ModoDisputa { get; set; }
    
    [JsonPropertyName("situacao_compra")] 
    public DominioData? SituacaoCompra { get; set; }
    
    // Valores calculados
    [JsonPropertyName("valor_total_estimado")] 
    public decimal ValorTotalEstimado { get; set; }
    
    [JsonPropertyName("valor_total_homologado")] 
    public decimal ValorTotalHomologado { get; set; }
    
    // Coleções
    [JsonPropertyName("arquivos")] 
    public List<ArquivoData>? Arquivos { get; set; }
    
    [JsonPropertyName("itens")] 
    public List<ItemData>? Itens { get; set; }
    
    // Metadados
    [JsonPropertyName("data_extracao")] 
    public string? DataExtracao { get; set; }
    
    [JsonPropertyName("fonte")] 
    public string Fonte { get; set; } = "campinas.sp.gov.br";
}
