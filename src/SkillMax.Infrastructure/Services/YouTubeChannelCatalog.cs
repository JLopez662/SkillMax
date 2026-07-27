using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SkillMax.Application.Models;
using SkillMax.Domain.Entities;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Infrastructure.Services;

/// <summary>
/// Everything this app knows how to ask YouTube, for any channel — no API key, no quota, no account.
///
/// Looking a channel up scrapes its page for the UC… id, name and picture. Listing its shorts reads
/// the public Atom feed at /feeds/videos.xml, which gives the latest ~15 uploads but doesn't say
/// which are shorts — so each newly seen id is probed once: /shorts/{id} answers in place for a
/// short and redirects to /watch for anything else. When that window holds no shorts at all the
/// channel's Shorts tab is read instead, which is the only way to reach a channel whose shorts are
/// older than its last fifteen uploads.
///
/// Listings cache to disk per channel, so scrolling costs nothing, a cold start costs one request
/// per followed channel, and the feed still fills when the phone is offline. Nothing here touches
/// the database, so it's a singleton shared by the scoped services above it.
/// </summary>
public sealed partial class YouTubeChannelCatalog
{
    private static readonly HttpClient Http = CreateClient(followRedirects: true);
    private static readonly HttpClient Probe = CreateClient(followRedirects: false);

    private readonly ShortsOptions _options;
    private readonly string _cacheDir;

    /// <summary>One gate per channel: two posts asking for the same listing shouldn't both fetch it.</summary>
    private readonly Dictionary<string, SemaphoreSlim> _gates = new();
    private readonly Dictionary<string, ChannelCache> _memory = new();

    public YouTubeChannelCatalog(SkillMaxStorage storage, ShortsOptions options)
    {
        _options = options;
        _cacheDir = storage.ShortsCacheDir;
    }

    // ---------------------------------------------------------------- looking a channel up

    /// <summary>
    /// Resolve a handle or channel id to the real thing. Null means YouTube has no such channel;
    /// an <see cref="HttpRequestException"/> means we couldn't ask — the two are different answers
    /// to the user and mustn't be collapsed.
    /// </summary>
    public async Task<ResolvedChannel?> ResolveAsync(ChannelQuery query, CancellationToken ct = default)
    {
        // A video stands in for its channel: find out who posted it, then carry on as normal.
        if (query.VideoId is { } videoId)
        {
            var owner = await OwnerOfAsync(videoId, ct).ConfigureAwait(false);
            return owner is null ? null : await ResolveAsync(owner, ct).ConfigureAwait(false);
        }

        var url = query.Handle is { } handle
            ? $"https://www.youtube.com/@{handle}"
            : $"https://www.youtube.com/channel/{query.ChannelId}";

        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        // Order matters, and not for tidiness. A channel page mentions every channel it links to —
        // @mkbhd's names seven, @veritasium's thirteen — so "the first UC… on the page" picks a
        // neighbour, not the channel you asked for. og:url, canonical and the RSS link all state
        // the page's own channel; externalId (channelMetadataRenderer) is the last resort.
        var channelId = query.ChannelId
                        ?? Match(html, OgUrlChannelRegex())
                        ?? Match(html, CanonicalChannelRegex())
                        ?? Match(html, FeedLinkChannelRegex())
                        ?? Match(html, ExternalIdRegex());
        if (channelId is null) return null;

        // The canonical handle, not whatever casing was typed. Falls back to what we were given.
        var canonicalHandle = Match(html, HandleRegex())?.TrimStart('@') ?? query.Handle ?? "";
        var title = Unescape(Match(html, OgTitleRegex())) ?? canonicalHandle;
        var avatar = Unescape(Match(html, OgImageRegex()));

        return new ResolvedChannel(channelId, canonicalHandle, title, avatar);
    }

    /// <summary>Who posted this video. Null if it's gone, private, or never existed.</summary>
    private static async Task<ChannelQuery?> OwnerOfAsync(string videoId, CancellationToken ct)
    {
        using var response = await Http
            .GetAsync($"https://www.youtube.com/watch?v={videoId}", ct).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        // The microformat's externalChannelId is the uploader; the plain "channelId" keys further
        // down the page belong to recommendations.
        var owner = Match(html, ExternalChannelIdRegex()) ?? Match(html, VideoDetailsChannelRegex());
        return owner is null ? null : ChannelQuery.FromChannelId(owner);
    }

    // ---------------------------------------------------------------- listing a channel's shorts

