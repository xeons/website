# Xeon Productions

A .NET 10 Blazor replacement for the WordPress site at xeonproductions.com, with its own
admin backend. WordPress is not required at runtime; it is read once by the importer and
can then be retired.

The public theme follows the GeneratePress layout conventions (container, `site-header`,
`inside-header`, `content-area`, `widget-area`, `entry-meta`, sidebar at 70/30) so the site
keeps the shape it had, with colour, width and typography driven from the admin instead of
a PHP customiser.

## Layout of the solution

| Project | Purpose |
| --- | --- |
| `src/XeonProductions.Domain` | Entities and enums. No dependencies beyond ASP.NET Identity. |
| `src/XeonProductions.Infrastructure` | EF Core context, migrations, and the services: content queries, media, mail, settings, navigation, HTML sanitising, the WordPress importer. |
| `src/XeonProductions.Web` | Blazor Web App: the public site and the `/admin` backend. |
| `tools/XeonProductions.WpImporter` | One-shot console importer that reads the WordPress REST API. |

Rendering is server-side by default. Public pages are static SSR, so they work without a
Blazor circuit and are fully crawlable; the admin screens opt into `InteractiveServer`.

## Requirements

- .NET 10 SDK
- PostgreSQL 15 or newer (the page slug index uses `NULLS NOT DISTINCT`)
- Docker and Docker Compose, for the deployment path below

## Running locally

Start a database:

```bash
docker run -d --name xeon-db \
  -e POSTGRES_DB=xeon_dev \
  -e POSTGRES_USER=xeon \
  -e POSTGRES_PASSWORD=xeon-dev-password \
  -p 5432:5432 postgres:17-alpine
```

Set an admin account for the first run. `appsettings.Development.json` deliberately leaves
these blank, and the seeder creates no account unless both are supplied, so use user secrets
and nothing reaches the repository:

```bash
cd src/XeonProductions.Web
dotnet user-secrets init
dotnet user-secrets set "Seed:AdminEmail" "you@example.com"
dotnet user-secrets set "Seed:AdminPassword" "a-long-password-here"
```

Then run it:

```bash
dotnet run --project src/XeonProductions.Web
```

The app applies migrations and seeds roles, the admin account, the default menus and the
sidebar widgets on start. Sign in at `/admin/login`.

If `Seed:AdminEmail` or `Seed:AdminPassword` is blank, no account is created. That is
deliberate: the application never invents a default password.

## Importing the WordPress content

The importer reads the public REST API, so it needs no WordPress credentials.

```bash
# See what would happen, without writing anything
dotnet run --project tools/XeonProductions.WpImporter -- --dry-run

# Do it
dotnet run --project tools/XeonProductions.WpImporter -- --source https://xeonproductions.com
```

| Flag | Effect |
| --- | --- |
| `--source <url>` | WordPress site to read. Defaults to `https://xeonproductions.com`. |
| `--dry-run` | Report only. |
| `--overwrite` | Replace content that already exists locally, rather than skipping it. |
| `--skip-media` | Do not download attachments. |

What it does:

- Categories and tags, parents resolved in a second pass.
- Media: each attachment is downloaded into the local media store, and in-content URLs are
  rewritten to point at it. Anything that fails to download is reported and left pointing
  at the original host rather than broken.
- Pages, shallowest first, so a child always finds its parent.
- Posts, with their categories, tags, featured image and publication dates.

Re-running is safe. Existing entries are skipped unless `--overwrite` is passed.

The importer writes media through the same service the web app uses, so both must agree on
where that folder is. It reads `Media:StorageRoot` from its own `appsettings.json`, relative
to the working directory, which `dotnet run --project` sets to the project folder. When
running the built binary directly, set it explicitly:

```bash
Media__StorageRoot=/app/media ./XeonProductions.WpImporter
```

### What the importer rewrites

Two things do not survive a move as-is, so the importer converts them.

**Crayon / Urvanov code blocks.** The snippets pages were built with a WordPress syntax
highlighter that renders each sample as a table of per-token spans wrapped in a toolbar.
That markup is meaningless without the plugin's own CSS and JavaScript, so it cannot come
along. The plugin does keep the untouched source in a hidden textarea, so the original code
is recovered exactly and re-emitted as `<pre><code class="language-csharp">`. All 34 blocks
across the snippets and tutorials pages convert cleanly.

