using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LicitacoesCampinasMCP;

class Program
{
    static async Task Main(string[] args)
    {
        // Verifica se deve rodar como API HTTP ou MCP stdio
        var runAsApi = Environment.GetEnvironmentVariable("RUN_AS_API") == "true" || args.Contains("--api");

        if (runAsApi)
        {
            await RunAsHttpApi(args);
        }
        else
        {
            await RunAsMcpServer(args);
        }
    }

    static async Task RunAsMcpServer(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.AddConsole(consoleLogOptions =>
        {
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services.AddSingleton<PlaywrightService>();

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "licitacoes-campinas", Version = "1.0.0" };
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync();
    }

    static async Task RunAsHttpApi(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSingleton<PlaywrightService>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        app.UseSwagger();
        app.UseSwaggerUI();

        // Endpoint de health check
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

        // Endpoint para buscar edital
        app.MapGet("/api/edital/{id}", async (string id, PlaywrightService playwrightService) =>
        {
            try
            {
                var result = await LicitacoesTools.BuscarEdital(playwrightService, id);
                return Results.Content(result, "application/json");
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // Endpoint para listar licitações
        app.MapGet("/api/licitacoes", async (int? pagina, int? itens, PlaywrightService playwrightService) =>
        {
            try
            {
                var result = await LicitacoesTools.BuscarListaLicitacoes(playwrightService, pagina ?? 1, itens ?? 100);
                return Results.Content(result, "application/json");
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // Endpoint para baixar arquivo
        app.MapPost("/api/edital/{id}/arquivo", async (string id, ArquivoRequest request, PlaywrightService playwrightService) =>
        {
            try
            {
                var result = await LicitacoesTools.BaixarArquivoEdital(playwrightService, id, request.NomeArquivo, request.PastaDestino ?? "/tmp/downloads");
                return Results.Content(result, "application/json");
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        Console.WriteLine("API HTTP iniciada em http://0.0.0.0:8080");
        Console.WriteLine("Swagger disponível em http://0.0.0.0:8080/swagger");

        await app.RunAsync();
    }
}

public record ArquivoRequest(string NomeArquivo, string? PastaDestino);

// ============================================================
// SERVIÇO DO PLAYWRIGHT
// ============================================================

public class PlaywrightService : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser != null) return _browser;

        await _semaphore.WaitAsync();
        try
        {
            if (_browser != null) return _browser;

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage" }
            });

            return _browser;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            await _browser.CloseAsync();
        }
        _playwright?.Dispose();
    }
}

// ============================================================
// MODELOS DE DADOS
// ============================================================

public class LicitacaoData
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("titulo")]
    public string? Titulo { get; set; }

    [JsonPropertyName("tipo")]
    public string? Tipo { get; set; }

    [JsonPropertyName("processo")]
    public string? Processo { get; set; }

    [JsonPropertyName("secretaria")]
    public string? Secretaria { get; set; }

    [JsonPropertyName("modalidade")]
    public string? Modalidade { get; set; }

    [JsonPropertyName("instrumento_convocatorio")]
    public string? InstrumentoConvocatorio { get; set; }

    [JsonPropertyName("modo_disputa")]
    public string? ModoDisputa { get; set; }

    [JsonPropertyName("amparo_legal")]
    public string? AmparoLegal { get; set; }

    [JsonPropertyName("link_contratacao")]
    public string? LinkContratacao { get; set; }

    [JsonPropertyName("objeto")]
    public string? Objeto { get; set; }

    [JsonPropertyName("informacao_complementar")]
    public string? InformacaoComplementar { get; set; }

    [JsonPropertyName("id_pncp")]
    public string? IdPncp { get; set; }

    [JsonPropertyName("numero_controle_pncp")]
    public string? NumeroControlePncp { get; set; }

    [JsonPropertyName("situacao_compra")]
    public string? SituacaoCompra { get; set; }

    [JsonPropertyName("status_compra")]
    public string? StatusCompra { get; set; }

    [JsonPropertyName("data_inicio_propostas")]
    public string? DataInicioPropostas { get; set; }

    [JsonPropertyName("data_fim_propostas")]
    public string? DataFimPropostas { get; set; }

    [JsonPropertyName("ultima_alteracao")]
    public string? UltimaAlteracao { get; set; }

    [JsonPropertyName("valor_estimado")]
    public decimal ValorEstimado { get; set; }

    [JsonPropertyName("valor_homologado")]
    public decimal ValorHomologado { get; set; }

    [JsonPropertyName("data_extracao")]
    public string? DataExtracao { get; set; }

    [JsonPropertyName("fonte")]
    public string Fonte { get; set; } = "campinas.sp.gov.br";

    [JsonPropertyName("arquivos")]
    public List<ArquivoData>? Arquivos { get; set; }

    [JsonPropertyName("itens")]
    public List<ItemData>? Itens { get; set; }
}

public class ArquivoData
{
    [JsonPropertyName("nome")]
    public string? Nome { get; set; }

    [JsonPropertyName("tipo")]
    public string? Tipo { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("url_download")]
    public string? UrlDownload { get; set; }
}

public class ItemData
{
    [JsonPropertyName("numero")]
    public string? Numero { get; set; }

    [JsonPropertyName("codigo")]
    public string? Codigo { get; set; }

    [JsonPropertyName("descricao")]
    public string? Descricao { get; set; }

    [JsonPropertyName("quantidade")]
    public string? Quantidade { get; set; }

    [JsonPropertyName("valor_unitario")]
    public decimal ValorUnitario { get; set; }

    [JsonPropertyName("valor_total")]
    public decimal ValorTotal { get; set; }

    [JsonPropertyName("situacao")]
    public string? Situacao { get; set; }
}

public class LicitacaoResumo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("modalidade")]
    public string? Modalidade { get; set; }

    [JsonPropertyName("numero")]
    public string? Numero { get; set; }

    [JsonPropertyName("processo")]
    public string? Processo { get; set; }

    [JsonPropertyName("unidade")]
    public string? Unidade { get; set; }

    [JsonPropertyName("objeto")]
    public string? Objeto { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

// ============================================================
// FERRAMENTAS MCP
// ============================================================

[McpServerToolType]
public static class LicitacoesTools
{
    [McpServerTool, Description("Busca os detalhes completos de um edital/licitação pelo ID. Retorna todas as informações incluindo arquivos e itens.")]
    public static async Task<string> BuscarEdital(
        PlaywrightService playwrightService,
        [Description("ID do edital a ser buscado (ex: 12043)")] string edital_id)
    {
        try
        {
            var browser = await playwrightService.GetBrowserAsync();
            var page = await browser.NewPageAsync();

            try
            {
                var url = $"https://campinas.sp.gov.br/licitacoes/edital/{edital_id}";
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

                try
                {
                    await page.WaitForSelectorAsync("text=Processo:", new PageWaitForSelectorOptions { Timeout = 30000 });
                }
                catch
                {
                    return JsonSerializer.Serialize(new { erro = "Edital não encontrado ou página não carregou", id = edital_id });
                }

                var licitacao = await ExtrairDadosLicitacao(page, edital_id);
                return JsonSerializer.Serialize(licitacao, new JsonSerializerOptions { WriteIndented = true });
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { erro = ex.Message, id = edital_id });
        }
    }

    [McpServerTool, Description("Busca a lista de licitações da página principal com paginação.")]
    public static async Task<string> BuscarListaLicitacoes(
        PlaywrightService playwrightService,
        [Description("Número da página (padrão: 1)")] int pagina = 1,
        [Description("Itens por página: 10, 25, 50 ou 100 (padrão: 100)")] int itens_por_pagina = 100)
    {
        try
        {
            var browser = await playwrightService.GetBrowserAsync();
            var page = await browser.NewPageAsync();

            try
            {
                await page.GotoAsync("https://campinas.sp.gov.br/licitacoes/home",
                    new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

                await page.WaitForSelectorAsync("table", new PageWaitForSelectorOptions { Timeout = 30000 });

                if (itens_por_pagina != 10)
                {
                    try
                    {
                        var selectItens = await page.QuerySelectorAsync("mat-select[aria-label*='Itens']");
                        if (selectItens != null)
                        {
                            await selectItens.ClickAsync();
                            await page.WaitForTimeoutAsync(500);
                            await page.ClickAsync($"text={itens_por_pagina}");
                            await page.WaitForTimeoutAsync(2000);
                        }
                    }
                    catch { }
                }

                for (int pag = 1; pag < pagina; pag++)
                {
                    try
                    {
                        var nextButton = await page.QuerySelectorAsync("button[aria-label*='Próxima']");
                        if (nextButton != null)
                        {
                            await nextButton.ClickAsync();
                            await page.WaitForTimeoutAsync(2000);
                        }
                    }
                    catch { break; }
                }

                var licitacoes = await ExtrairListaLicitacoes(page);
                return JsonSerializer.Serialize(licitacoes, new JsonSerializerOptions { WriteIndented = true });
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { erro = ex.Message });
        }
    }

    [McpServerTool, Description("Baixa um arquivo específico de um edital para uma pasta local.")]
    public static async Task<string> BaixarArquivoEdital(
        PlaywrightService playwrightService,
        [Description("ID do edital")] string edital_id,
        [Description("Nome do arquivo a ser baixado")] string nome_arquivo,
        [Description("Pasta onde salvar o arquivo (padrão: ./downloads)")] string pasta_destino = "./downloads")
    {
        try
        {
            Directory.CreateDirectory(pasta_destino);

            var browser = await playwrightService.GetBrowserAsync();
            var page = await browser.NewPageAsync();

            try
            {
                var url = $"https://campinas.sp.gov.br/licitacoes/edital/{edital_id}";
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

                try
                {
                    await page.ClickAsync("text=Arquivos");
                    await page.WaitForTimeoutAsync(1000);
                }
                catch { }

                var downloadButton = await page.QuerySelectorAsync($"tr:has-text('{nome_arquivo}') button");

                if (downloadButton != null)
                {
                    var download = await page.RunAndWaitForDownloadAsync(async () =>
                    {
                        await downloadButton.ClickAsync();
                    });

                    var filePath = Path.Combine(pasta_destino, download.SuggestedFilename);
                    await download.SaveAsAsync(filePath);

                    return JsonSerializer.Serialize(new { sucesso = true, arquivo = filePath });
                }
                else
                {
                    return JsonSerializer.Serialize(new { erro = $"Arquivo '{nome_arquivo}' não encontrado no edital {edital_id}" });
                }
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { erro = ex.Message });
        }
    }

    // ============================================================
    // MÉTODOS AUXILIARES
    // ============================================================

    private static async Task<LicitacaoData> ExtrairDadosLicitacao(IPage page, string editalId)
    {
        var licitacao = new LicitacaoData
        {
            Id = editalId,
            DataExtracao = DateTime.UtcNow.ToString("o")
        };

        licitacao.Titulo = await ExtrairTexto(page, "h2");
        licitacao.Tipo = await ExtrairCampo(page, "Tipo:");
        licitacao.Processo = await ExtrairCampo(page, "Processo:");
        licitacao.Secretaria = await ExtrairCampo(page, "Secretaria:");
        licitacao.Modalidade = await ExtrairCampo(page, "Modalidade:");
        licitacao.InstrumentoConvocatorio = await ExtrairCampo(page, "Instrumento convocatório:");
        licitacao.ModoDisputa = await ExtrairCampo(page, "Modo de Disputa:");
        licitacao.AmparoLegal = await ExtrairCampo(page, "Amparo legal:");
        licitacao.LinkContratacao = await ExtrairCampo(page, "Link da Contratação:");
        licitacao.Objeto = await ExtrairCampo(page, "Objeto:");
        licitacao.InformacaoComplementar = await ExtrairCampo(page, "Informação complementar:");
        licitacao.IdPncp = await ExtrairCampo(page, "ID PNCP Compra:");
        licitacao.NumeroControlePncp = await ExtrairCampo(page, "Número controle PNCP:");
        licitacao.SituacaoCompra = await ExtrairCampo(page, "Situação compra:");
        licitacao.StatusCompra = await ExtrairCampo(page, "Status da Compra:");
        licitacao.DataInicioPropostas = await ExtrairCampo(page, "Data de início de recebimento de propostas:");
        licitacao.DataFimPropostas = await ExtrairCampo(page, "Data fim de recebimento propostas:");
        licitacao.UltimaAlteracao = await ExtrairCampo(page, "Última alteração:");

        var valorEstimadoStr = await ExtrairValor(page, "Valor total estimado");
        licitacao.ValorEstimado = ConverterValorBrasileiro(valorEstimadoStr);

        var valorHomologadoStr = await ExtrairValor(page, "Valor total homologado");
        licitacao.ValorHomologado = ConverterValorBrasileiro(valorHomologadoStr);

        licitacao.Arquivos = await ExtrairArquivos(page);
        licitacao.Itens = await ExtrairItens(page);

        return licitacao;
    }

    private static async Task<string?> ExtrairTexto(IPage page, string selector)
    {
        try
        {
            var element = await page.QuerySelectorAsync(selector);
            if (element != null)
            {
                return await element.InnerTextAsync();
            }
        }
        catch { }
        return null;
    }

    private static async Task<string?> ExtrairCampo(IPage page, string label)
    {
        try
        {
            var escapedLabel = label.Replace(":", "\\:");
            var script = $@"
                (() => {{
                    const elements = document.body.innerText;
                    const regex = new RegExp('{escapedLabel}\\s*([^\\n]+)', 'i');
                    const match = elements.match(regex);
                    return match ? match[1].trim() : null;
                }})()
            ";
            var result = await page.EvaluateAsync<string?>(script);
            return result;
        }
        catch { }
        return null;
    }

    private static async Task<string?> ExtrairValor(IPage page, string label)
    {
        try
        {
            var script = $@"
                (() => {{
                    const elements = document.body.innerText;
                    const regex = new RegExp('{label}[^R]*R\\$\\s*([\\d.,]+)', 'i');
                    const match = elements.match(regex);
                    return match ? match[1].trim() : null;
                }})()
            ";
            var result = await page.EvaluateAsync<string?>(script);
            return result;
        }
        catch { }
        return null;
    }

    private static decimal ConverterValorBrasileiro(string? valorStr)
    {
        if (string.IsNullOrWhiteSpace(valorStr)) return 0;

        valorStr = valorStr.Replace("R$", "").Trim();
        valorStr = valorStr.Replace(".", "").Replace(",", ".");

        if (decimal.TryParse(valorStr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var valor))
            return valor;

        return 0;
    }

    private static async Task<List<ArquivoData>> ExtrairArquivos(IPage page)
    {
        var arquivos = new List<ArquivoData>();

        try
        {
            var abaArquivos = await page.QuerySelectorAsync("text=Arquivos");
            if (abaArquivos != null)
            {
                await abaArquivos.ClickAsync();
                await page.WaitForTimeoutAsync(1000);
            }

            var script = @"
                (() => {
                    const arquivos = [];
                    const rows = document.querySelectorAll('table tr');
                    rows.forEach((row, index) => {
                        if (index === 0) return;
                        const cells = row.querySelectorAll('td');
                        if (cells.length >= 3) {
                            arquivos.push({
                                nome: cells[0]?.innerText?.trim() || '',
                                tipo: cells[1]?.innerText?.trim() || '',
                                data: cells[2]?.innerText?.trim() || ''
                            });
                        }
                    });
                    return arquivos;
                })()
            ";

            var result = await page.EvaluateAsync<List<ArquivoData>>(script);
            if (result != null)
            {
                arquivos = result;
            }
        }
        catch { }

        return arquivos;
    }

    private static async Task<List<ItemData>> ExtrairItens(IPage page)
    {
        var itens = new List<ItemData>();

        try
        {
            var abaItens = await page.QuerySelectorAsync("text=Itens");
            if (abaItens != null)
            {
                await abaItens.ClickAsync();
                await page.WaitForTimeoutAsync(1000);
            }

            var script = @"
                (() => {
                    const itens = [];
                    const rows = document.querySelectorAll('table tr');
                    rows.forEach((row, index) => {
                        if (index === 0) return;
                        const cells = row.querySelectorAll('td');
                        if (cells.length >= 6) {
                            itens.push({
                                numero: cells[0]?.innerText?.trim() || '',
                                codigo: cells[1]?.innerText?.trim() || '',
                                descricao: cells[2]?.innerText?.trim() || '',
                                quantidade: cells[3]?.innerText?.trim() || '',
                                valor_unitario: 0,
                                valor_total: 0,
                                situacao: cells[6]?.innerText?.trim() || ''
                            });
                        }
                    });
                    return itens;
                })()
            ";

            var result = await page.EvaluateAsync<List<ItemData>>(script);
            if (result != null)
            {
                itens = result;
            }
        }
        catch { }

        return itens;
    }

    private static async Task<List<LicitacaoResumo>> ExtrairListaLicitacoes(IPage page)
    {
        var script = @"
            (() => {
                const licitacoes = [];
                const rows = document.querySelectorAll('table tbody tr');
                rows.forEach(row => {
                    const cells = row.querySelectorAll('td');
                    const link = row.querySelector('a[href*=""edital""]');
                    const href = link?.getAttribute('href') || '';
                    const idMatch = href.match(/edital\/(\d+)/);
                    
                    if (cells.length >= 5) {
                        licitacoes.push({
                            id: idMatch ? idMatch[1] : '',
                            modalidade: cells[0]?.innerText?.trim() || '',
                            numero: cells[1]?.innerText?.trim() || '',
                            processo: cells[2]?.innerText?.trim() || '',
                            unidade: cells[3]?.innerText?.trim() || '',
                            objeto: cells[4]?.innerText?.trim() || '',
                            status: cells[5]?.innerText?.trim() || '',
                            url: href ? 'https://campinas.sp.gov.br' + href : ''
                        });
                    }
                });
                return licitacoes;
            })()
        ";

        var result = await page.EvaluateAsync<List<LicitacaoResumo>>(script);
        return result ?? new List<LicitacaoResumo>();
    }
}