    /// <summary>
    /// This channel's shorts, from cache when it's fresh. Empty when the channel can't be reached
    /// and nothing is cached — a channel that's down shouldn't empty the whole feed.
    /// </summary>
    public async Task<IReadOnlyList<CachedVideo>> GetShortsAsync(string channelId, CancellationToken ct = default)
    {
        var cache = await EnsureAsync(channelId, _options.CacheFor,
            (previous, token) => FetchChannelAsync(channelId, previous, token), ct).ConfigureAwait(false);
        if (cache is null) return Array.Empty<CachedVideo>();

        // Only a probed yes counts. An unknown is left out rather than guessed into the feed as a
        // 9:16 card that turns out to be a landscape video; the next refresh asks again.
        return _options.ShortsOnly
            ? cache.Videos.Where(v => v.IsShort == true).ToList()
            : cache.Videos;
    }

    // ---------------------------------------------------------------- shorts matching a search

    /// <summary>
    /// What the Shorts tab of YouTube's own results page holds for this search. Unlike a channel
    /// listing these span creators, so each result carries the name of whoever posted it.
    /// Same caching contract: fresh from disk, and a stale listing beats an empty feed.
    /// </summary>
    public async Task<IReadOnlyList<CachedVideo>> SearchShortsAsync(string query, CancellationToken ct = default)
    {
        var cache = await EnsureAsync(SearchKey(query), _options.SearchCacheFor,
            (previous, token) => FetchSearchAsync(query, previous, token), ct).ConfigureAwait(false);

        // No IsShort filter here: these came off the Shorts filter, so they're shorts by construction.
        return cache?.Videos ?? (IReadOnlyList<CachedVideo>)Array.Empty<CachedVideo>();
    }

    // ---------------------------------------------------------------- caching, shared by both

    private async Task<ChannelCache?> EnsureAsync(
        string key,
        TimeSpan ttl,
        Func<ChannelCache?, CancellationToken, Task<ChannelCache>> fetch,
        CancellationToken ct)
    {
        if (Fresh(key, ttl, out var hit)) return hit;

        var gate = GateFor(key);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Fresh(key, ttl, out hit)) return hit;

            var known = hit ?? await ReadCacheAsync(key, ct).ConfigureAwait(false);
            if (known is not null) lock (_memory) _memory[key] = known;
            if (IsFresh(known, ttl)) return known;

