public interface ISearchService
{
    Task EnsureIndexAsync();
    Task EmbedDocumentsAsync();
    Task UploadChunksAsync();
}