Highlighting then happens on the server, through ColorCode, at render time. No highlighting
library is shipped to the browser, there is nothing for the content security policy to
allow, and the code reads correctly with JavaScript turned off. Stored content stays in the
plain `pre`/`code` form, so restyling later is a CSS change rather than a re-import.

C#, C, C++, PHP, JavaScript, TypeScript, SQL, XML, HTML, CSS, JSON, Python, Java and
PowerShell are highlighted. ColorCode ships no shell grammar, and shell is the largest group
of samples here, so `ShellLanguage` adds one covering comments, quoted strings, variables,
option flags, the common builtins and the operators that join commands.

A block only highlights when its `<code>` declares a language, as `class="language-csharp"`.
The importer takes that from the plugin's own label, so a block that was published without
one stays plain until the language is added in the page editor.

**Auto-generated excerpts.** The old theme appended a "Read more" anchor to every excerpt it
generated. Stripping the tags would leave the words "... Read more" sitting in the text, so
the importer detects those and rebuilds the excerpt from the post body instead.

### URLs are preserved

Post permalinks keep the WordPress shape, `/YYYY/MM/DD/slug`, so existing inbound links and
search rankings survive. Pages resolve through their parent chain, so `/snippets/c-simple-list`
still works. All 20 permalinks were verified to match the WordPress originals exactly.

That last part depends on the **site timezone** setting, and it is not cosmetic. WordPress
built permalinks from the site clock, so a post published at 19:18 on 16 March in
America/Chicago lives at `/2024/03/16/...` even though that instant is already 17 March in
UTC. A container runs in UTC, so deriving the date from the server clock silently moves every
evening post to a different URL. The timezone is stored in the database and defaults to
America/Chicago; change it in Settings if the site ever moves.

If a slug does change later, add a rule under **Redirects** in the admin. Those are matched
before routing, from an in-memory map, and issue a real 301.

## The admin backend

Everything at `/admin`, behind cookie authentication with two roles: `Administrator` and
`Editor`.

| Screen | What it covers |
| --- | --- |
| Dashboard | Counts, recently updated content, anything scheduled. |
| Posts | List with search and status filter, full editor with slug, excerpt, categories, tags, featured image, schedule, and per-post SEO. |
| Editing | Rich text editor with an HTML view behind a toggle. See below. |
| Pages | Hierarchical list, editor with parent, menu order and four templates (default, full width, narrow, landing). |
| Media | Upload with drag-and-drop, automatic downscaling and WebP thumbnails, alt text and captions. |
| Downloads | Binaries served through a protected link rather than a static path. See below. |
| Categories, Tags | Create, rename, re-slug, delete. Tags can be pruned of orphans in bulk. |
| Comments | Moderation queue: approve, mark spam, delete. |
| Messages | Contact form submissions, with archive and spam views. |
| Menus | Primary, footer and social menus, with nesting and new-tab links. |
| Widgets | Sidebar and three footer columns. Link lists, custom HTML, recent posts, categories, tags, search, and external RSS or Atom feeds. Affiliate links are flagged and get `rel="sponsored"`. |
| Redirects | The rules described above, with hit counts. |
| Users | Accounts, roles, author profile and password resets. |
| Settings | Site identity, timezone, theme (accent colour, widths, fonts, sidebar side), content behaviour, SEO defaults and the analytics snippet. |

### The editor

Posts and pages are edited with TinyMCE, self-hosted from `wwwroot/lib/tinymce`. It has two
views over the same content, switched with the Visual and HTML tabs. Both panes stay mounted
and are only shown or hidden, because unmounting would restart the editor and lose undo
history.

The HTML view is not a fallback, it is load-bearing. Two imported code blocks still need a
language set on them, which is a `class="language-csharp"` on the `<code>` tag, and that is
not something a visual editor exposes.

Configuration worth knowing about:

- `valid_elements` is `*[*]` and `verify_html` is off. That looks alarming and is not: every
  save goes through the server-side sanitiser, which is the actual gate. Letting the editor
  be permissive is what stops it quietly rewriting imported WordPress markup and code blocks
  on a round trip.
