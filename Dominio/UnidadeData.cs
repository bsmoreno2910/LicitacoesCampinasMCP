using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa uma unidade administrativa (secretaria, departamento, etc.).
/// </summary>
public class UnidadeData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("codigo")] 
    public string? Codigo { get; set; }
    
    [JsonPropertyName("nome")] 
    public string? Nome { get; set; }
}
