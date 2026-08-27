namespace XeonProductions.Infrastructure.Services;

public record ImportReport
{
    public int Categories { get; set; }
    public int Tags { get; set; }
    public int Media { get; set; }
    public int MediaFailed { get; set; }
    public int Pages { get; set; }
    public int Posts { get; set; }
    public int Skipped { get; set; }
    public List<string> Warnings { get; } = [];

    public override string ToString() =>
        $"{Posts} posts, {Pages} pages, {Categories} categories, {Tags} tags, " +
        $"{Media} media ({MediaFailed} failed), {Skipped} skipped, {Warnings.Count} warnings";
}
