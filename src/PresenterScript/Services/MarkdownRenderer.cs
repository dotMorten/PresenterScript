using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers;
using Markdig.Syntax;

namespace PresenterScript.Services;

/// <summary>
/// Converts Markdown into HTML split into presenter "sections" and copyable "snippets".
/// <list type="bullet">
/// <item>Prose is split into sections at every code block and at every thematic break (<c>---</c>).</item>
/// <item>Each snippet records the index of the section that follows it so pasting it can advance the highlight.</item>
/// <item>A line containing only <c>@20</c>, <c>@20:30</c> or <c>@1:05:00</c> sets a time target for its section
/// (minutes[:seconds], or hours:minutes:seconds, into the talk). If such a tag is the document's last non-blank line,
/// it is the total talk length instead.</item>
/// <item>A task list (<c>- [ ] item</c>) at the very top of the document, optionally under headings, becomes a setup
/// checklist that is rendered outside the section flow.</item>
/// </list>
/// </summary>
public static partial class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    [GeneratedRegex(@"^ {0,3}@(\d{1,3}(?::[0-5]\d){0,2})[ \t]*$")]
    private static partial Regex TargetTagLine();

    [GeneratedRegex(@"<!--target:(\d+)-->")]
    private static partial Regex TargetMarker();

    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})")]
    private static partial Regex FenceLine();

    public static RenderResult Render(string markdown)
    {
        var document = Markdown.Parse(ReplaceTargetTags(markdown ?? string.Empty, out var talkLength), Pipeline);

        var html = new StringBuilder();
        var prose = new StringWriter();
        var renderer = new HtmlRenderer(prose);
        Pipeline.Setup(renderer);

        int sectionIndex = 0;
        var snippets = new List<string>();
        var targets = new Dictionary<int, TimeSpan>();
        var emptySections = new HashSet<int>();

        void FlushSection()
        {
            var content = prose.ToString();
            var marker = TargetMarker().Match(content);
            bool empty = string.IsNullOrWhiteSpace(TargetMarker().Replace(content, string.Empty));
            if (empty)
            {
                emptySections.Add(sectionIndex);
            }
            html.Append($"<section class=\"sect{(empty ? " empty" : "")}\" data-idx=\"{sectionIndex}\"");
            if (marker.Success)
            {
                var target = TimeSpan.FromSeconds(int.Parse(marker.Groups[1].Value, CultureInfo.InvariantCulture));
                targets[sectionIndex] = target;
                html.Append($" data-target=\"{(int)target.TotalSeconds}\">")
                    .Append($"<span class=\"target-badge\" title=\"Target: {FormatTarget(target)} into the talk\">⏱ {FormatTarget(target)}</span>");
            }
            else
            {
                html.Append('>');
            }
            html.Append(content).Append("</section>\n");
            prose.GetStringBuilder().Clear();
            sectionIndex++;
        }

        int start = AppendChecklist(document, html);
        for (int i = start; i < document.Count; i++)
        {
            var block = document[i];
            if (block is CodeBlock code)
            {
                FlushSection();
                snippets.Add(AppendSnippet(html, code, snippets.Count, sectionIndex));
            }
            else if (block is ThematicBreakBlock)
            {
                // "---" ends the current section. Skip empty prose so a break right after a snippet
                // (or at the top of the file) doesn't create a dead section.
                if (!string.IsNullOrWhiteSpace(prose.ToString()))
                {
                    FlushSection();
                }
                html.Append("<hr class=\"sep\">\n");
            }
            else
            {
                renderer.Render(block);
                renderer.EnsureLine();
            }
        }
        FlushSection();
        if (talkLength is TimeSpan length)
        {
            html.Append($"<div class=\"talk-end\">⏱ End of talk · {FormatTarget(length)}</div>\n");
        }

        return new RenderResult(html.ToString(), sectionIndex, snippets, targets, talkLength, emptySections);
    }

    /// <summary>
    /// Renders a leading setup checklist (optional headings + task list) and returns the index of the first block after it.
    /// The heading right above the list titles the checklist; any headings before it (e.g. the talk title) are
    /// rendered above the checklist, outside the section flow.
    /// </summary>
    private static int AppendChecklist(MarkdownDocument document, StringBuilder html)
    {
        int listIndex = 0;
        while (listIndex < document.Count && document[listIndex] is HeadingBlock)
        {
            listIndex++;
        }
        if (listIndex >= document.Count || !IsTaskList(document[listIndex]))
        {
            return 0;
        }

        string RenderBlocks(int from, int to)
        {
            var writer = new StringWriter();
            var renderer = new HtmlRenderer(writer);
            Pipeline.Setup(renderer);
            for (int i = from; i < to; i++)
            {
                renderer.Render(document[i]);
                renderer.EnsureLine();
            }
            return writer.ToString();
        }

        int checklistStart = Math.Max(0, listIndex - 1);
        html.Append(RenderBlocks(0, checklistStart))
            .Append("<div class=\"checklist\">")
            .Append(listIndex == 0 ? "<h2>Setup</h2>" : string.Empty)
            .Append(RenderBlocks(checklistStart, listIndex + 1))
            .Append("</div>\n");
        return listIndex + 1;
    }

    private static bool IsTaskList(Block block) =>
        block is ListBlock list &&
        list.FirstOrDefault() is ListItemBlock item &&
        item.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: TaskList };

    /// <summary>
    /// Turns <c>@mm[:ss]</c> tag lines (outside fenced code) into invisible HTML comment markers. A tag on the
    /// document's last non-blank line is the talk length instead of a section target.
    /// </summary>
    private static string ReplaceTargetTags(string markdown, out TimeSpan? talkLength)
    {
        talkLength = null;
        if (!markdown.Contains('@'))
        {
            return markdown;
        }

        // The editor TextBox uses bare '\r' line breaks; files may use CRLF or LF.
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        int lastLine = Array.FindLastIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        string? fence = null;
        for (int i = 0; i < lines.Length; i++)
        {
            var fenceMatch = FenceLine().Match(lines[i]);
            if (fence is null)
            {
                if (fenceMatch.Success)
                {
                    fence = fenceMatch.Groups[1].Value;
                }
                else if (TargetTagLine().Match(lines[i]) is { Success: true } tag && ParseTarget(tag.Groups[1].Value) is TimeSpan t)
                {
                    if (i == lastLine)
                    {
                        talkLength = t;
                        lines[i] = string.Empty;
                    }
                    else
                    {
                        lines[i] = $"<!--target:{(int)t.TotalSeconds}-->";
                    }
                }
            }
            else if (fenceMatch.Success && fenceMatch.Groups[1].Value[0] == fence[0] &&
                     fenceMatch.Groups[1].Value.Length >= fence.Length &&
                     string.IsNullOrWhiteSpace(lines[i][fenceMatch.Length..]))
            {
                fence = null;
            }
        }
        return string.Join('\n', lines);
    }

    /// <summary>Parses <c>m</c>, <c>m:ss</c> or <c>h:mm:ss</c>.</summary>
    private static TimeSpan? ParseTarget(string value)
    {
        var parts = value.Split(':').Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        return parts.Length switch
        {
            1 => TimeSpan.FromMinutes(parts[0]),
            2 => new TimeSpan(0, parts[0], parts[1]),
            3 => new TimeSpan(parts[0], parts[1], parts[2]),
            _ => null,
        };
    }

    private static string FormatTarget(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private static string AppendSnippet(StringBuilder html, CodeBlock code, int index, int nextSection)
    {
        var text = code.Lines.ToString().TrimEnd('\r', '\n');
        html.Append($"<div class=\"snippet\" data-snippet=\"{index}\" data-next=\"{nextSection}\" title=\"Click to copy\">");
        html.Append("<span class=\"copy-badge\">Copy</span>");
        html.Append("<pre><code>").Append(WebUtility.HtmlEncode(text)).Append("</code></pre></div>\n");
        return text;
    }
}

public sealed record RenderResult(
    string Html,
    int SectionCount,
    IReadOnlyList<string> Snippets,
    IReadOnlyDictionary<int, TimeSpan> Targets,
    TimeSpan? TalkLength = null,
    IReadOnlySet<int>? EmptySections = null);
