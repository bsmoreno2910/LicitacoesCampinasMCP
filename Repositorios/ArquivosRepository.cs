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
}