- `convert_urls` is off. TinyMCE rewrites URLs by default, which would undo the relative
  links the importer creates.
- `codesample_languages` lists exactly the languages the server-side highlighter understands,
  so a block inserted through the toolbar highlights on the public page.
- Dropping or pasting an image uploads it into the media library via
  `/admin/api/media/upload` and inserts the stored URL. That endpoint requires the
  `CanEditContent` policy and opts out of antiforgery, which is safe here because the auth
  cookie is `SameSite=Lax` and browsers will not send it on a cross-site POST.
- The skin follows the admin light or dark theme. The browser is asked which is in effect
  before the editor is created, so the first render shows a placeholder.

TinyMCE is **GPL v2 or later**. Running it on your own site triggers no obligation, since
the GPL is about distribution. The licence text ships alongside it at
`wwwroot/lib/tinymce/license.md`. Only the plugins actually used are vendored, about 1.9 MB
rather than the full 12 MB.

The content security policy needed two additions for it: `frame-src 'self' blob:`, because
the editable area is an iframe, and `blob:` in `img-src` for pasted image previews.

### Feed widgets

An **RSS or Atom** widget pulls items from any external feed, which is how the GitHub
activity list is built: point it at `https://github.com/<user>.atom`.

The feed is fetched on the server and cached for twenty minutes, so the feed host sees one
request regardless of traffic, and nothing is requested from the visitor's browser. A feed
that is unreachable or malformed hides the block rather than showing an empty box or
breaking the page, and failures are cached briefly so a dead host is not retried on every
request. Item text is treated as untrusted and rendered as plain text.

Dates are lenient by necessity. Atom is meant to be ISO 8601 and RSS uses RFC 822, but
GitHub writes `2026-08-24 00:38:18 UTC`, which is neither, so the parser falls back through
several formats rather than silently dropping the date.

### The logo

A logo is usually a wide banner rather than a square mark, so the theme fixes its height and
lets the width follow the image's own proportions. **Logo height** sets that, and **Header
layout** chooses between the logo on the left with navigation below, or the logo centred with
the navigation centred beneath it. A wide banner generally reads better centred.

There is a separate **Logo for dark theme** slot. A banner drawn for a light background often
disappears against a dark one. Both are rendered and swapped with CSS, because the theme is a
browser preference and the page is served identically to everyone.

The header emits a `srcset` pairing the generated WebP with the original, so a browser takes
the small one when that is all the header needs. This matters more than it sounds: the logo
loads on every page, and an unscaled export can easily be fifty times larger than the size it
is actually drawn at.

Two settings worth calling out:

- **Blog index shows** is set to *Full posts* by default, so the front page and every archive
  render whole entries the way a classic weblog reads. Switching it to *Excerpt and a
  read-more link* restores the summary listing.
- **Site timezone** drives permalinks and displayed dates, as described above.

## Downloads

Archives, installers and anything else binary live under **Downloads** in the admin. There is
no public downloads index and no listing page: a download is linked by hand from whatever post
or page it belongs to, using the address the admin screen hands you.

Uploads go straight from the browser to `/admin/api/downloads/upload` over an ordinary
multipart POST, not over the Blazor circuit. `InputFile` frames its bytes through SignalR and
buffers them server-side, which is fine for a screenshot and hopeless for a two gigabyte
archive; posting to an endpoint lets Kestrel stream it to disk while a SHA-256 is computed on
the way past. The 25 MB ceiling on media does not apply here. `Downloads:MaxFileSizeBytes`
does, it defaults to 2 GB, and it is enforced against the bytes that actually arrive rather
than against the length the client claimed.

### Why a download is not a media item

Media is written into a folder that is mounted as static files and served straight off disk.
That is exactly right for an image in a post and exactly wrong for a release archive: a static
path can be linked from anywhere, cached by anyone, cannot be counted, and cannot be withdrawn
without deleting the file.

So downloads have no static path at all. The storage root is not mounted, it is not inside the
media root, and every byte leaves through an endpoint. Everything below follows from that.

### The two hops

A download is served in two steps, and the split is the whole design.

