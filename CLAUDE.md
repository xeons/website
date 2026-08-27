# Working in this repository

## Text and encoding

Every file committed here is plain ASCII. Do not introduce em dashes, en dashes, curly
quotes, ellipsis characters, arrows, non-breaking spaces or any other character above
U+007F, in source, markup, comments, documentation or commit messages.

Use `-` for a dash, `'` and `"` for quotes, `...` for an ellipsis, `->` for an arrow.

This includes UTF-8 byte order marks. No tracked file we maintain starts with one.

Before finishing a change, verify it:

```bash
python tools/check-ascii.py
```

It scans every tracked file and exits non-zero on anything above U+007F. Vendored
third-party code under `wwwroot/lib/` is skipped, since it is replaced wholesale on upgrade
and is not ours to edit.

`dotnet ef` writes a byte order mark on the files it generates, so run the fixer after
adding a migration:

```bash
python tools/check-ascii.py --fix
```

`--fix` only strips byte order marks. It never rewrites a real character, because choosing
the ASCII replacement is a judgement call.

## Comments

A comment says what the code does, or what a caller must know to use it correctly.

Do not write comments that narrate history, justify a past decision, record a bug that was
once fixed, compare the current approach against one that was tried before, or speculate
about what might go wrong. That material does not belong in the source. Put it in
`NOTES.local.md`, which is ignored by git.

Wrong:

```csharp
// This used to read the form body, which buffered the whole upload before the first check
// ran and caused large uploads to fail. Switching to the header avoids that.
```

Right:

```csharp
// Reads the token from the request header. The form fallback would buffer the body.
```

Keep them short. An XML doc comment on a public member should describe the contract in a
sentence or two. Prefer no comment to one that restates the line beneath it.

## File organisation

One type per file, named after the type. Interfaces, classes, records, enums, structs and
delegates each get their own file.

- `IDownloadService` goes in `IDownloadService.cs`.
- `DownloadService` goes in `DownloadService.cs`.
- An enum used by both goes in its own file next to them.

Namespaces follow the folder path. Entities live under `Domain/Entities`, enums under
`Domain/Enums`, services under `Infrastructure/Services` or `Web/Services`, endpoint groups
under `Web/Endpoints`, Razor components under `Web/Components`.

Some older files predate this rule and still hold several types. Do not rewrite them as a
side effect of unrelated work; split them when the work is actually about that file.

## Notes that are not code

`NOTES.local.md` is gitignored. Use it for anything that would otherwise become a large
explanatory comment: why an approach was chosen, what was tried and rejected, a bug that
was diagnosed, an observation that may matter later. Keep it out of the tracked tree.

## Project layout

| Project | Contains |
| --- | --- |
| `src/XeonProductions.Domain` | Entities and enums. No dependencies beyond ASP.NET Identity. |
| `src/XeonProductions.Infrastructure` | EF Core context, migrations, services for content, media, downloads, mail, settings, navigation and HTML sanitising. |
| `src/XeonProductions.Web` | Blazor Web App: the public site and the `/admin` backend. |
| `tools/XeonProductions.WpImporter` | One-shot console importer for the old WordPress content. |

Public pages render as static SSR. Admin screens opt into `InteractiveServer`.

## Conventions that already hold

- Interactive components take `IDbContextFactory<AppDbContext>` and open a short-lived
  context per operation. A circuit can outlive a context by hours.
- Post and page HTML is sanitised on save, not on render.
- Settings, menus, widgets and redirects are cached in memory and invalidated on save.
- Uploaded media is served from a static file mount. Downloads are not, and must not be;
  they are served only through the download endpoints.
- Anything reaching a response header from user input is stripped of control characters
  first.
- Database changes go through an EF migration. Do not hand-edit a generated migration.

## Before calling a change done

- `dotnet build` is clean, with no new warnings.
- `python tools/check-ascii.py` exits 0.
- New types are each in their own file.
- Comments describe behaviour, not history.
