using System.IO;
using System.Windows.Media.Imaging;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Mark.Tests.Sales;
using Xunit;

namespace Mark.Tests.Design;

/// <summary>Mesh can be added to any design from every category; fixed panes carry the glass mark and FIXED.</summary>
public class UniversalMeshTests
{
    [Fact]
    public void EveryCategory_OffersMeshForAnyDesign_First()
    {
        var library = QuotationPdfTests.OnSta(() =>
        {
            var vm = new DesignLibraryViewModel(new DesignRules(), (_, _) => null);
            var firsts = new List<(string Category, string Section, string[] Items)>();
            foreach (string category in DesignTemplates.Categories)
            {
                vm.SelectedCategory = category;
                var first = vm.Sections[0];
                firsts.Add((category, first.Title, first.Items.Select(i => i.Name).ToArray()));
            }
            return firsts;
        });

        foreach (var (category, section, items) in library.Where(f => f.Category != DesignTemplates.Mesh))
        {
            Assert.Equal(DesignTemplates.UniversalSection, section);
            Assert.Equal(new[] { "Add mesh (keeps the design)", "Remove mesh (keeps the design)" }, items);
        }
        // The Mesh category lists them once, in its own section.
        Assert.NotEqual(DesignTemplates.UniversalSection, library.Single(f => f.Category == DesignTemplates.Mesh).Section);
    }

    [Fact]
    public void AddMesh_KeepsACasementAndAFixedPane_AsTheyAre()
    {
        var rules = new DesignRules();
        var frame = FrameEditor.CreateFrame(0, 0, 1500, 1200, rules);
        FrameEditor.ApplyTemplate(frame, DesignTemplates.Find("cas-fixed-left")!, null, rules);
        var before = frame.GlassPanels.Select(g => g.Opening).ToList();

        FrameEditor.ApplyTemplate(frame, DesignTemplates.Universal.Single(t => t.Id == "mesh-add"), null, rules);

        Assert.Equal(before, frame.GlassPanels.Select(g => g.Opening));
        Assert.All(frame.GlassPanels, g => Assert.True(g.HasMesh));
    }

    [Fact]
    public void AFixedPane_IsDrawnWithItsGlassMark()
    {
        var sample = LibrarySerializer.Load(TempDatabase.ShippedLibraryPath);
        var rules = new DesignRules();
        byte[] Png(OpeningType opening) => QuotationPdfTests.OnSta(() =>
        {
            var frame = FrameEditor.CreateFrame(0, 0, 1000, 1200, rules);
            FrameEditor.SetOpening(frame, frame.GlassPanels.Select(g => g.Id).ToList(), opening, false, rules);
            return QuotationBuilder.RenderDrawing(frame, rules, sample)!;
        });

        // The fixed pane has the glass strokes and FIXED drawn in its own colour; a casement does not.
        Assert.True(Count(Png(OpeningType.Fixed)) > Count(Png(OpeningType.SideHungLeft)) + 50);
    }

    /// <summary>Pixels in the fixed-glass blue (0x4F 0x8F 0xBF / 0x4F 0x7F 0xA8, give or take).</summary>
    private static int Count(byte[] png)
    {
        var decoder = new PngBitmapDecoder(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        int n = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            if (r < 110 && g is > 110 and < 160 && b > 150 && b - r > 60) n++;
        }
        return n;
    }
}
