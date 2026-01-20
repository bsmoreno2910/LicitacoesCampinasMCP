using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa as informações da API Key capturada.
/// </summary>
public class ApiKeyInfo
{
    [JsonPropertyName("api_key")] 
    public string ApiKey { get; set; } = string.Empty;
    
    [JsonPropertyName("expires_at")] 
    public DateTime ExpiresAt { get; set; }
    
    [JsonPropertyName("base_url")] 
    public string BaseUrl { get; set; } = "https://contratacoes-api.campinas.sp.gov.br";
}
