using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa a resposta paginada de listagem de licitações.
/// </summary>
public class ListaLicitacoesResponse
{
    [JsonPropertyName("pagina")] 
    public int Pagina { get; set; }
    
    [JsonPropertyName("proxima_pagina")] 
    public int? ProximaPagina { get; set; }
    
    [JsonPropertyName("itens_por_pagina")] 
    public int ItensPorPagina { get; set; }
    
    [JsonPropertyName("total")] 
    public int Total { get; set; }
    
    [JsonPropertyName("licitacoes")] 
    public List<LicitacaoResumo> Licitacoes { get; set; } = new();
}
