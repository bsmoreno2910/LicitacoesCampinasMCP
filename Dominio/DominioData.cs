using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa um item de domínio genérico (modalidade, amparo legal, situação, etc.).
/// </summary>
public class DominioData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("titulo")] 
    public string? Titulo { get; set; }
}
