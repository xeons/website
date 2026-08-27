using ColorCode;
using ColorCode.Common;

namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// A shell grammar for ColorCode, which ships none.
///
/// Shell is the largest group of code samples on this site, so leaving those blocks plain
/// while C# and PHP were coloured looked like the highlighter was broken. This covers what
/// a command listing actually contains: comments, quoted strings, variables, option flags,
/// the common builtins, and the operators that join commands together.
/// </summary>
public class ShellLanguage : ILanguage
{
    public string Id => "shell";
    public string Name => "Shell";
    public string CssClassName => "shell";

    /// <summary>Used to detect the language from a shebang when none was declared.</summary>
    public string FirstLinePattern => @"^#!/.*\b(bash|sh|zsh|ksh)\b";

    public IList<LanguageRule> Rules =>
    [
        // Comments first: a # inside a string is handled by ordering, since ColorCode
        // applies rules in sequence and will not re-scope text already claimed.
        new LanguageRule(
            @"(?m)(?<=^|\s)(#(?!\!).*?)$",
            new Dictionary<int, string> { { 1, ScopeName.Comment } }),

        // Shebang line.
        new LanguageRule(
            @"(?m)^(#!.*)$",
            new Dictionary<int, string> { { 1, ScopeName.PreprocessorKeyword } }),

        // Double-quoted strings, honouring backslash escapes.
        new LanguageRule(
            @"(""[^""\\]*(?:\\.[^""\\]*)*"")",
            new Dictionary<int, string> { { 1, ScopeName.String } }),

        // Single-quoted strings, which take no escapes in shell.
        new LanguageRule(
            @"('[^']*')",
            new Dictionary<int, string> { { 1, ScopeName.String } }),

        // Variables: $NAME, ${NAME}, $1, $@, and command substitution $(...).
        new LanguageRule(
            @"(\$\{[^}]+\}|\$[A-Za-z_][A-Za-z0-9_]*|\$[0-9@*#?$!-])",
            new Dictionary<int, string> { { 1, ScopeName.PowerShellVariable } }),

        // Control flow and shell keywords.
        new LanguageRule(
            @"(?<=^|\s|;|\||&|\()(if|then|else|elif|fi|for|while|until|do|done|case|esac|" +
            @"function|select|in|return|break|continue|local|export|declare|readonly|" +
            @"source|eval|exec|trap|shift|set|unset|alias)(?=\s|;|$|\))",
            new Dictionary<int, string> { { 1, ScopeName.Keyword } }),

        // Commands that show up constantly in a listing like this one.
        new LanguageRule(
            @"(?<=^|\s|;|\||&|\()(sudo|apt|apt-get|yum|dnf|pacman|systemctl|service|" +
            @"docker|docker-compose|git|ssh|scp|rsync|curl|wget|tar|gzip|gunzip|zip|unzip|" +
            @"chmod|chown|chgrp|ls|cd|pwd|mkdir|rmdir|rm|cp|mv|ln|find|locate|which|whereis|" +
            @"cat|less|more|head|tail|grep|egrep|sed|awk|cut|sort|uniq|wc|tr|tee|xargs|" +
            @"ps|top|htop|kill|killall|jobs|bg|fg|nohup|screen|tmux|crontab|" +
            @"df|du|free|uname|uptime|whoami|id|su|passwd|useradd|usermod|groupadd|" +
            @"ping|netstat|ss|ip|ifconfig|iptables|ufw|nano|vim|vi|echo|printf|read|" +
            @"mount|umount|dd|fdisk|lsblk|journalctl|dmesg|history|man|env|" +
            @"npm|node|dotnet|python|python3|pip|pip3|make|gcc|g\+\+|java|mysql|psql)" +
            @"(?=\s|;|$|\))",
            new Dictionary<int, string> { { 1, ScopeName.Type } }),

        // Option flags: -v, --verbose, -rf.
        new LanguageRule(
            @"(?<=\s)(--?[A-Za-z][A-Za-z0-9-]*)",
            new Dictionary<int, string> { { 1, ScopeName.Number } }),

        // Operators that join or redirect commands.
        new LanguageRule(
            @"(\|\||&&|[|;]|\d?>>?|<<?|&)",
            new Dictionary<int, string> { { 1, ScopeName.PreprocessorKeyword } })
    ];

    public bool HasAlias(string lang) =>
        lang.ToLowerInvariant() switch
        {
            "sh" or "bash" or "zsh" or "shell" or "console" or "terminal" => true,
            _ => false
        };

    public override string ToString() => Name;
}
