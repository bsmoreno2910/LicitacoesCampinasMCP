using LicitacoesCampinasMCP.Dominio;
using LicitacoesCampinasMCP.Repositorios;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace LicitacoesCampinasMCP.Servicos;

/// <summary>
/// Tools MCP para acesso às licitações de Campinas.
/// </summary>
[McpServerToolType]
public static class LicitacoesTools
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description("Obtém a API Key para acessar a API de contratações de Campinas.")]
    public static async Task<string> ObterApiKey(ApiKeyService apiKeyService)
    {
        try
        {
            var info = await apiKeyService.GetApiKeyInfoAsync();
            return JsonSerializer.Serialize(info, _jsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { erro = ex.Message }, _jsonOptions);
        }
    }

    [McpServerTool, Description("Busca detalhes completos de um edital pelo ID, incluindo arquivos e itens.")]
    public static async Task<string> BuscarEdital(
        LicitacoesRepository repository, 
        [Description("ID do edital (ex: 12043)")] string edital_id)
    {
        try
        {
            var licitacao = await repository.BuscarPorIdAsync(edital_id);
            
            if (licitacao == null)
                return JsonSerializer.Serialize(new { erro = "Edital não encontrado", id = edital_id }, _jsonOptions);

            return JsonSerializer.Serialize(licitacao, _jsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { erro = ex.Message, id = edital_id }, _jsonOptions);
        }
    }

    [McpServerTool, Description("Lista licitações com paginação.")]
    public static async Task<string> BuscarListaLicitacoes(
        LicitacoesRepository repository, 
        [Description("Página (padrão: 1)")] int pagina = 1, 
        [Description("Itens por página (padrão: 100)")] int itens_por_pagina = 100)
    {
        try
        {
            var response = await repository.ListarAsync(pagina, itens_por_pagina);
            return JsonSerializer.Serialize(response, _jsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { erro = ex.Message }, _jsonOptions);
        }
    }

    [McpServerTool, Description("Busca licitações por número de processo ou termo no objeto.")]
    public static async Task<string> BuscarPorFiltro(
        LicitacoesRepository repository, 
        [Description("Número do processo (ex: PMC.2025.00124491-59)")] string? processo = null, 
        [Description("Termo para buscar no objeto")] string? objeto = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(processo) && string.IsNullOrWhiteSpace(objeto))
            {
                return JsonSerializer.Serialize(new { erro = "Informe pelo menos um filtro: processo ou objeto" }, _jsonOptions);
            }

            var response = await repository.BuscarPorFiltroAsync(processo, objeto);
            return JsonSerializer.Serialize(response, _jsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { erro = ex.Message }, _jsonOptions);
        }
    }
}
