using XeonProductions.Web.Components.Tools;

namespace XeonProductions.Web.Tools;

/// <summary>
/// Every tool served under /tools, in the order they appear on the index.
///
/// The tools run entirely in the browser. Nothing here reaches the server, so a definition
/// carries only the text used to describe and route to a tool, never any of its behaviour.
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ToolDefinition> All { get; } =
    [
        new("hash", "Hash Generator",
            "MD5, SHA-1, SHA-256, SHA-384, SHA-512 and CRC32 of any text, side by side.",
            "Hash text with MD5, SHA-1, SHA-256, SHA-384, SHA-512 or CRC32. Runs in your "
            + "browser, so the text never leaves your machine.",
            ToolCategory.Hashing, typeof(HashTool)),

        new("hmac", "HMAC Generator",
            "Sign a message with a secret key using SHA-1, SHA-256, SHA-384 or SHA-512.",
            "Generate an HMAC signature from a message and a secret key, in hex or Base64. "
            + "Runs entirely in your browser.",
            ToolCategory.Hashing, typeof(HmacTool)),

        new("base64", "Base64 Encode and Decode",
            "Convert text to and from Base64, with a URL-safe variant.",
            "Encode text to Base64 or decode Base64 back to text, including the URL-safe "
            + "alphabet. Handles the full Unicode range and runs in your browser.",
            ToolCategory.Encoding, typeof(Base64Tool)),

        new("url", "URL Encode and Decode",
            "Percent-encode text for a query string, or decode it back.",
            "Percent-encode text for use in a URL, or decode an encoded URL back to plain "
            + "text. Runs in your browser.",
            ToolCategory.Encoding, typeof(UrlTool)),

        new("hex", "Hex Encode and Decode",
            "Convert text to and from hexadecimal, with a choice of separator.",
            "Convert text to hexadecimal or decode hex back to text, with optional separators "
            + "and upper case output. Runs in your browser.",
            ToolCategory.Encoding, typeof(HexTool)),

        new("binary", "Binary Encode and Decode",
            "Convert text to and from its binary representation.",
            "Convert text to binary or decode binary back to text, one group of eight bits "
            + "per byte. Runs in your browser.",
            ToolCategory.Encoding, typeof(BinaryTool)),

        new("html-entities", "HTML Entity Encode and Decode",
            "Escape text for HTML, or turn entities back into characters.",
            "Escape characters that have meaning in HTML, or decode named and numeric HTML "
            + "entities back to text. Runs in your browser.",
            ToolCategory.Encoding, typeof(HtmlEntityTool)),

        new("jwt", "JWT Decoder",
            "Read the header and claims of a JSON Web Token. Nothing is sent anywhere.",
            "Decode a JSON Web Token to read its header, payload and claim timestamps. The "
            + "token is decoded in your browser and never transmitted.",
            ToolCategory.Formats, typeof(JwtTool)),

        new("json", "JSON Formatter",
            "Pretty-print, minify and validate JSON, with the error position on failure.",
            "Format, minify and validate JSON. Errors report the position they occur at. "
            + "Runs in your browser.",
            ToolCategory.Formats, typeof(JsonTool)),

        new("base-convert", "Number Base Converter",
            "Convert between binary, octal, decimal and hex at arbitrary precision.",
            "Convert a number between binary, octal, decimal and hexadecimal. Uses arbitrary "
            + "precision arithmetic, so large values keep every digit.",
            ToolCategory.Formats, typeof(BaseConvertTool)),

        new("timestamp", "Timestamp Converter",
            "Unix epoch to a readable date and back, in UTC and your local time.",
            "Convert a Unix timestamp in seconds or milliseconds to a readable date, or a "
            + "date back to epoch. Shows UTC and your local time zone.",
            ToolCategory.Formats, typeof(TimestampTool)),

        new("colour", "Colour Converter",
            "Convert between HEX, RGB and HSL with a live preview.",
            "Convert a colour between HEX, RGB and HSL notation and preview the result. Runs "
            + "in your browser.",
            ToolCategory.Formats, typeof(ColourTool)),

        new("uuid", "UUID Generator",
            "Generate version 4 or time-ordered version 7 UUIDs in bulk.",
            "Generate random version 4 UUIDs or time-ordered version 7 UUIDs, one or many at "
            + "a time. Uses the browser's cryptographic random source.",
            ToolCategory.Generators, typeof(UuidTool)),

        new("password", "Password Generator",
            "Random passwords and passphrases, with the entropy shown for each.",
            "Generate a random password or passphrase and see how much entropy it carries. "
            + "Uses the browser's cryptographic random source; nothing is transmitted.",
            ToolCategory.Generators, typeof(PasswordTool)),

        new("case", "Case Converter",
            "camelCase, PascalCase, snake_case, kebab-case, CONSTANT_CASE and more.",
            "Convert text between camelCase, PascalCase, snake_case, kebab-case, "
            + "CONSTANT_CASE, Title Case and sentence case.",
            ToolCategory.Text, typeof(CaseTool)),

        new("text-stats", "Text Statistics",
            "Characters, words, lines, and the true UTF-8 byte length.",
            "Count characters, words, sentences and lines, and measure the real UTF-8 byte "
            + "length of a block of text.",
            ToolCategory.Text, typeof(TextStatsTool)),

        new("diff", "Text Diff",
            "Compare two blocks of text and see which lines were added or removed.",
            "Compare two blocks of text line by line and see exactly what was added, removed "
            + "or left alone. Runs in your browser.",
            ToolCategory.Text, typeof(DiffTool)),

        new("regex", "Regex Tester",
            "Test a regular expression against sample text with highlighted matches.",
            "Test a JavaScript regular expression against sample text, with matches "
            + "highlighted and capture groups listed. Runaway patterns are cut off safely.",
            ToolCategory.Text, typeof(RegexTool))
    ];

    /// <summary>The tool for a URL slug, or null when the slug matches nothing.</summary>
    public static ToolDefinition? Find(string? slug) =>
        string.IsNullOrWhiteSpace(slug)
            ? null
            : All.FirstOrDefault(t => string.Equals(t.Slug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>The tools in one category, in catalog order.</summary>
    public static IEnumerable<ToolDefinition> InCategory(ToolCategory category) =>
        All.Where(t => t.Category == category);

    /// <summary>Section heading for a category.</summary>
    public static string HeadingFor(ToolCategory category) => category switch
    {
        ToolCategory.Encoding => "Encoding",
        ToolCategory.Hashing => "Hashing",
        ToolCategory.Generators => "Generators",
        ToolCategory.Text => "Text",
        ToolCategory.Formats => "Formats and data",
        _ => category.ToString()
    };
}
