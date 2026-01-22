using LicitacoesCampinasMCP.Dominio;
using LicitacoesCampinasMCP.Servicos;

namespace LicitacoesCampinasMCP.Repositorios;

/// <summary>
/// Repositório responsável por operações com arquivos de licitações.
/// </summary>
public class ArquivosRepository
{
    private readonly CampinasApiService _apiService;

    public ArquivosRepository(CampinasApiService apiService)
    {
        _apiService = apiService;
    }

    /// <summary>
    /// Faz download de um arquivo de licitação.
    /// </summary>
    public async Task<ArquivoDownload> DownloadAsync(string compraId, string arquivoId, CancellationToken cancellationToken = default)
    {
        return await _apiService.DownloadArquivoAsync(compraId, arquivoId, cancellationToken);
    }

    /// <summary>
    /// Faz download de um arquivo de empenho.
    /// </summary>
    public async Task<ArquivoDownload> DownloadArquivoEmpenhoAsync(string compraId, string empenhoId, string arquivoId, CancellationToken cancellationToken = default)
    {
        return await _apiService.DownloadArquivoEmpenhoAsync(compraId, empenhoId, arquivoId, cancellationToken);
    }

    /// <summary>
    /// Faz download direto do PDF de um empenho.
    /// </summary>
    public async Task<ArquivoDownload> DownloadEmpenhoAsync(string compraId, string empenhoId, CancellationToken cancellationToken = default)
    {
        return await _apiService.DownloadEmpenhoAsync(compraId, empenhoId, cancellationToken);
    }
}
