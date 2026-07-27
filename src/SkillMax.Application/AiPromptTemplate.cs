using System.Text.RegularExpressions;
using SkillMax.Application.Models;

namespace SkillMax.Application;

/// <summary>
/// The prompt a page carries when it's handed to a chat assistant, and the rules for filling it in.
/// Pure and deterministic, so the settings screen can preview exactly what a post will send.
///
/// Two pieces of syntax:
///   <c>{book}</c> — a token, replaced with the value from the page.
///   <c>[[ by {author}]]</c> — an optional run, dropped whole when a token inside it has no value.
///
/// The second is what lets one template serve a book with no author and a book with one, without
/// leaving "I'm reading X by ." behind. A token the renderer doesn't know is left as written, so a
/// stray brace in the reader's own prose survives rather than silently disappearing.
/// </summary>
public static class AiPromptTemplate
{
    public const string Default = """
        I'm reading "{book}"[[ by {author}]].[[ This page is from the chapter "{chapter}".]] Below is page {page}[[ of {pages}]], exactly as it appears.

        Read it and hold it — don't summarise it back to me. In my next message I'll ask a question or make a claim. Answer it against this passage: quote the lines that bear on it, put them in plain language, and tell me where I've misread it. If I go past what the page covers, say so, and mark clearly what's yours rather than the book's.

        Keep it a conversation: short turns, one idea at a time, and ask me something back when it helps.

        --- begin page {page} ---
        {text}
        --- end page {page} ---

        Reply with just "Ready." and nothing else.
        """;

    /// <summary>What the settings screen lists under the editor.</summary>
    public static IReadOnlyList<PromptToken> Tokens { get; } = new[]
    {
        new PromptToken("book", "the book's title"),
        new PromptToken("author", "the author, when the book names one"),
        new PromptToken("chapter", "the chapter the page falls in, when the book has an outline"),
        new PromptToken("page", "the page number"),
        new PromptToken("pages", "how many pages the book has"),
        new PromptToken("text", "the page itself — the whole of it, not just the slide on screen"),
    };

    private static readonly Regex OptionalRun = new(@"\[\[(.*?)\]\]", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex TokenRef = new(@"\{([A-Za-z]+)\}", RegexOptions.Compiled);
    private static readonly Regex RunOfSpaces = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex TrailingSpace = new(@"[ \t]+$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex BlankRun = new(@"\n{3,}", RegexOptions.Compiled);

    /// <summary>
    /// Where the page's text goes. It is spliced in only after the scaffolding has been tidied, so
    /// the whitespace clean-up can't reflow what the book actually says — and it's a control
    /// character rather than a word so the reader's own prose can never collide with it.
    /// </summary>
    private static readonly string TextSlot = $"{(char)1}page-text{(char)1}";

    public static string Render(string? template, AiPromptContext context)
    {
        // Newlines are normalised first so the blank-run tidy below sees one shape, whatever the
        // template arrived as — a saved one from the editor, or the default as this file stores it.
        var source = (string.IsNullOrWhiteSpace(template) ? Default : template).Replace("\r\n", "\n");

        var body = OptionalRun.Replace(source, m => Keeps(m.Groups[1].Value, context)
            ? Substitute(m.Groups[1].Value, context)
            : "");

        body = Substitute(body, context);

        body = RunOfSpaces.Replace(body, " ");
        body = TrailingSpace.Replace(body, "");
        body = BlankRun.Replace(body, "\n\n");

        return body.Replace(TextSlot, context.Text).Trim();
    }

    /// <summary>An optional run survives only if every token it names has something to say.</summary>
    private static bool Keeps(string run, AiPromptContext context)
    {
        foreach (Match m in TokenRef.Matches(run))
        {
            var value = Resolve(m.Groups[1].Value, context);
            if (value is not null && string.IsNullOrWhiteSpace(value)) return false;
        }
        return true;
    }

    private static string Substitute(string s, AiPromptContext context) =>
        TokenRef.Replace(s, m => Resolve(m.Groups[1].Value, context) ?? m.Value);

    /// <summary>The token's value, "" when the page hasn't got one, null when it isn't a token at all.</summary>
    private static string? Resolve(string name, AiPromptContext c) => name.ToLowerInvariant() switch
    {
        "book" => c.Book.Trim(),
        "author" => c.Author?.Trim() ?? "",
        "chapter" => c.Chapter?.Trim() ?? "",
        "page" => c.Page > 0 ? c.Page.ToString() : "",
        "pages" => c.PageCount > 0 ? c.PageCount.ToString() : "",
        "text" => string.IsNullOrWhiteSpace(c.Text) ? "" : TextSlot,
        _ => null,
    };
}

/// <summary>One substitution the reader can use in their template, and what it stands for.</summary>
public sealed record PromptToken(string Name, string What);
