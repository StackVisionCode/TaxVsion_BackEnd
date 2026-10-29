using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.DataCollection.Engine.Text;

internal static class RichTextValidator
{
    private static readonly HashSet<string> BlockTypes = new(
        ["paragraph", "heading", "bulletList", "orderedList"],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> Alignments = new(
        ["left", "center", "right", "justify"],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> Fonts = new(
        ["sans", "serif", "script1", "script2"],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> Marks = new(["bold", "italic", "underline"], StringComparer.Ordinal);
    private static readonly HashSet<string> Colors = new(
        ["ink", "secondary", "muted", "brand", "accent", "success", "warning", "danger", "info"],
        StringComparer.Ordinal
    );

    internal static IReadOnlyList<ValidationIssue> Validate(RichTextDocument? document, string path, int maxCharacters)
    {
        if (document is null)
            return [];

        var issues = new List<ValidationIssue>();
        if (document.Type != "doc" || document.Blocks.Count > 200)
            AddInvalidNode(issues, path, "Rich text document is invalid.");

        var characterCount = 0;
        for (var index = 0; index < document.Blocks.Count; index++)
        {
            var block = document.Blocks[index];
            var blockPath = $"{path}/blocks/{index}";
            if (!BlockTypes.Contains(block.Type))
            {
                AddInvalidNode(issues, blockPath, "Rich text block type is not supported.");
                continue;
            }
            if (block.Type == "heading" && block.Level is not (2 or 3))
                AddInvalidNode(issues, $"{blockPath}/level", "Heading level must be 2 or 3.");
            if (block.Type != "heading" && block.Level is not null)
                AddInvalidNode(issues, $"{blockPath}/level", "Level is allowed only on headings.");
            if (block.Align is not null && !Alignments.Contains(block.Align))
                AddInvalidNode(issues, $"{blockPath}/align", "Alignment is not supported.");
            if (block.Font is not null && !Fonts.Contains(block.Font))
                AddInvalidNode(issues, $"{blockPath}/font", "Font is not supported.");

            var isList = block.Type is "bulletList" or "orderedList";
            if (isList)
            {
                if (block.Children.Count > 0 || block.Items.Count == 0)
                    AddInvalidNode(issues, blockPath, "Lists require items and cannot contain direct children.");
                for (var itemIndex = 0; itemIndex < block.Items.Count; itemIndex++)
                {
                    if (block.Items[itemIndex].Count == 0)
                        AddInvalidNode(issues, $"{blockPath}/items/{itemIndex}", "List items cannot be empty.");
                    ValidateInlines(
                        block.Items[itemIndex],
                        $"{blockPath}/items/{itemIndex}",
                        issues,
                        ref characterCount,
                        true
                    );
                }
            }
            else
            {
                if (block.Items.Count > 0)
                    AddInvalidNode(issues, $"{blockPath}/items", "Paragraphs and headings cannot contain list items.");
                ValidateInlines(block.Children, $"{blockPath}/children", issues, ref characterCount, true);
            }
        }

        if (characterCount > maxCharacters)
            AddInvalidNode(issues, path, $"Rich text exceeds {maxCharacters} characters.");
        return issues;
    }

    internal static int CharacterCount(RichTextDocument? document) =>
        document?.Blocks.Sum(block => Count(block.Children) + block.Items.Sum(Count)) ?? 0;

    internal static string PlainText(RichTextDocument? document) =>
        document is null
            ? string.Empty
            : string.Join(
                ' ',
                document.Blocks.SelectMany(block =>
                    new[] { Flatten(block.Children) }.Concat(block.Items.Select(Flatten))
                )
            );

    private static int Count(IEnumerable<RichTextInline> nodes) =>
        nodes.Sum(node => (node.Text?.Length ?? 0) + Count(node.Children));

    private static string Flatten(IEnumerable<RichTextInline> nodes) =>
        string.Concat(nodes.Select(node => (node.Text ?? string.Empty) + Flatten(node.Children)));

    private static void ValidateInlines(
        IReadOnlyList<RichTextInline> nodes,
        string path,
        ICollection<ValidationIssue> issues,
        ref int characterCount,
        bool allowLinks
    )
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var nodePath = $"{path}/{index}";
            if (node.Type is not ("text" or "link") || (!allowLinks && node.Type == "link"))
            {
                AddInvalidNode(issues, nodePath, "Rich text inline type is not supported.");
                continue;
            }

            characterCount += node.Text?.Length ?? 0;
            if (
                node.Marks.Count != node.Marks.Distinct(StringComparer.Ordinal).Count()
                || node.Marks.Any(mark => !Marks.Contains(mark))
                || (node.Color is not null && !Colors.Contains(node.Color))
            )
                AddInvalidNode(issues, nodePath, "Rich text mark is not supported.");

            if (node.Type == "link")
            {
                if (!Uri.TryCreate(node.Href, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    issues.Add(
                        new ValidationIssue(
                            "richtext.invalid_link",
                            $"{nodePath}/href",
                            null,
                            null,
                            "Only HTTPS links are allowed."
                        )
                    );
                if (node.Text is not null || node.Children.Count == 0)
                    AddInvalidNode(issues, nodePath, "Links require child text and cannot contain direct text.");
                ValidateInlines(node.Children, $"{nodePath}/children", issues, ref characterCount, false);
            }
            else if (node.Text is null || node.Children.Count > 0 || node.Href is not null)
            {
                AddInvalidNode(issues, nodePath, "Text nodes require text and cannot contain links or children.");
            }
        }
    }

    private static void AddInvalidNode(ICollection<ValidationIssue> issues, string path, string message) =>
        issues.Add(new ValidationIssue("richtext.invalid_node", path, null, null, message));
}
