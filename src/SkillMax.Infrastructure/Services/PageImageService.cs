using PDFtoImage;
using SkiaSharp;
using SkillMax.Application.Abstractions;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Infrastructure.Services;

/// <summary>
/// Renders a PDF page to a PNG (via PDFium/SkiaSharp) and caches it on disk. Falls back to a
/// generated SVG placeholder if a page can't be rendered, so the feed always shows something.
/// DB-free and thread-safe (a gate serializes the non-reentrant native calls) → registered as
/// a singleton, never touching the shared scoped DbContext.
/// </summary>
public sealed class PageImageService : IPageImageService
{
    /// <summary>Reader pages are read at arm's length; they get the full width.</summary>
    private const int PageWidth = 1080;

    /// <summary>A cover is never shown wider than half a phone, and a shelf loads many at once.</summary>
    private const int CoverWidth = 420;

    private readonly SkillMaxStorage _storage;
    private readonly SemaphoreSlim _renderGate = new(1, 1);

    public PageImageService(SkillMaxStorage storage) => _storage = storage;

    public async Task<string> GetPageDataUriAsync(int bookId, string pdfPath, int pageIndex, CancellationToken ct = default)
    {
        var cachePath = Path.Combine(_storage.PageCacheDir, $"{bookId}_{pageIndex}.png");

        if (File.Exists(cachePath))
        {
            var cached = await File.ReadAllBytesAsync(cachePath, ct).ConfigureAwait(false);
            return PngDataUri(cached);
        }

        try
        {
            var png = await RenderPngAsync(pdfPath, pageIndex, PageWidth, ct).ConfigureAwait(false);
            try { await File.WriteAllBytesAsync(cachePath, png, ct).ConfigureAwait(false); }
            catch { /* cache is best-effort */ }
            return PngDataUri(png);
        }
        catch
        {
            return PlaceholderDataUri(pageIndex);
        }
    }

    public async Task<string?> GetCoverDataUriAsync(int bookId, string pdfPath, CancellationToken ct = default)
    {
        // Its own cache entry, still under "{bookId}_*.png" so BookService's cleanup takes it
        // with the book. A cover is never the reader's page 1 — different size, different job.
        var cachePath = Path.Combine(_storage.PageCacheDir, $"{bookId}_cover.png");

        if (File.Exists(cachePath))
        {
            try
            {
                var cached = await File.ReadAllBytesAsync(cachePath, ct).ConfigureAwait(false);
                return PngDataUri(cached);
            }
            catch { /* unreadable: re-render below */ }
        }

        try
        {
            var png = await RenderPngAsync(pdfPath, 0, CoverWidth, ct).ConfigureAwait(false);
            try { await File.WriteAllBytesAsync(cachePath, png, ct).ConfigureAwait(false); }
            catch { /* cache is best-effort */ }
            return PngDataUri(png);
        }
        catch
        {
            // No placeholder: the shelf already has a gradient to fall back on, and a tile of
            // "preview unavailable" reads worse than the colour it replaces.
            return null;
        }
    }

    private async Task<byte[]> RenderPngAsync(string pdfPath, int pageIndex, int width, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(pdfPath, ct).ConfigureAwait(false);

        await _renderGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                using var bitmap = Conversion.ToImage(
                    bytes,
                    page: pageIndex,
                    password: null,
                    options: new RenderOptions(Width: width, WithAspectRatio: true));
                using var data = bitmap.Encode(SKEncodedImageFormat.Png, 90);
                return data.ToArray();
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    private static string PngDataUri(byte[] png) => "data:image/png;base64," + Convert.ToBase64String(png);

    private static string PlaceholderDataUri(int pageIndex)
    {
        var svg =
            $"<svg xmlns='http://www.w3.org/2000/svg' width='900' height='900'>" +
            $"<rect width='100%' height='100%' fill='#f2efe7'/>" +
            $"<text x='50%' y='48%' font-family='Georgia, serif' font-size='46' fill='#8a8172' text-anchor='middle'>Page {pageIndex + 1}</text>" +
            $"<text x='50%' y='56%' font-family='sans-serif' font-size='24' fill='#b3ab99' text-anchor='middle'>preview unavailable</text>" +
            $"</svg>";
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
        return "data:image/svg+xml;base64," + b64;
    }
}
