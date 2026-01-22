using LicitacoesCampinasMCP.Dominio;
using Microsoft.Playwright;
using System.Text.Json;

namespace LicitacoesCampinasMCP.Servicos;

/// <summary>
/// Serviço responsável por fazer requisições à API de contratações de Campinas.
/// Cada requisição usa uma sessão dedicada do pool, que é devolvida após o uso.
/// </summary>
public class CampinasApiService : IAsyncDisposable
{
    private readonly BrowserPoolService _browserPool;
    private readonly ApiKeyService _apiKeyService;
    
    private const string BASE_URL = "https://contratacoes-api.campinas.sp.gov.br";
    private const int REQUEST_TIMEOUT_MS = 300000; // 5 minutos

    public CampinasApiService(BrowserPoolService browserPool, ApiKeyService apiKeyService)
    {
        _browserPool = browserPool;
        _apiKeyService = apiKeyService;
    }

    /// <summary>
    /// Cria um contexto de API com a chave atual usando uma sessão do pool.
    /// </summary>
    private async Task<IAPIRequestContext> CreateApiContextAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync(cancellationToken);
        
        Console.WriteLine("[CampinasApi] Criando contexto de API via Browserless...");
        return await session.Playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
        {
            BaseURL = BASE_URL,
            Timeout = REQUEST_TIMEOUT_MS,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["x-api-key"] = apiKey,
                ["Accept"] = "application/json",
                ["Content-Type"] = "application/json",
                ["Origin"] = "https://campinas.sp.gov.br"
            }
        });
    }

    /// <summary>
    /// Faz uma requisição GET à API de Campinas via Browserless.
    /// Usa uma sessão dedicada do pool para cada requisição.
    /// </summary>
    public async Task<JsonDocument?> GetAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        BrowserSession? session = null;
        IAPIRequestContext? apiContext = null;
        
        try
        {
            // Adquire uma sessão do pool
            Console.WriteLine("[CampinasApi] Adquirindo sessão do BrowserPool...");
            session = await _browserPool.AcquireSessionAsync(cancellationToken);
            
            // Cria contexto de API para esta requisição
            apiContext = await CreateApiContextAsync(session, cancellationToken);
            
            Console.WriteLine($"[CampinasApi] GET {endpoint} (via Browserless)");
            var response = await apiContext.GetAsync(endpoint);
            
            // Se a chave expirou, tenta renovar
            if (!response.Ok && (response.Status == 401 || response.Status == 403))
            {
                Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
                _apiKeyService.InvalidateApiKey();
                
                // Recria o contexto com a nova chave
                await apiContext.DisposeAsync();
                apiContext = await CreateApiContextAsync(session, cancellationToken);
                response = await apiContext.GetAsync(endpoint);
            }

            if (!response.Ok)
            {
                var errorBody = await response.TextAsync();
                throw new Exception($"Erro na API: {response.Status} - {errorBody}");
            }

            var json = await response.TextAsync();
            return JsonDocument.Parse(json);
        }
        finally
        {
            // Sempre libera os recursos
            if (apiContext != null)
            {
                await apiContext.DisposeAsync();
            }
            if (session != null)
            {
                await _browserPool.ReleaseSessionAsync(session);
            }
        }
    }

    /// <summary>
    /// Faz download de um arquivo da API de Campinas via Browserless.
    /// Usa uma sessão dedicada do pool para cada download.
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoAsync(string compraId, string arquivoId, CancellationToken cancellationToken = default)
    {
        BrowserSession? session = null;
        IAPIRequestContext? apiContext = null;
        
        try
        {
            // Adquire uma sessão do pool
            Console.WriteLine("[CampinasApi] Adquirindo sessão do BrowserPool para download...");
            session = await _browserPool.AcquireSessionAsync(cancellationToken);
            
            // Cria contexto de API para este download
            apiContext = await CreateApiContextAsync(session, cancellationToken);
            
            var endpoint = $"/compras/{compraId}/arquivos/{arquivoId}/blob";
            Console.WriteLine($"[CampinasApi] DOWNLOAD {endpoint} (via Browserless)");
            
            var response = await apiContext.GetAsync(endpoint);
            
            // Se a chave expirou, tenta renovar
            if (!response.Ok && (response.Status == 401 || response.Status == 403))
            {
                Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
                _apiKeyService.InvalidateApiKey();
                
                await apiContext.DisposeAsync();
                apiContext = await CreateApiContextAsync(session, cancellationToken);
                response = await apiContext.GetAsync(endpoint);
            }

            if (!response.Ok)
            {
                var errorBody = await response.TextAsync();
                throw new Exception($"Erro ao baixar arquivo: {response.Status} - {errorBody}");
            }

            var bytes = await response.BodyAsync();
            var contentType = response.Headers.TryGetValue("content-type", out var ct) ? ct : "application/octet-stream";
            
            // Tenta extrair nome do arquivo do header content-disposition
            var fileName = $"arquivo_{arquivoId}";
            if (response.Headers.TryGetValue("content-disposition", out var cd))
            {
                var match = System.Text.RegularExpressions.Regex.Match(cd, @"filename\*?=['""]?(?:UTF-8'')?([^'"";\n]+)");
                if (match.Success) fileName = Uri.UnescapeDataString(match.Groups[1].Value);
            }
            
            // Adiciona extensão baseada no content-type se não tiver
            if (!fileName.Contains("."))
            {
                fileName += contentType switch
                {
                    "application/pdf" => ".pdf",
                    "image/jpeg" => ".jpg",
                    "image/png" => ".png",
                    "application/zip" => ".zip",
                    "application/msword" => ".doc",
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                    "application/vnd.ms-excel" => ".xls",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
                    _ => ""
                };
            }

            return new ArquivoDownload
            {
                Bytes = bytes,
                ContentType = contentType,
                FileName = fileName
            };
        }
        finally
        {
            if (apiContext != null)
            {
                await apiContext.DisposeAsync();
            }
            if (session != null)
            {
                await _browserPool.ReleaseSessionAsync(session);
            }
        }
    }

    /// <summary>
    /// Faz download de um arquivo de empenho da API de Campinas via Browserless.
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoEmpenhoAsync(string compraId, string empenhoId, string arquivoId, CancellationToken cancellationToken = default)
    {
        BrowserSession? session = null;
        IAPIRequestContext? apiContext = null;
        
        try
        {
            session = await _browserPool.AcquireSessionAsync(cancellationToken);
            apiContext = await CreateApiContextAsync(session, cancellationToken);
            
            var endpoint = $"/compras/{compraId}/empenhos/{empenhoId}/arquivos/{arquivoId}/blob";
            Console.WriteLine($"[CampinasApi] DOWNLOAD EMPENHO {endpoint} (via Browserless)");
            
            var response = await apiContext.GetAsync(endpoint);
            
            if (!response.Ok && (response.Status == 401 || response.Status == 403))
            {
                Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
                _apiKeyService.InvalidateApiKey();
                
                await apiContext.DisposeAsync();
                apiContext = await CreateApiContextAsync(session, cancellationToken);
                response = await apiContext.GetAsync(endpoint);
            }

            if (!response.Ok)
            {
                var errorBody = await response.TextAsync();
                throw new Exception($"Erro ao baixar arquivo de empenho: {response.Status} - {errorBody}");
            }

            var bytes = await response.BodyAsync();
            var contentType = response.Headers.TryGetValue("content-type", out var ct) ? ct : "application/octet-stream";
            
            var fileName = $"empenho_{empenhoId}_arquivo_{arquivoId}";
            if (response.Headers.TryGetValue("content-disposition", out var cd))
            {
                var match = System.Text.RegularExpressions.Regex.Match(cd, @"filename\*?=['""]?(?:UTF-8'')?([^'"";\n]+)");
                if (match.Success) fileName = Uri.UnescapeDataString(match.Groups[1].Value);
            }
            
            if (!fileName.Contains("."))
            {
                fileName += contentType switch
                {
                    "application/pdf" => ".pdf",
                    "image/jpeg" => ".jpg",
                    "image/png" => ".png",
                    "application/zip" => ".zip",
                    _ => ""
                };
            }

            return new ArquivoDownload
            {
                Bytes = bytes,
                ContentType = contentType,
                FileName = fileName
            };
        }
        finally
        {
            if (apiContext != null)
            {
                await apiContext.DisposeAsync();
            }
            if (session != null)
            {
                await _browserPool.ReleaseSessionAsync(session);
            }
        }
    }

    /// <summary>
    /// Faz download direto do PDF de um empenho da API de Campinas via Browserless.
    /// O endpoint /compras/{compraId}/empenhos/{empenhoId} retorna diretamente o arquivo PDF.
    /// </summary>
    public async Task<ArquivoDownload> DownloadEmpenhoAsync(string compraId, string empenhoId, CancellationToken cancellationToken = default)
    {
        BrowserSession? session = null;
        IAPIRequestContext? apiContext = null;
        
        try
        {
            session = await _browserPool.AcquireSessionAsync(cancellationToken);
            apiContext = await CreateApiContextAsync(session, cancellationToken);
            
            var endpoint = $"/compras/{compraId}/empenhos/{empenhoId}";
            Console.WriteLine($"[CampinasApi] DOWNLOAD EMPENHO DIRETO {endpoint} (via Browserless)");
            
            var response = await apiContext.GetAsync(endpoint);
            
            if (!response.Ok && (response.Status == 401 || response.Status == 403))
            {
                Console.WriteLine("[CampinasApi] API Key expirada, renovando...");
                _apiKeyService.InvalidateApiKey();
                
                await apiContext.DisposeAsync();
                apiContext = await CreateApiContextAsync(session, cancellationToken);
                response = await apiContext.GetAsync(endpoint);
            }

            if (!response.Ok)
            {
                var errorBody = await response.TextAsync();
                throw new Exception($"Erro ao baixar empenho: {response.Status} - {errorBody}");
            }

            var bytes = await response.BodyAsync();
            var contentType = response.Headers.TryGetValue("content-type", out var ct) ? ct : "application/pdf";
            
            var fileName = $"empenho_{empenhoId}.pdf";
            if (response.Headers.TryGetValue("content-disposition", out var cd))
            {
                var match = System.Text.RegularExpressions.Regex.Match(cd, @"filename\*?=['""]?(?:UTF-8'')?([^'"";\n]+)");
                if (match.Success) fileName = Uri.UnescapeDataString(match.Groups[1].Value);
            }
            
            if (!fileName.Contains("."))
            {
                fileName += ".pdf";
            }

            return new ArquivoDownload
            {
                Bytes = bytes,
                ContentType = contentType,
                FileName = fileName
            };
        }
        finally
        {
            if (apiContext != null)
            {
                await apiContext.DisposeAsync();
            }
            if (session != null)
            {
                await _browserPool.ReleaseSessionAsync(session);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        // Não há mais recursos compartilhados para liberar
        // Cada requisição gerencia sua própria sessão
        return ValueTask.CompletedTask;
    }
}