            try
            {
                var fetched = await fetch(known, ct).ConfigureAwait(false);
                await WriteCacheAsync(key, fetched, ct).ConfigureAwait(false);
                lock (_memory) _memory[key] = fetched;
                return fetched;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                             or System.Xml.XmlException or IOException)
            {
                // Offline, rate-limited, or YouTube changed shape. A stale listing beats no feed.
                return known;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private bool Fresh(string key, TimeSpan ttl, out ChannelCache? cache)
    {
        lock (_memory) _memory.TryGetValue(key, out cache);
        return IsFresh(cache, ttl);
    }

    private static bool IsFresh(ChannelCache? cache, TimeSpan ttl) =>
        cache is not null && DateTimeOffset.UtcNow - cache.FetchedAt < ttl;

    private SemaphoreSlim GateFor(string key)
    {
        lock (_gates)
        {
            if (!_gates.TryGetValue(key, out var gate))
                _gates[key] = gate = new SemaphoreSlim(1, 1);
            return gate;
        }
    }

    private async Task<ChannelCache> FetchChannelAsync(string channelId, ChannelCache? previous, CancellationToken ct)
    {
        var entries = await FetchUploadsAsync(channelId, ct).ConfigureAwait(false);

        // A video's shorts-or-not verdict never changes, so it's only paid for once per video —
        // but only a real answer is worth carrying forward. Unknowns get asked again.
        var seen = (previous?.Videos ?? new List<CachedVideo>())
            .Where(v => v.IsShort is not null)
            .ToDictionary(v => v.Id, v => v.IsShort);
        var videos = new List<CachedVideo>(entries.Count);

        if (_options.ShortsOnly)
        {
            using var probes = new SemaphoreSlim(4, 4);
            var verdicts = await Task.WhenAll(entries.Select(async entry =>
            {
                if (seen.TryGetValue(entry.Id, out var known)) return entry with { IsShort = known };
                await probes.WaitAsync(ct).ConfigureAwait(false);
                try { return entry with { IsShort = await IsShortAsync(entry.Id, ct).ConfigureAwait(false) }; }
                finally { probes.Release(); }
            })).ConfigureAwait(false);

            videos.AddRange(verdicts);
        }
        else
        {
            // Filter off: don't invent verdicts. Anything already known stays known.
            videos.AddRange(entries.Select(e =>
                seen.TryGetValue(e.Id, out var known) ? e with { IsShort = known } : e));
        }

        // The uploads feed is a rolling window of the latest ~15 uploads of any kind, so a channel
        // that still has shorts but hasn't posted one lately has them sitting outside the window
        // where the feed can never reach them — and the filter above then empties the channel. The
        // Shorts tab lists them directly. Only worth asking when the window came up dry: a channel
        // posting shorts regularly has them in the feed already, and shouldn't pay for a request.
        if (_options.ShortsOnly && !videos.Any(v => v.IsShort == true))
        {
            // Kept separate from the fetch above: the feed answered, and losing that to a failure
            // on this second, optional request would fall the whole channel back to stale cache.
            try
            {
                var tab = await FetchShortsTabAsync(channelId, ct).ConfigureAwait(false);
                var have = videos.Select(v => v.Id).ToHashSet();
                videos.AddRange(tab.Where(v => have.Add(v.Id)));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Nothing gained, nothing lost — the feed's verdicts still stand, and the next
                // refresh asks again.
            }
        }

        return new ChannelCache { FetchedAt = DateTimeOffset.UtcNow, Videos = videos };
    }

    private static async Task<List<CachedVideo>> FetchUploadsAsync(string channelId, CancellationToken ct)
    {
        var xml = await Http
            .GetStringAsync($"https://www.youtube.com/feeds/videos.xml?channel_id={channelId}", ct)
            .ConfigureAwait(false);

        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace yt = "http://www.youtube.com/xml/schemas/2015";
        XNamespace media = "http://search.yahoo.com/mrss/";

        var root = XDocument.Parse(xml).Root ?? throw new HttpRequestException("Empty channel feed.");

        var entries = new List<CachedVideo>();
        foreach (var entry in root.Elements(atom + "entry"))
        {
            var id = entry.Element(yt + "videoId")?.Value;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var group = entry.Element(media + "group");
            entries.Add(new CachedVideo
            {
                Id = id,
                Title = group?.Element(media + "title")?.Value ?? entry.Element(atom + "title")?.Value ?? "",
                Published = DateTimeOffset.TryParse(entry.Element(atom + "published")?.Value, out var p) ? p : null,
            });
        }

        return entries;
    }

    /// <summary>
    /// The channel's Shorts tab, read the same way as a search results page — it's the same lockup
    /// markup. Everything on it is a short by construction, so none of these need probing. The tab
    /// doesn't state publish dates, so these arrive without one.
    /// </summary>
    private static async Task<List<CachedVideo>> FetchShortsTabAsync(string channelId, CancellationToken ct)
    {
        // By id rather than handle: the id is what a followed channel is stored as, and it's stable
        // across a handle change.
        var html = await Http
            .GetStringAsync($"https://www.youtube.com/channel/{channelId}/shorts", ct)
            .ConfigureAwait(false);

        return ShortsFrom(html);
    }

    /// <summary>Every shorts lockup on a page, deduped, in the order the page lists them.</summary>
    private static List<CachedVideo> ShortsFrom(string html)
    {
        var found = new List<CachedVideo>();
        var seenIds = new HashSet<string>();

        foreach (Match match in ShortsLockupRegex().Matches(html))
        {
            var id = match.Groups[1].Value;
            if (!seenIds.Add(id)) continue;

            found.Add(new CachedVideo
            {
                Id = id,
                // The page states the title only inside the accessibility label, tailed by the
                // view count: "The 12-Week Year is a GAME CHANGER!, 164 thousand views - play Short".
                Title = TitleFrom(Unescape(match.Groups[2].Value)),
                IsShort = true,
            });
        }

        return found;
    }

    private async Task<ChannelCache> FetchSearchAsync(string query, ChannelCache? previous, CancellationToken ct)
    {
        // sp=EgIYAQ== is the results page's own "Shorts" type filter. Everything it returns is a
        // short already, which is why nothing here needs the per-video probe a channel needs.
        var html = await Http.GetStringAsync(
            $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}&sp=EgIYAQ%3D%3D", ct)
            .ConfigureAwait(false);

        var found = ShortsFrom(html);

        // Who posted each one isn't on the results page at all. oEmbed answers it in one small
        // request per video — paid once, then carried across refreshes with the listing.
        var known = (previous?.Videos ?? new List<CachedVideo>())
            .Where(v => v.ChannelTitle is not null)
            .ToDictionary(v => v.Id);

        using var lookups = new SemaphoreSlim(4, 4);
        var withCreators = await Task.WhenAll(found.Select(async video =>
        {
            if (known.TryGetValue(video.Id, out var before))
                return video with { ChannelTitle = before.ChannelTitle, ChannelHandle = before.ChannelHandle };

            await lookups.WaitAsync(ct).ConfigureAwait(false);
            try { return await WithCreatorAsync(video, ct).ConfigureAwait(false); }
            finally { lookups.Release(); }
        })).ConfigureAwait(false);

        return new ChannelCache { FetchedAt = DateTimeOffset.UtcNow, Videos = withCreators.ToList() };
    }

    /// <summary>Fills in the creator (and a better title) from YouTube's oEmbed endpoint.</summary>
    private static async Task<CachedVideo> WithCreatorAsync(CachedVideo video, CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync(
                $"https://www.youtube.com/oembed?url=https%3A//www.youtube.com/watch%3Fv%3D{video.Id}&format=json", ct)
                .ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var author = root.TryGetProperty("author_name", out var a) ? a.GetString() : null;
            var authorUrl = root.TryGetProperty("author_url", out var u) ? u.GetString() : null;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;

            return video with
            {
                Title = string.IsNullOrWhiteSpace(video.Title) ? title ?? "" : video.Title,
                ChannelTitle = author,
                ChannelHandle = HandleFromUrl(authorUrl),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Creator unknown — the card falls back to attributing it to the search itself.
            return video;
        }
    }

    /// <summary>
    /// A short answers on /shorts/{id}; a regular upload is bounced to /watch. One request per
    /// video, once ever — the answer is cached with the listing.
    /// </summary>
    private static async Task<bool?> IsShortAsync(string id, CancellationToken ct)
    {
        try
        {
            // GET, not HEAD: YouTube doesn't answer HEAD here. ResponseHeadersRead means the body
            // is never pulled down — the status line is the whole answer.
            using var response = await Probe
                .GetAsync($"https://www.youtube.com/shorts/{id}", HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            var code = (int)response.StatusCode;
            if (code is >= 300 and < 400) return false;
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Unknown, not "yes". One flaky request shouldn't promote a landscape video into a
            // shorts card for the next twelve hours; the following refresh asks again.
            return null;
        }
    }

    // ---------------------------------------------------------------- disk cache

    private string CachePath(string key) => Path.Combine(_cacheDir, $"{key}.json");

    /// <summary>A filename for a search. Queries hold spaces, slashes and emoji; hashes don't.</summary>
    private static string SearchKey(string query)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(SearchTerm.Normalize(query)));
        return "q-" + Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private async Task<ChannelCache?> ReadCacheAsync(string channelId, CancellationToken ct)
    {
        try
        {
            var path = CachePath(channelId);
            if (!File.Exists(path)) return null;
            await using var stream = File.OpenRead(path);
            return await JsonSerializer
                .DeserializeAsync(stream, ShortsJsonContext.Default.ChannelCache, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private async Task WriteCacheAsync(string channelId, ChannelCache cache, CancellationToken ct)
    {
        try
        {
            await using var stream = File.Create(CachePath(channelId));
            await JsonSerializer
                .SerializeAsync(stream, cache, ShortsJsonContext.Default.ChannelCache, ct)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            // A cache we couldn't write just means the next cold start re-fetches.
        }
    }

    /// <summary>Called when a channel is unfollowed, so its listing doesn't sit on disk forever.</summary>
    public void ForgetChannel(string channelId) => Forget(channelId);

    /// <summary>The same, for a search that's been removed.</summary>
    public void ForgetSearch(string query) => Forget(SearchKey(query));

    private void Forget(string key)
    {
        lock (_memory) _memory.Remove(key);
        try { File.Delete(CachePath(key)); } catch (IOException) { /* it'll be overwritten */ }
    }

    // ---------------------------------------------------------------- plumbing

    private static HttpClient CreateClient(bool followRedirects)
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = followRedirects })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        // Without a browser user-agent YouTube serves a stripped page with no channel id in it,
        // and without the consent cookie a European IP gets the consent wall instead of the channel.
        http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "CONSENT=YES+cb");

        return http;
    }

