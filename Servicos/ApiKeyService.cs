using LicitacoesCampinasMCP.Dominio;
using Microsoft.Playwright;

namespace LicitacoesCampinasMCP.Servicos;

/// <summary>
/// Serviço responsável por capturar e gerenciar a API Key do portal de Campinas.
/// Utiliza o BrowserPoolService para capturar a chave via interceptação de requisições.
/// </summary>
public class ApiKeyService
{
    private readonly BrowserPoolService _browserPool;
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    
    private string? _cachedApiKey;
    private DateTime _apiKeyExpiry = DateTime.MinValue;
    
    private const string SITE_URL = "https://campinas.sp.gov.br/licitacoes/home";
    private const string BASE_URL = "https://contratacoes-api.campinas.sp.gov.br";
    private static readonly TimeSpan API_KEY_VALIDITY = TimeSpan.FromMinutes(30);

    public ApiKeyService(BrowserPoolService browserPool)
    {
        _browserPool = browserPool;
    }

    /// <summary>
    /// Obtém a API Key atual. Se expirada ou inexistente, captura uma nova.
    /// </summary>
    public async Task<string> GetApiKeyAsync(CancellationToken cancellationToken = default)
    {
        // Verifica cache primeiro (sem lock)
        if (!string.IsNullOrEmpty(_cachedApiKey) && DateTime.UtcNow < _apiKeyExpiry)
        {
            return _cachedApiKey;
        }

        await _captureLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check após adquirir o lock
            if (!string.IsNullOrEmpty(_cachedApiKey) && DateTime.UtcNow < _apiKeyExpiry)
            {
                return _cachedApiKey;
            }

            Console.WriteLine("[ApiKeyService] Capturando nova API Key...");
            var newKey = await CaptureApiKeyAsync(cancellationToken);
            
            _cachedApiKey = newKey;
            _apiKeyExpiry = DateTime.UtcNow.Add(API_KEY_VALIDITY);
            
            Console.WriteLine($"[ApiKeyService] API Key capturada com sucesso. Expira em: {_apiKeyExpiry:HH:mm:ss}");
            return _cachedApiKey;
        }
        finally
        {
            _captureLock.Release();
        }
    }

    /// <summary>
    /// Obtém informações completas da API Key.
    /// </summary>
    public async Task<ApiKeyInfo> GetApiKeyInfoAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = await GetApiKeyAsync(cancellationToken);
        return new ApiKeyInfo
        {
            ApiKey = apiKey,
            ExpiresAt = _apiKeyExpiry,
            BaseUrl = BASE_URL
        };
    }

    /// <summary>
    /// Obtém a data de expiração da API Key atual.
    /// </summary>
    public DateTime GetApiKeyExpiry() => _apiKeyExpiry;

    /// <summary>
    /// Invalida a API Key atual, forçando uma nova captura na próxima requisição.
    /// </summary>
    public void InvalidateApiKey()
    {
        Console.WriteLine("[ApiKeyService] Invalidando API Key...");
        _cachedApiKey = null;
        _apiKeyExpiry = DateTime.MinValue;
    }

    /// <summary>
    /// Captura a API Key navegando no site e interceptando requisições.
    /// </summary>
    private async Task<string> CaptureApiKeyAsync(CancellationToken cancellationToken)
    {
        var session = await _browserPool.AcquireSessionAsync(cancellationToken);
        IPage? page = null;
        
        try
        {
            page = await session.NewPageAsync();
            string? capturedKey = null;

            // Intercepta requisições para capturar a API Key
            page.Request += (_, request) =>
            {
                if (request.Url.Contains("contratacoes-api.campinas.sp.gov.br"))
                {
                    var headers = request.Headers;
                    if (headers.TryGetValue("x-api-key", out var key))
                    {
                        capturedKey = key;
                        Console.WriteLine($"[ApiKeyService] API Key interceptada: {key.Substring(0, Math.Min(10, key.Length))}...");
                    }
                }
            };

            // Navega para o site
            await page.GotoAsync(SITE_URL, new PageGotoOptions 
            { 
                WaitUntil = WaitUntilState.NetworkIdle, 
                Timeout = 60000 
            });
            
            await page.WaitForTimeoutAsync(5000);

            // Se não capturou, tenta interagir com a página
            if (string.IsNullOrEmpty(capturedKey))
            {
                try
                {
                    await page.WaitForSelectorAsync("table tbody tr", new PageWaitForSelectorOptions { Timeout = 10000 });
                    var btn = await page.QuerySelectorAsync("table tbody tr button");
                    if (btn != null)
                    {
                        await btn.ClickAsync();
                        await page.WaitForTimeoutAsync(3000);
                    }
                }
                catch (Exception ex) 
                { 
                    Console.WriteLine($"[ApiKeyService] Erro ao interagir com a página: {ex.Message}"); 
                }
            }

            if (string.IsNullOrEmpty(capturedKey))
            {
                throw new Exception("Não foi possível capturar a API Key. Verifique se o site está acessível.");
            }

            return capturedKey;
        }
        finally
        {
            if (page != null)
            {
                await page.CloseAsync();
            }
            await _browserPool.ReleaseSessionAsync(session);
        }
    }
}
