using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa a resposta de busca de licitações por filtro.
/// Retorna dados completos de cada licitação (como a busca por ID).
/// </summary>
public class BuscaLicitacoesResponse
{
    [JsonPropertyName("filtros")] 
    public FiltrosBusca Filtros { get; set; } = new();
    
    [JsonPropertyName("total")] 
    public int Total { get; set; }
    
    [JsonPropertyName("licitacoes")] 
    public List<LicitacaoData> Licitacoes { get; set; } = new();
}

/// <summary>
/// Representa os filtros aplicados na busca.
/// </summary>
public class FiltrosBusca
{
    [JsonPropertyName("processo")] 
    public string? Processo { get; set; }
    
    [JsonPropertyName("objeto")] 
    public string? Objeto { get; set; }
}
