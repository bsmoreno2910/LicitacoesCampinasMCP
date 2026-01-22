using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa os dados de um empenho de uma licitação.
/// </summary>
public class EmpenhoData
{
    [JsonPropertyName("id_empenho")] 
    public int IdEmpenho { get; set; }
    
    [JsonPropertyName("ano_compra")] 
    public int? AnoCompra { get; set; }
    
    [JsonPropertyName("numero_processo")] 
    public string? NumeroProcesso { get; set; }
    
    [JsonPropertyName("numero_empenho")] 
    public string? NumeroEmpenho { get; set; }
    
    [JsonPropertyName("tipo_empenho")] 
    public string? TipoEmpenho { get; set; }
    
    [JsonPropertyName("modalidade")] 
    public string? Modalidade { get; set; }
    
    [JsonPropertyName("ano_contrato")] 
    public int? AnoContrato { get; set; }
    
    [JsonPropertyName("categoria_processo_id")] 
    public int? CategoriaProcessoId { get; set; }
    
    [JsonPropertyName("codigo_unidade")] 
    public int? CodigoUnidade { get; set; }
    
    [JsonPropertyName("tipo_fornecedor")] 
    public string? TipoFornecedor { get; set; }
    
    [JsonPropertyName("cgc_fornecedor")] 
    public string? CgcFornecedor { get; set; }
    
    [JsonPropertyName("nome_fornecedor")] 
    public string? NomeFornecedor { get; set; }
    
    [JsonPropertyName("cod_gestora")] 
    public string? CodGestora { get; set; }
    
    [JsonPropertyName("nome_gestora")] 
    public string? NomeGestora { get; set; }
    
    [JsonPropertyName("cod_uo")] 
    public string? CodUo { get; set; }
    
    [JsonPropertyName("nome_uo")] 
    public string? NomeUo { get; set; }
    
    [JsonPropertyName("cod_programa")] 
    public string? CodPrograma { get; set; }
    
    [JsonPropertyName("descr_programa")] 
    public string? DescrPrograma { get; set; }
    
    [JsonPropertyName("cod_despesa")] 
    public string? CodDespesa { get; set; }
    
    [JsonPropertyName("descr_despesa")] 
    public string? DescrDespesa { get; set; }
    
    [JsonPropertyName("cod_fonte")] 
    public string? CodFonte { get; set; }
    
    [JsonPropertyName("descr_fonte")] 
    public string? DescrFonte { get; set; }
    
    [JsonPropertyName("valor_empenhado")] 
    public decimal? ValorEmpenhado { get; set; }
    
    [JsonPropertyName("valor_reforco")] 
    public decimal? ValorReforco { get; set; }
    
    [JsonPropertyName("valor_anulacao")] 
    public decimal? ValorAnulacao { get; set; }
    
    [JsonPropertyName("valor_total")] 
    public decimal? ValorTotal { get; set; }
    
    [JsonPropertyName("data_assinatura")] 
    public string? DataAssinatura { get; set; }
    
    [JsonPropertyName("objeto")] 
    public string? Objeto { get; set; }
    
    // Itens do empenho
    [JsonPropertyName("itens")] 
    public List<EmpenhoItemData> Itens { get; set; } = new();
    
    // Arquivos do empenho (para download)
    [JsonPropertyName("arquivos")] 
    public List<EmpenhoArquivoData> Arquivos { get; set; } = new();
}

/// <summary>
/// Representa um item de um empenho.
/// </summary>
public class EmpenhoItemData
{
    [JsonPropertyName("numero_item")] 
    public int NumeroItem { get; set; }
    
    [JsonPropertyName("cod_reduzido")] 
    public string? CodReduzido { get; set; }
    
    [JsonPropertyName("descricao_item")] 
    public string? DescricaoItem { get; set; }
    
    [JsonPropertyName("unidade")] 
    public string? Unidade { get; set; }
    
    [JsonPropertyName("quantidade")] 
    public decimal? Quantidade { get; set; }
    
    [JsonPropertyName("valor_unitario")] 
    public decimal? ValorUnitario { get; set; }
    
    [JsonPropertyName("valor_total")] 
    public decimal? ValorTotal { get; set; }
}