    private static string? Match(string html, Regex regex)
    {
        var match = regex.Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Channel names arrive JSON- and HTML-escaped: "You Are Loved \u0026 Co".</summary>
    private static string? Unescape(string? value)
    {
        if (value is null) return null;
        var decoded = System.Net.WebUtility.HtmlDecode(value);

        // A lone backslash in a channel name makes Regex.Unescape throw, and the name is worth
        // more than the unicode-escape handling.
        try { return Regex.Unescape(decoded); }
        catch (ArgumentException) { return decoded; }
    }

    // ---- a channel page's own id, most trustworthy first ----

    [GeneratedRegex("<meta\\s+property=\"og:url\"\\s+content=\"[^\"]*?/channel/(UC[\\w-]{22})\"")]
    private static partial Regex OgUrlChannelRegex();

    [GeneratedRegex("<link\\s+rel=\"canonical\"\\s+href=\"[^\"]*?/channel/(UC[\\w-]{22})\"")]
    private static partial Regex CanonicalChannelRegex();

    [GeneratedRegex("feeds/videos\\.xml\\?channel_id=(UC[\\w-]{22})")]
    private static partial Regex FeedLinkChannelRegex();

    [GeneratedRegex("\"externalId\"\\s*:\\s*\"(UC[\\w-]{22})\"")]
    private static partial Regex ExternalIdRegex();

    // ---- shorts lockups: a search results page and a channel's Shorts tab, same markup ----

    [GeneratedRegex("\"shortsLockupViewModel\"\\s*:\\s*\\{\"entityId\"\\s*:\\s*\"shorts-shelf-item-([\\w-]{11})\"\\s*,\\s*\"accessibilityText\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex ShortsLockupRegex();

    /// <summary>Trims "…, 164 thousand views - play Short" back to the title.</summary>
    [GeneratedRegex(",\\s*[\\d.,]+\\s*(?:thousand|million|billion)?\\s*views?$", RegexOptions.IgnoreCase)]
    private static partial Regex ViewCountTailRegex();

    [GeneratedRegex("/@([A-Za-z0-9_.-]{3,30})\\s*$")]
    private static partial Regex AuthorHandleRegex();

    private static string TitleFrom(string? accessibilityText)
    {
        var text = (accessibilityText ?? "").Trim();
        const string playSuffix = "- play Short";
        if (text.EndsWith(playSuffix, StringComparison.OrdinalIgnoreCase))
            text = text[..^playSuffix.Length].TrimEnd();

        return ViewCountTailRegex().Replace(text, "").Trim();
    }

    private static string? HandleFromUrl(string? authorUrl)
    {
        if (string.IsNullOrWhiteSpace(authorUrl)) return null;
        var match = AuthorHandleRegex().Match(authorUrl);
        return match.Success ? match.Groups[1].Value : null;
    }

    // ---- a watch page's uploader ----

    [GeneratedRegex("\"externalChannelId\"\\s*:\\s*\"(UC[\\w-]{22})\"")]
    private static partial Regex ExternalChannelIdRegex();

    [GeneratedRegex("\"videoDetails\"[\\s\\S]{0,1500}?\"channelId\"\\s*:\\s*\"(UC[\\w-]{22})\"")]
    private static partial Regex VideoDetailsChannelRegex();

    [GeneratedRegex("\"(?:canonicalBaseUrl|vanityChannelUrl)\"\\s*:\\s*\"[^\"]*?/(@[A-Za-z0-9_.-]{3,30})\"")]
    private static partial Regex HandleRegex();

    [GeneratedRegex("<meta\\s+property=\"og:title\"\\s+content=\"([^\"]*)\"")]
    private static partial Regex OgTitleRegex();

    [GeneratedRegex("<meta\\s+property=\"og:image\"\\s+content=\"([^\"]*)\"")]
    private static partial Regex OgImageRegex();
}

/// <param name="AvatarUrl">Null when the channel page didn't advertise a picture.</param>
public sealed record ResolvedChannel(string ChannelId, string Handle, string Title, string? AvatarUrl);

// ---------------------------------------------------------------------- cache shape

internal sealed record ChannelCache
{
    public DateTimeOffset FetchedAt { get; init; }
    public List<CachedVideo> Videos { get; init; } = new();
}

public sealed record CachedVideo
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public DateTimeOffset? Published { get; init; }

    /// <summary>
    /// Whether /shorts/{id} claimed this one — probed once, then carried forward. Null means
    /// nobody has asked yet, which is different from "no": it's what a listing fetched with the
    /// filter off leaves behind, and what a failed probe leaves behind, and both should be asked
    /// again rather than assumed either way.
    /// </summary>
    public bool? IsShort { get; init; }

    /// <summary>Who posted it. Only carried for search results, where each hit is a different
    /// creator; a channel listing gets its name from the followed channel instead.</summary>
    public string? ChannelTitle { get; init; }

    public string? ChannelHandle { get; init; }
}

/// <summary>Source-generated so the cache still round-trips in a trimmed release build.</summary>
[JsonSerializable(typeof(ChannelCache))]
internal sealed partial class ShortsJsonContext : JsonSerializerContext;
