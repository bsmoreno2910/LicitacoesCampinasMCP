using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa os dados completos de uma licitação/compra pública.
/// </summary>
public class LicitacaoData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("pncp_ano_compra")] 
    public int? PncpAnoCompra { get; set; }
    
    [JsonPropertyName("pncp_tipo_instrumento_convocatorio_id")] 
    public int? PncpTipoInstrumentoConvocatorioId { get; set; }
    
    [JsonPropertyName("pncp_modalidade_id")] 
    public int? PncpModalidadeId { get; set; }
    
    [JsonPropertyName("pncp_modo_disputa_id")] 
    public int? PncpModoDisputaId { get; set; }
    
    [JsonPropertyName("pncp_numero_compra")] 
    public string? PncpNumeroCompra { get; set; }
    
    [JsonPropertyName("pncp_numero_processo")] 
    public string? PncpNumeroProcesso { get; set; }
    
    [JsonPropertyName("pncp_objeto_compra")] 
    public string? PncpObjetoCompra { get; set; }
    
    [JsonPropertyName("pncp_informacao_complementar")] 
    public string? PncpInformacaoComplementar { get; set; }
    
    [JsonPropertyName("pncp_srp")] 
    public bool? PncpSrp { get; set; }
    
    [JsonPropertyName("pncp_orcamento_sigiloso")] 
    public bool? PncpOrcamentoSigiloso { get; set; }
    
    [JsonPropertyName("pncp_amparo_legal_id")] 
    public int? PncpAmparoLegalId { get; set; }
    
    [JsonPropertyName("pncp_data_abertura_proposta")] 
    public string? PncpDataAberturaProposta { get; set; }
    
    [JsonPropertyName("pncp_data_encerramento_proposta")] 
    public string? PncpDataEncerramentoProposta { get; set; }
    
    [JsonPropertyName("pncp_codigo_unidade_compradora")] 
    public string? PncpCodigoUnidadeCompradora { get; set; }
    
    [JsonPropertyName("pncp_link_sistema_origem")] 
    public string? PncpLinkSistemaOrigem { get; set; }
    
    [JsonPropertyName("orgao_id")] 
    public int? OrgaoId { get; set; }
    
    [JsonPropertyName("situacao")] 
    public string? Situacao { get; set; }
    
    [JsonPropertyName("motivo_reprova")] 
    public string? MotivoReprova { get; set; }
    
    [JsonPropertyName("retorno_pncp")] 
    public string? RetornoPncp { get; set; }
    
    [JsonPropertyName("pncp_sequencial_compra")] 
    public int? PncpSequencialCompra { get; set; }
    
    [JsonPropertyName("pncp_situacao_compra_id")] 
    public int? PncpSituacaoCompraId { get; set; }
    
    [JsonPropertyName("pncp_justificativa")] 
    public string? PncpJustificativa { get; set; }
    
    [JsonPropertyName("created_at")] 
    public string? CreatedAt { get; set; }
    
    [JsonPropertyName("updated_at")] 
    public string? UpdatedAt { get; set; }
    
    [JsonPropertyName("pncp_justificativa_presencial")] 
    public string? PncpJustificativaPresencial { get; set; }
    
    [JsonPropertyName("numero_controle_pncp")] 
    public string? NumeroControlePncp { get; set; }
    
    [JsonPropertyName("status")] 
    public string? Status { get; set; }
    
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
    
    // Histórico
    [JsonPropertyName("compra_historicos")] 
    public List<CompraHistoricoData>? CompraHistoricos { get; set; }
    
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
    
    [JsonPropertyName("empenhos")] 
    public List<EmpenhoData>? Empenhos { get; set; }
    
    // Metadados
    [JsonPropertyName("data_extracao")] 
    public string? DataExtracao { get; set; }
    
    [JsonPropertyName("fonte")] 
    public string Fonte { get; set; } = "campinas.sp.gov.br";
}