`/download/{slug}` is the **stable, permanent** address. It is what you paste into a post and
it never returns a byte of the file. It decides whether the request deserves the file and, if
so, issues a signed ticket. Because it is a decision rather than a payload, an `<img>` or an
`<a>` on someone else's site pointed at it gets a 403 rather than your bandwidth.

`/download/file/{token}` is the **transfer**, and it is the opposite kind of URL: unguessable,
expiring, and tied to the client that asked for it. The token is signed with the application's
data protection key ring - the one already persisted to a volume for auth cookies, so tickets
survive a redeploy - and carries the address and user agent it was issued to. Copying the
resolved address out of the browser and posting it somewhere yields a link that dies within the
hour and was never going to work for anyone else. Nothing about it is worth sharing, which is
what makes it safe to let it carry the file.

The two routes cannot collide: one is two segments and the other is three, so a download
slugged `file` is still reachable and still unambiguous.

### What the referrer check actually checks

Two headers carry the answer and they fail in opposite directions.

`Referer` is the old one and is widely suppressed - a strict referrer policy, a privacy
extension or an HTTPS-to-HTTP hop all send nothing, so its absence proves nothing at all.

`Sec-Fetch-Site` is the newer one and is much the better signal. The browser computes it, a
page cannot suppress it with a referrer policy, and it distinguishes the case this feature
exists for, a request initiated by another site, from a visitor opening a bookmark. It is
preferred wherever it is present, with `Referer` as the fallback for older clients.

That gives three verdicts, and the setting decides what to do with them:

| Setting | Linked from elsewhere | No referrer at all |
| --- | --- | --- |
| Off | allowed | allowed |
| Block links from other sites *(default)* | **403** | allowed |
| Require a link from this site | **403** | **403** |

The default is deliberate. Refusing a request that names another site stops embedding, which
is the thing worth stopping; refusing one that names nothing would turn away real visitors
whose browser is simply not telling you, and would buy very little, because the transfer link
those requests receive is private and short-lived regardless. The strict setting is there when
a bare address pasted into a chat window is also unwanted, and it will cost you some
legitimate traffic.

Individual downloads override the site default, and either level can name extra hosts that are
allowed to link straight at a file. Subdomains of an allowed host count as allowed, so naming
`example.com` does not then turn away `www.example.com`.

### Limits on the bytes themselves

Referrer checks answer embedding. They do nothing about the person who simply takes everything,
so there are three more knobs in **Settings -> Downloads**:

- **Transfers per address per hour** answers the script that walks every link on the site.
- **Simultaneous transfers per address** answers the download manager that opens a dozen
  sockets at one file and takes the whole upstream with it. These are genuinely different
  attacks and neither limit expresses the other.
- **Speed limit per transfer** caps throughput. It holds a long-run average rather than
  policing each chunk, so a transfer that fell behind is allowed to catch up.

IPv6 clients are counted per `/64`, not per address. Handing out a fresh address from your own
prefix is otherwise the cheapest way there is to walk straight through a per-address limit.

The counters are in memory. They are worthless after a restart and a database write per
transfer would be a strange thing to ask of Postgres for a personal site. On more than one
instance each keeps its own counts, so the effective limit is the configured one times the
instance count.

### Smaller things that matter

- Everything is served as `application/octet-stream` with `Content-Disposition: attachment`,
  whatever was uploaded. Serving a stored file under its own declared type would let an
  uploaded `.html` or `.svg` execute inside this origin.
- `Cache-Control: private, no-store` on both hops. A shared cache holding either the redirect
  or the file would hand both to whoever asked next, which is the leech being prevented.
- Range requests are supported, so a large download resumes. The token is checked when a
  transfer starts, not during it, so the lifetime bounds how long a copied link is worth
  something rather than how long a slow download may take.
- Caddy is told not to compress `/download/*`. Beyond wasting CPU on an already-compressed
  archive, a compressed response cannot serve a byte range, which would quietly take resumable
  downloads away.
- `/download/` is disallowed in `robots.txt`. A crawler following those links would spend the
  bandwidth all of this exists to conserve.
- Stored paths carry a random component, so the layout stays unguessable even if the folder
  were ever exposed by a misconfigured proxy.
