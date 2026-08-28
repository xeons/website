namespace XeonProductions.Domain.Enums;

public enum ContentStatus
{
    Draft = 0,
    Published = 1,
    Scheduled = 2,
    Private = 3,
    Archived = 4
}

public enum PageTemplate
{
    /// <summary>Content column plus the primary sidebar.</summary>
    Default = 0,
    /// <summary>Content spans the full container width, no sidebar.</summary>
    FullWidth = 1,
    /// <summary>Full width with no page title heading, for landing pages.</summary>
    Landing = 2,
    /// <summary>Narrow, centred reading column with no sidebar.</summary>
    Narrow = 3
}

/// <summary>How the masthead arranges the logo and the navigation.</summary>
public enum HeaderLayout
{
    /// <summary>Logo on the left, actions on the right, navigation bar below.</summary>
    LogoLeft = 0,

    /// <summary>Logo centred on its own row, navigation centred beneath it.</summary>
    Centered = 1,

    /// <summary>
    /// Logo drawn across the full container width as a masthead banner, with the
    /// navigation bar directly beneath it.
    /// </summary>
    Banner = 2
}

/// <summary>How strictly a download checks where the request came from.</summary>
public enum HotlinkProtection
{
    /// <summary>No referrer check. A signed transfer link is still issued.</summary>
    Off = 0,

    /// <summary>Refuses a request naming another site, allows one naming nothing.</summary>
    Lenient = 1,

    /// <summary>Requires a referrer from an allowed host.</summary>
    Strict = 2
}

/// <summary>The kind of client a page view came from.</summary>
public enum DeviceType
{
    Unknown = 0,
    Desktop = 1,
    Mobile = 2,
    Tablet = 3,

    /// <summary>A crawler or other automated client. Excluded from the reports.</summary>
    Bot = 4
}

public enum MenuLocation
{
    Primary = 0,
    Footer = 1,
    Social = 2
}

public enum CommentStatus
{
    Pending = 0,
    Approved = 1,
    Spam = 2,
    Trash = 3
}

public enum WidgetArea
{
    Sidebar = 0,
    FooterColumn1 = 1,
    FooterColumn2 = 2,
    FooterColumn3 = 3,
    BelowPost = 4
}

public enum WidgetType
{
    LinkList = 0,
    Html = 1,
    RecentPosts = 2,
    Categories = 3,
    Tags = 4,
    Search = 5,
    About = 6,

    /// <summary>Items pulled from an external RSS or Atom feed.</summary>
    RssFeed = 7
}

/// <summary>How much of a post the blog index and archives show.</summary>
public enum BlogContentDisplay
{
    /// <summary>A short summary with a link through to the post.</summary>
    Excerpt = 0,

    /// <summary>The whole post, the way a classic weblog reads.</summary>
    FullContent = 1
}

/// <summary>
/// How the content area is boxed, mirroring the GeneratePress container options.
/// </summary>
public enum ContainerStyle
{
    /// <summary>Each entry and widget is its own card on the page background.</summary>
    Separate = 0,

    /// <summary>Content and sidebar share one surface, divided by rules rather than gaps.</summary>
    One = 1
}

public enum SidebarLayout
{
    RightSidebar = 0,
    LeftSidebar = 1,
    NoSidebar = 2
}
