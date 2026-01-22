using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa o resultado de um item de licitação (dados do fornecedor vencedor e valores homologados).
/// </summary>
public class ItemResultadoData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("pncp_quantidade_homologada")] 
    public decimal? QuantidadeHomologada { get; set; }
    
    [JsonPropertyName("pncp_valor_unitario_homologado")] 
    public decimal? ValorUnitarioHomologado { get; set; }
    
    [JsonPropertyName("pncp_valor_total_homologado")] 
    public decimal? ValorTotalHomologado { get; set; }
    
    [JsonPropertyName("pncp_percentual_desconto")] 
    public decimal? PercentualDesconto { get; set; }
    
    [JsonPropertyName("pncp_tipo_pessoa_id")] 
    public string? TipoPessoa { get; set; }
    
    [JsonPropertyName("pncp_ni_fornecedor")] 
    public string? CnpjCpfFornecedor { get; set; }
    
    [JsonPropertyName("pncp_nome_razao_social_fornecedor")] 
    public string? NomeFornecedor { get; set; }
    
    [JsonPropertyName("pncp_porte_fornecedor_id")] 
    public int? PorteFornecedorId { get; set; }
    
    [JsonPropertyName("pncp_natureza_juridica_id")] 
    public int? NaturezaJuridicaId { get; set; }
    
    [JsonPropertyName("pncp_codigo_pais")] 
    public string? CodigoPais { get; set; }
    
    [JsonPropertyName("pncp_indicador_subcontratacao")] 
    public bool? IndicadorSubcontratacao { get; set; }
    
    [JsonPropertyName("pncp_ordem_classificacao_srp")] 
    public int? OrdemClassificacaoSrp { get; set; }
    
    [JsonPropertyName("pncp_data_resultado")] 
    public string? DataResultado { get; set; }
    
    [JsonPropertyName("pncp_situacao_compra_item_resultado_id")] 
    public int? SituacaoCompraItemResultadoId { get; set; }
    
    [JsonPropertyName("pncp_justificativa")] 
    public string? Justificativa { get; set; }
    
    [JsonPropertyName("pncp_data_cancelamento")] 
    public string? DataCancelamento { get; set; }
    
    [JsonPropertyName("pncp_motivo_cancelamento")] 
    public string? MotivoCancelamento { get; set; }
    
    [JsonPropertyName("pncp_aplicacao_margem_preferencia")] 
    public bool? AplicacaoMargemPreferencia { get; set; }
    
    [JsonPropertyName("pncp_aplicacao_beneficio_me_epp")] 
    public bool? AplicacaoBeneficioMeEpp { get; set; }
    
    [JsonPropertyName("pncp_aplicacao_criterio_desempate")] 
    public bool? AplicacaoCriterioDesempate { get; set; }
}