- Replacing a file keeps the address, the title and the transfer count. Changing the **slug**
  does break links already published, so the editor says so; add a redirect if you must.
- The blocked, expired and missing responses are small self-contained pages that link the site
  stylesheet, so they follow the theme without a layout to render into.

## Testing on a machine with Docker

`docker-compose.dev.yml` brings up the app and the database with no proxy in front, which is
what you want on a box that has no public DNS. Caddy is deliberately absent: it cannot obtain
a certificate without a domain resolving to the host, so it is only exercised by a real deploy.

```bash
ADMIN_PASSWORD=a-long-password-here docker compose -f docker-compose.dev.yml up -d --build
```

The site is then on `http://<host>:8080`, with the admin at `/admin/login`. Postgres is
published on 5432 as well, so the app can be run from a workstation against the same database:

```bash
ConnectionStrings__Default="Host=<host>;Port=5432;Database=xeon;Username=xeon;Password=xeon-dev-password" \
  dotnet run --project src/XeonProductions.Web
```

## Deploying

Copy `.env.example` to `.env`, fill it in, then:

```bash
docker compose up -d --build
```

That brings up three containers: Caddy on 80 and 443 handling TLS automatically, the app on
an internal network, and Postgres on a network the proxy cannot reach. Uploads and database
files live on named volumes, so a rebuild does not touch them.

Point the DNS at the server first, or Caddy cannot complete the certificate challenge.

To run the importer against the deployed database:

```bash
docker compose run --rm \
  -e Media__StorageRoot=/app/media \
  app dotnet XeonProductions.WpImporter.dll --source https://xeonproductions.com
```

### Backups

Three things need backing up: the database, the media volume and the downloads volume.

```bash
docker compose exec -T db pg_dump -U xeon xeon | gzip > xeon-$(date +%F).sql.gz
docker run --rm -v newwebsite_media-data:/media -v "$PWD":/backup alpine \
  tar czf /backup/media-$(date +%F).tar.gz -C /media .
docker run --rm -v newwebsite_downloads-data:/downloads -v "$PWD":/backup alpine \
  tar czf /backup/downloads-$(date +%F).tar.gz -C /downloads .
```

## Security notes

- Post and page HTML is sanitised on save, not on render. Script tags and event handlers are
  removed, and iframes are restricted to a short list of embed hosts.
- A per-request nonce backs the content security policy, so inline scripts run only when the
  application put them there. The analytics snippet from settings is stamped with the nonce
  automatically.
- The contact form has a honeypot field and a fixed-window rate limit. Flagged submissions
  are still stored, just marked as spam, so nothing legitimate is silently discarded.
- Comments are stored as plain text and escaped on render; they never carry HTML.
- Failed sign-ins lock the account for fifteen minutes after five attempts, and the error
  message does not reveal whether the address exists.
- The `returnUrl` on the login form only accepts site-relative paths.
- Downloads are never served from a static path. The stable link issues a signed, expiring,
  client-bound ticket and the transfer happens on that; see the Downloads section above.
- The download upload endpoint validates its antiforgery token by hand rather than through the
  middleware. The middleware falls back to reading the token out of the form body, and doing
  that to a multipart request means buffering the whole upload before the first check runs.

## Things worth knowing

- Settings, menus, widgets and redirects are cached in memory and invalidated when the admin
  saves. A change is visible immediately.
- Interactive admin components take an `IDbContextFactory` and open a short-lived context per
  operation. A Blazor circuit can live for hours, and a context that old serves stale data.
- `/feed.xml`, `/sitemap.xml` and `/robots.txt` are generated from the database. `/feed`
  redirects to `/feed.xml` for readers subscribed to the WordPress URL.
- The theme reads its palette from CSS custom properties emitted per request from the admin
  settings. Only validated hex colours make it into that block.

## Licence

GNU Affero General Public License, version 3 or later. The full text is in `LICENSE`.

The AGPL was chosen over the GPL because this is a web application rather than something
people install. Section 13 is the difference: anyone who runs a modified copy as a network
service has to offer its source to the people using it, which the plain GPL does not require
since nothing is being distributed.

