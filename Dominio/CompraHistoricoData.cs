using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP.Dominio;

/// <summary>
/// Representa um registro de histórico de uma compra/licitação.
/// </summary>
public class CompraHistoricoData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("descricao")] 
    public string? Descricao { get; set; }
    
    [JsonPropertyName("created_at")] 
    public string? CreatedAt { get; set; }
    
    [JsonPropertyName("updated_at")] 
    public string? UpdatedAt { get; set; }
    
    [JsonPropertyName("compra_id")] 
    public int? CompraId { get; set; }
    
    [JsonPropertyName("usuario")] 
    public UsuarioData? Usuario { get; set; }
}

/// <summary>
/// Representa um usuário do sistema.
/// </summary>
public class UsuarioData
{
    [JsonPropertyName("id")] 
    public int Id { get; set; }
    
    [JsonPropertyName("nome")] 
    public string? Nome { get; set; }
    
    [JsonPropertyName("email")] 
    public string? Email { get; set; }
}
