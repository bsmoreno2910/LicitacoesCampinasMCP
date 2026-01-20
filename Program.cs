using LicitacoesCampinasMCP.Dominio;
using LicitacoesCampinasMCP.Repositorios;
using LicitacoesCampinasMCP.Servicos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace LicitacoesCampinasMCP;

class Program
{
    static async Task Main(string[] args)
    {
        var runAsApi = Environment.GetEnvironmentVariable("RUN_AS_API") == "true" || args.Contains("--api");
        
        if (runAsApi) 
            await RunAsHttpApi(args);
        else 
            await RunAsMcpServer(args);
    }

    /// <summary>
    /// Executa como servidor MCP (Model Context Protocol).
    /// </summary>
    static async Task RunAsMcpServer(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        
        // Registra os serviços
        ConfigureServices(builder.Services);
        
        builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "licitacoes-campinas", Version = "2.0.0" })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();
        
        await builder.Build().RunAsync();
    }

    /// <summary>
    /// Executa como API HTTP REST.
    /// </summary>
    static async Task RunAsHttpApi(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Registra os serviços
        ConfigureServices(builder.Services);
        
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        
        var app = builder.Build();
        
        app.UseSwagger();
        app.UseSwaggerUI();

        // Configura os endpoints
        ConfigureEndpoints(app);

        Console.WriteLine("===========================================");
        Console.WriteLine("  Licitações Campinas API v2.0");
        Console.WriteLine("  Pool de Browsers: 20 conexões simultâneas");
        Console.WriteLine("  Servidor: http://0.0.0.0:8080");
        Console.WriteLine("===========================================");
        
        await app.RunAsync();
    }

    /// <summary>
    /// Configura os serviços de injeção de dependência.
    /// </summary>
    static void ConfigureServices(IServiceCollection services)
    {
        // Pool de browsers (20 conexões simultâneas)
        services.AddSingleton<BrowserPoolService>(sp => new BrowserPoolService(
            maxConnections: 20,
            browserlessUrl: "wss://browserless.vtc.dev.br/?token=78e69a4812450ad4e2e657ee2cdf90b5"
        ));
        
        // Serviços
        services.AddSingleton<ApiKeyService>();
        services.AddSingleton<CampinasApiService>();
        
        // Repositórios
        services.AddSingleton<LicitacoesRepository>();
        services.AddSingleton<ArquivosRepository>();
    }

    /// <summary>
    /// Configura os endpoints da API HTTP.
    /// </summary>
    static void ConfigureEndpoints(WebApplication app)
    {
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        // Health check
        app.MapGet("/health", (BrowserPoolService browserPool) => 
        {
            var stats = browserPool.GetStats();
            return Results.Ok(new 
            { 
                status = "healthy", 
                timestamp = DateTime.UtcNow,
                pool = stats
            });
        });

        // Estatísticas do pool
        app.MapGet("/api/pool/stats", (BrowserPoolService browserPool) => 
        {
            return Results.Ok(browserPool.GetStats());
        });

        // Obter API Key
        app.MapGet("/api/apikey", async (ApiKeyService apiKeyService) => 
        {
            try 
            { 
                var info = await apiKeyService.GetApiKeyInfoAsync();
                return Results.Ok(info); 
            }
            catch (Exception ex) 
            { 
                return Results.Problem(ex.Message); 
            }
        });

        // Buscar edital por ID
        app.MapGet("/api/edital/{id}", async (string id, LicitacoesRepository repository) => 
        {
            try 
            { 
                var licitacao = await repository.BuscarPorIdAsync(id);
                
                if (licitacao == null)
                    return Results.NotFound(new { erro = "Edital não encontrado", id });
                
                return Results.Ok(licitacao);
            }
            catch (Exception ex) 
            { 
                return Results.Problem(ex.Message); 
            }
        });

        // Listar licitações com paginação
        app.MapGet("/api/licitacoes", async (int? pagina, int? itens, LicitacoesRepository repository) => 
        {
            try 
            { 
                var response = await repository.ListarAsync(pagina ?? 1, itens ?? 100);
                return Results.Ok(response);
            }
            catch (Exception ex) 
            { 
                return Results.Problem(ex.Message); 
            }
        });

        // Buscar por filtro
        app.MapGet("/api/buscar", async (string? processo, string? objeto, LicitacoesRepository repository) => 
        {
            try 
            { 
                if (string.IsNullOrWhiteSpace(processo) && string.IsNullOrWhiteSpace(objeto))
                {
                    return Results.BadRequest(new { erro = "Informe pelo menos um filtro: processo ou objeto" });
                }
                
                var response = await repository.BuscarPorFiltroAsync(processo, objeto);
                return Results.Ok(response);
            }
            catch (Exception ex) 
            { 
                return Results.Problem(ex.Message); 
            }
        });

        // Download de arquivo
        app.MapGet("/api/compra/{compraId}/arquivo/{arquivoId}/download", async (string compraId, string arquivoId, ArquivosRepository repository) => 
        {
            try 
            { 
                var arquivo = await repository.DownloadAsync(compraId, arquivoId);
                return Results.File(arquivo.Bytes, arquivo.ContentType, arquivo.FileName);
            }
            catch (Exception ex) 
            { 
                return Results.Problem(ex.Message); 
            }
        });

        // Obter URL de download de arquivo
        app.MapGet("/api/compra/{compraId}/arquivo/{arquivoId}/url", async (string compraId, string arquivoId, ApiKeyService apiKeyService) => 
        {
            try 
            { 
                var apiKey = await apiKeyService.GetApiKeyAsync();
                return Results.Ok(new 
                { 
                    download_url = $"https://contratacoes-api.campinas.sp.gov.br/compras/{compraId}/arquivos/{arquivoId}/blob",
                    api_key = apiKey,
                    headers = new { x_api_key = apiKey }
                });
            }
            catch (Exception ex) 
            { 
                return Results.Problem(ex.Message); 
            }
        });
    }
}
