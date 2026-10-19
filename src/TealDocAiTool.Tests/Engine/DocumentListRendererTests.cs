using TealDocAiTool.Engine;
using TealDocAiTool.Models;

namespace TealDocAiTool.Tests.Engine;

public class DocumentListRendererTests
{
    private static readonly MachineDocument[] Docs =
    {
        new("kezelesi.pdf", "kezelesi.pdf", "A gép kezelési útmutatója."),
        new("rajz.pdf", @"Rajzok\rajz.pdf", null),
    };

    [Fact]
    public void Render_WithoutUrl_KeepsPlainFileNames()
    {
        string result = DocumentListRenderer.Render(Docs, machineUrl: "", linkEachDocument: true);

        Assert.Equal("- kezelesi.pdf — A gép kezelési útmutatója.\n- rajz.pdf", result);
    }

    [Fact]
    public void Render_WithUrl_LinksEachDocument()
    {
        string result = DocumentListRenderer.Render(Docs, "https://x.sharepoint.com/D/1401", linkEachDocument: true);

        Assert.Equal(
            "- [kezelesi.pdf](https://x.sharepoint.com/D/1401/kezelesi.pdf) — A gép kezelési útmutatója.\n" +
            "- [rajz.pdf](https://x.sharepoint.com/D/1401/Rajzok/rajz.pdf)",
            result);
    }

    [Fact]
    public void Render_LinksDisabled_KeepsPlainFileNamesEvenWithUrl()
    {
        string result = DocumentListRenderer.Render(Docs, "https://x.sharepoint.com/D/1401", linkEachDocument: false);

        Assert.DoesNotContain("https://", result);
        Assert.Contains("- rajz.pdf", result);
    }

    [Fact]
    public void Render_EscapesBracketsInLinkText()
    {
        // Kezeletlenül a `]` lezárná a link szövegét, és a maradék nyers szövegként jelenne meg.
        var docs = new[] { new MachineDocument("Rajz [rev2].pdf", "Rajz [rev2].pdf", null) };

        string result = DocumentListRenderer.Render(docs, "https://x.sharepoint.com/D", linkEachDocument: true);

        Assert.Equal(@"- [Rajz \[rev2\].pdf](https://x.sharepoint.com/D/Rajz%20%5Brev2%5D.pdf)", result);
    }

    [Fact]
    public void Render_EmptyList_ReturnsEmptyString() =>
        Assert.Equal("", DocumentListRenderer.Render(Array.Empty<MachineDocument>(), "https://x/D", true));
}