That obligation applies to this deployment too. There is no code change needed to satisfy it,
because the footer already has two ways to carry the link: add an entry to the **Footer** menu
under Menus, or put it in **Footer text** under Settings. Point either at the repository.

### Third-party components

| Component | Licence |
| --- | --- |
| TinyMCE, vendored in `wwwroot/lib/tinymce` | GPL v2 or later |
| TinyMCE.Blazor wrapper | MIT |
| AngleSharp, ColorCode, HtmlSanitizer, MailKit, SkiaSharp, EF Core, ASP.NET Identity | MIT |
| Npgsql | PostgreSQL licence |
| xUnit | Apache 2.0 |
| Moq | BSD 3-Clause |
| bUnit | MIT |

Everything except TinyMCE is permissive and imposes nothing on the combined work.

TinyMCE is copyleft, and the "or later" in its GPL v2 or later grant is what makes this work:
it allows the editor to be taken under GPL v3, and GPL v3 and AGPL v3 are written to permit
being combined with each other. A GPL v2 only dependency would not have been compatible.

None of this is legal advice.

## Statistics

Built in, at **Statistics** in the admin. Nothing is sent anywhere: the site records its own
traffic into its own database.

Page views, visitors, visits, average time on page, bounce rate and a live count, with
breakdowns by page, referrer, entry page, country, browser, operating system and device.

### How a view is recorded

Capture is in two halves because neither half can do the job alone.

The **server** records the view, in middleware, for any HTML page it returns with a 200. That
works with JavaScript off, is unaffected by blockers, and counts the visitors that a script
based tracker never hears from. What it cannot see is how long the page stayed open.

The **browser** reports that. A small script measures time while the page is visible and sends
it with `sendBeacon` when the page is hidden or closed, which survives the page going away
where an ordinary request would be cancelled. Views with no report keep a duration of zero and
are left out of the average rather than dragging it down.

Neither half writes to the database during the request. Views go onto a bounded queue and a
background service writes them in batches, so no page waits on a database round trip. If the
queue fills, views are dropped rather than made to wait: statistics are not worth slowing the
site for.

### No cookies and no addresses

There is no cookie, no local storage and no identifier that survives the day.

A visitor is counted as a hash of their address, their browser string and a secret, together
with the current date. The address itself is never written down, the hash cannot be reversed,
and because the date is part of it the same visitor hashes to something different tomorrow.
There is no column in `page_views` that holds an address or a user agent.

The cost of that is honest and worth stating: **visitors are counted per day.** Somebody
returning on Tuesday and Thursday counts twice, because there is deliberately no way to know
they were the same person. Sessions work the same way, from a rolling thirty minute window.

Crawlers are excluded, and your own visits are not recorded while you are signed in.

### Country data

Country needs a lookup database, which is not shipped: MaxMind licence it separately and
refresh it on their own schedule. Until one is configured the admin says so and every other
figure works as normal.

To enable it, take a free GeoLite2 account, download **GeoLite2-Country**, and put the
`.mmdb` file in `deploy/geoip/`. Compose mounts that directory read only at `/app/geoip` and
`Stats__GeoDatabasePath` already points at it. The file is gitignored; refresh it on
MaxMind's schedule with `geoipupdate` or by hand.

A missing or unreadable file is not an error. It is logged once at startup and country stays
empty.

### Settings

Under `Stats` in configuration:

| Key | Default | Effect |
| --- | --- | --- |
| `Enabled` | `true` | Turns capture off. Existing figures still display. |
| `GeoDatabasePath` | blank | GeoLite2 country database. Blank disables country. |
| `RetentionDays` | `400` | Views older than this are pruned nightly. `0` keeps everything. |
| `SessionWindowMinutes` | `30` | A longer gap starts a new visit. |
| `MaxDurationSeconds` | `1800` | Ceiling on a reported dwell time, so a forgotten tab cannot skew it. |
| `IgnoredPathPrefixes` | admin, media, download, health, api | Never recorded. |
| `IgnoreAuthenticated` | `true` | Skip views from signed-in accounts. |

The chart is inline SVG rendered on the server. No charting library is loaded, there is
nothing for the content security policy to allow, and it draws with JavaScript off.

The **Analytics snippet** setting is untouched and still works, if you want to run something
alongside this.
