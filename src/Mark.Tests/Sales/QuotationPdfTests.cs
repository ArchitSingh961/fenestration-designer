using System.IO;
using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Designer.ViewModels;
using Mark.Reports;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Sales;

/// <summary>Milestone 15: the quotation PDF, built from a quote in the usual quotation layout.</summary>
public class QuotationPdfTests
{
    private static readonly ProductLibrary Sample = LibrarySerializer.Load(TempDatabase.ShippedLibraryPath);

    /// <summary>Runs WPF rendering on its own STA thread (tests run on MTA threads).</summary>
    internal static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException(error.Message, error);
        return result;
    }

    /// <summary>A priced quote with a casement, a sliding window in frosted glass, and a fixed window.</summary>
    internal static MainViewModel Quote()
    {
        var vm = new MainViewModel(Sample);
        vm.Project.Name = "Sample residence";
        vm.Project.Quote.Number = "QT-00042";
        vm.Project.Quote.Client = new ClientInfo { Title = "Mr.", FirstName = "Test", LastName = "Client", AddressLine1 = "12 Park Road", City = "Jaipur" };
        var casement = DesignTemplates.ForSystem(Sample.FindSystem("SYS-UPVC-62C")!, Sample).First(t => t.Openings.Any(o => o!.Value.IsHinged()));
        var sliding = DesignTemplates.ForSystem(Sample.FindSystem("SYS-AL-SL60")!, Sample).First(t => t.Openings.Any(o => o!.Value.IsSliding()));
        Assert.Null(vm.ApplyDesign(casement, "SYS-UPVC-62C"));
        vm.ClearSelection();
        Assert.True(((Mark.Designer.Interaction.IViewportDropTarget)vm).Drop(new Mark.Core.Geometry.Point2D(2500, 0), $"{sliding.Id}@SYS-AL-SL60"));
        var slidingFrame = vm.Project.Frames[1];
        vm.CommandHistory.Execute(new AssignGlassCommand(slidingFrame, slidingFrame.GlassPanels.Select(g => g.Id).ToList(), "GLS-FRS-5", vm.Library, vm.Rules));
        vm.Project.Frames[0].Design.Reference = "W1";
        vm.Project.Frames[0].Design.Location = "Bedroom";
        vm.Project.Frames[0].Design.Quantity = 3;
        vm.Project.Frames[0].Design.ProfileColour = "White";
        vm.Project.Frames[0].Design.HandleColour = "White";
        slidingFrame.Design.Reference = "W2";
        slidingFrame.Design.Location = "Living room";
        vm.ClearSelection();
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 6000, 0, 900, 1200, vm.Rules));
        return vm;
    }

    internal static QuotationDocument Document(MainViewModel vm, QuotationSettings? settings = null)
        => QuotationBuilder.Build(new QuotationInputs(vm.Project, vm.Calculation.Result, vm.Price, vm.Library, vm.Rules,
            settings ?? new QuotationSettings
            {
                PartnerLabel = "Authorised partner", Address = "Plot 1, Industrial Area\nJaipur 302001", Phone = "+91 90000 00000",
                Email = "sales@example.com", Gstin = "08ABCDE1234F1Z5", BrandName = "Sample Systems", BankAccountName = "Test Windows",
                BankAccountNumber = "000111222333", BankName = "Sample Bank", BankIfsc = "SMPL0000001"
            },
            "Test Windows", null, new DateTime(2026, 10, 4)));

    [Fact]
    public void AQuote_BecomesAQuotation_WithEveryDesignAndTheTotals()
    {
        var doc = OnSta(() => Document(Quote()));

        Assert.Equal("QT-00042", doc.QuoteNumber);
        Assert.Equal("04-10-2026", doc.Date);
        Assert.Equal("Mr. Test Client", doc.To[0]);
        Assert.Equal(3, doc.Designs.Count);
        var w1 = doc.Designs[0];
        Assert.Equal("W1", w1.Code);
        Assert.Equal("62mm Casement – uPVC", w1.System);
        Assert.StartsWith("W = 1,", w1.Size);
        Assert.NotNull(w1.Drawing);
        Assert.Equal(new[] { "Sq.Ft. per window", "Value per Sq.Ft.", "Unit Price", "Quantity", "Value" }, w1.Values.Select(v => v.Label));
        Assert.Equal("3", w1.Values[3].Value);
        // As on the usual quotation: colour, mesh, each profile by what it is with its reinforcement right after it.
        Assert.Equal("Profile Color : White", w1.Profiles[0]);
        Assert.Equal("MeshType : No", w1.Profiles[1]);
        int outer = w1.Profiles.ToList().FindIndex(p => p.StartsWith("OUTER : "));
        Assert.True(outer > 1);
        Assert.StartsWith("OUTER RI : ", w1.Profiles[outer + 1]);
        Assert.Contains(w1.Profiles, p => p.StartsWith("CASEMENT SASH : "));
        Assert.StartsWith("Locking : Multi-point", w1.Accessories[0]);
        Assert.Equal("Handle color : White", w1.Accessories[1]);
        Assert.Contains(doc.Designs[1].Profiles, p => p.StartsWith("SLIDING SASH : "));
        Assert.StartsWith("Locking : Single-point", doc.Designs[1].Accessories[0]);
        Assert.Contains(doc.Totals, t => t.Label == "GST @18%");
        Assert.Contains("5mm Frosted Toughened", doc.Designs[1].Glass);
        Assert.Contains(doc.Totals, t => t.Label == "No. of Components" && t.Value == "5");
        Assert.Contains(doc.Totals, t => t.Label == "Grand Total");
        Assert.Contains(doc.Company.Lines, l => l == "GSTIN : 08ABCDE1234F1Z5");
        Assert.Equal(new[] { "Account Name", "Account Number", "Bank Name", "IFSC" }, doc.Bank!.Rows.Select(r => r.Label));
        Assert.Equal(6, doc.Terms.Count);
    }

    [Fact]
    public void TheQuotation_IsWrittenAsAPdf_OverSeveralPages()
    {
        byte[] pdf = OnSta(() =>
        {
            using var stream = new MemoryStream();
            QuotationPdf.Write(Document(Quote()), stream);
            return stream.ToArray();
        });

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(pdf.Length > 20_000);
        if (Environment.GetEnvironmentVariable("MARK_QUOTATION_OUT") is { Length: > 0 } path)
            File.WriteAllBytes(path, pdf);
        int pages = OnSta(() => QuotationPdf.CountPages(Document(Quote())));
        Assert.InRange(pages, 4, 6);                     // letter, designs (2 + 1), total, terms
    }

    [Fact]
    public void SquareMetres_CanBeUsedInsteadOfSquareFeet()
    {
        var doc = OnSta(() => Document(Quote(), new QuotationSettings { AreaUnit = AreaUnit.SquareMetres }));

        Assert.Equal("m² per window", doc.Designs[0].Values[0].Label);
        Assert.Null(doc.Bank);
        Assert.Null(doc.Brand);
    }
}
