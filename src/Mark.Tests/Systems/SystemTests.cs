using Mark.Calculation;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Systems;

/// <summary>
/// Milestone 13: product systems, "used with", reinforcement and bundles — in the library, the calculation, the frame
/// editor, the database and the designer. Most tests use the shipped sample library, whose three systems are realistic.
/// </summary>
public class SystemTests
{
    private const string Upvc = "SYS-UPVC-62C";
    private const string Sliding = "SYS-AL-SL60";
    private const string Casement = "SYS-AL-S60C";

    private static readonly Lazy<ProductLibrary> Shipped = new(() => LibrarySerializer.Load(TempDatabase.ShippedLibraryPath));

    private static ProductLibrary Library => Shipped.Value;

    private static readonly DesignRules Rules = new();

    /// <summary>A frame of the given size in a system (members and glass follow the system).</summary>
    private static Frame FrameIn(string? systemId, double width = 1000, double height = 1200)
    {
        var frame = FrameEditor.CreateFrame(0, 0, width, height, Rules);
        FrameEditor.SetSystem(frame, systemId, Library, Rules);
        return frame;
    }

    private static CalculationResult Calculate(params Frame[] frames)
    {
        var project = new Project();
        project.Frames.AddRange(frames);
        return new CalculationEngine().Calculate(project, Library, new CalculationRules());
    }

    private static void Open(Frame frame, OpeningType opening)
        => FrameEditor.SetOpening(frame, frame.GlassPanels.Select(g => g.Id).ToList(), opening, null, Rules);

    // ── Library ─────────────────────────────────────────────────────

    [Fact]
    public void ShippedLibrary_HasThreeSystemsWithBundles()
    {
        Assert.Equal(new[] { Casement, Upvc, Sliding }, Library.Systems.Select(x => x.Id));
        Assert.Equal(SystemMaterial.Upvc, Library.FindSystem(Upvc)!.Material);
        Assert.Equal(Casement, Library.DefaultSystem!.Id);
        Assert.Contains(Library.Bundles, b => b.SystemId == Sliding && b.ProfileId == "PRF-SL-FRM-2T");
        Assert.Equal("PRF-RI-FRM-62", Library.FindProfile("PRF-UPVC-FRM-62")!.Reinforcement!.ProfileId);
    }

    [Fact]
    public void SystemsAndBundles_SurviveTheLibraryFile()
    {
        var again = LibrarySerializer.Deserialize(LibrarySerializer.Serialize(Library));

        Assert.Equal(LibrarySerializer.Serialize(Library), LibrarySerializer.Serialize(again));
        var hinges = again.FindBundle("BND-UPVC-CASE-HW")!.Parts[0];
        Assert.Equal(PartBasis.BySize, hinges.Basis);
        Assert.Equal(3, hinges.QuantityFor(1500));
        Assert.Equal(2, hinges.QuantityFor(1200));
    }

    [Fact]
    public void InconsistentSystemsAndBundles_AreRejected()
    {
        var steel = Library.FindProfile("PRF-RI-FRM-62")!;
        string Error(Func<ProductLibrary> make) => Assert.Throws<LibraryValidationException>(make).Message;

        Assert.Contains("not in the library", Error(() => new ProductLibrary(Library.Profiles, Library.Glass, Library.Materials,
            Library.Defaults, "INR", Library.Systems.Append(new ProductSystem { Id = "SYS-X", Name = "X", FrameProfileId = "NOPE" }), Library.Bundles)));
        Assert.Contains("cannot be the frame profile", Error(() => new ProductLibrary(Library.Profiles, Library.Glass, Library.Materials,
            Library.Defaults, "INR", Library.Systems.Append(new ProductSystem { Id = "SYS-X", Name = "X", FrameProfileId = steel.Id }), Library.Bundles)));
        Assert.Contains("must grow", Error(() => new ProductLibrary(Library.Profiles, Library.Glass, Library.Materials, Library.Defaults, "INR",
            Library.Systems, Library.Bundles.Append(new Bundle
            {
                Id = "BND-X", Name = "X", Parts = new[]
                {
                    new BundlePart { ItemId = "MAT-HINGE", Basis = PartBasis.BySize, Steps = new[] { new SizeStep { UpToMm = 900, Quantity = 1 }, new SizeStep { UpToMm = 600, Quantity = 2 } } }
                }
            }))));
        Assert.Contains("reinforcement role", Error(() => new ProductLibrary(
            Library.Profiles.Select(p => p.Id == "PRF-UPVC-FRM-62" ? p with { Reinforcement = new ReinforcementRule { ProfileId = "PRF-UPVC-BEAD-24" } } : p),
            Library.Glass, Library.Materials, Library.Defaults, "INR", Library.Systems, Library.Bundles)));
        Assert.Contains("used with system", Error(() => new ProductLibrary(
            Library.Profiles.Select(p => p.Id == "PRF-FRM-60" ? p with { UsedWith = new UsedWith { SystemIds = new[] { "SYS-GONE" } } } : p),
            Library.Glass, Library.Materials, Library.Defaults, "INR", Library.Systems, Library.Bundles)));
    }

    // ── Catalogue selection ─────────────────────────────────────────

    [Fact]
    public void SelectingASystem_BringsEverythingItNeeds()
    {
        var catalogue = CatalogueSelector.Select(Library, new CatalogueSelection { SystemIds = new[] { Upvc } });

        Assert.Equal(new[] { Upvc }, catalogue.Systems.Select(x => x.Id));
        Assert.All(catalogue.Bundles, b => Assert.Equal(Upvc, b.SystemId));
        foreach (string id in new[] { "PRF-UPVC-FRM-62", "PRF-UPVC-SASH-62", "PRF-RI-FRM-62", "PRF-UPVC-BEAD-24", "GLS-DGU-24", "MAT-HINGE",
                     "MAT-RI-SCREW", "PRF-UPVC-COUPLER" })
            Assert.NotNull(catalogue.NameOf(id));
        Assert.Null(catalogue.FindProfile("PRF-SL-TRACK"));
        Assert.Null(catalogue.FindGlass("GLS-TGH-8"));
        Assert.Equal(Upvc, catalogue.Defaults.SystemId);
        Assert.Null(catalogue.Defaults.FrameProfileId);                  // the master's default frame is not in it
    }

    [Fact]
    public void SystemsOfUnlicensedMaterials_AreLeftOut_SingleItemsComeAlong()
    {
        var selection = new CatalogueSelection { SystemIds = new[] { Upvc, Sliding }, ItemIds = new[] { "GLS-TGH-10" } };

        var upvcOnly = CatalogueSelector.Select(Library, selection, m => m == SystemMaterial.Upvc);

        Assert.Equal(new[] { Upvc }, upvcOnly.Systems.Select(x => x.Id));
        Assert.Null(upvcOnly.FindProfile("PRF-SL-FRM-2T"));
        var glass = upvcOnly.FindGlass("GLS-TGH-10")!;
        Assert.Empty(glass.UsedWith!.SystemIds);                         // its systems are not in this catalogue
    }

    // ── Frame editor ────────────────────────────────────────────────

    [Fact]
    public void ChangingTheSystem_TakesItsProfilesAndGlass_KeepingTheOuterSize()
    {
        var frame = FrameEditor.CreateFrame(0, 0, 1000, 1200, Rules);
        Assert.True(FrameEditor.TryAddDivision(frame, MemberAxis.Vertical, null, null, Rules, out _).Success);
        frame.GlassPanels[0].GlassDefinitionId = "GLS-TGH-8";

        FrameEditor.SetSystem(frame, Upvc, Library, Rules);

        Assert.Equal(Upvc, frame.SystemId);
        Assert.Equal((1000.0, 1200.0), (frame.Width, frame.Height));
        Assert.All(frame.Profiles, p => Assert.Null(p.ProfileDefinitionId));
        Assert.All(frame.Profiles.Where(p => p.ProfileType == ProfileType.Frame), p => Assert.Equal(62, p.Thickness));
        Assert.Equal(74, frame.Profiles.Single(p => p.ProfileType == ProfileType.Mullion).Thickness);
        Assert.All(frame.GlassPanels, g =>
        {
            Assert.Null(g.GlassDefinitionId);                            // 8 mm does not fit 20–28 mm: the system's glass
            Assert.Equal(24, g.Thickness);
        });

        FrameEditor.SetSystem(frame, null, Library, Rules);
        Assert.Null(frame.SystemId);
        Assert.All(frame.Profiles.Where(p => p.ProfileType == ProfileType.Frame), p => Assert.Equal(60, p.Thickness));
    }

    [Fact]
    public void UnknownSystem_IsRefused()
    {
        var frame = FrameEditor.CreateFrame(0, 0, 1000, 1200, Rules);

        Assert.False(FrameEditor.TrySetSystem(frame, "SYS-NOPE", Library, Rules).Success);
        Assert.Null(frame.SystemId);
    }

    // ── Calculation ─────────────────────────────────────────────────

    [Fact]
    public void AFrameInASystem_UsesTheSystemsProfilesAndGlass()
    {
        var result = Calculate(FrameIn(Upvc));

        Assert.All(result.Profiles.Where(p => p.OpeningId is null && p.PartOf is null), p => Assert.Equal("PRF-UPVC-FRM-62", p.DefinitionId));
        Assert.Equal("GLS-DGU-24", result.Glass.Single().DefinitionId);
        Assert.DoesNotContain(result.Issues, i => i.Message.Contains("drawn"));
    }

    [Fact]
    public void UpvcFrameBars_GetSteelReinforcementAndTheirBundleParts()
    {
        var result = Calculate(FrameIn(Upvc));

        var steel = result.Profiles.Where(p => p.DefinitionId == "PRF-RI-FRM-62").Select(p => p.CutLengthMm).Order().ToList();
        Assert.Equal(new[] { 900.0, 900, 1100, 1100 }, steel);         // 100 mm shorter than the mitred bars
        Assert.All(result.Profiles.Where(p => p.DefinitionId == "PRF-RI-FRM-62"), p => Assert.Equal("Reinforcement", p.PartOf));
        Assert.Equal(4.0, result.Frames[0].ReinforcementMetres);
        Assert.Equal(4, result.Profiles.Count(p => p.DefinitionId == "PRF-UPVC-BEAD-24"));
        Assert.Equal(2, result.Materials.Where(m => m.MaterialId == "MAT-DRAIN").Sum(m => m.Quantity));   // bottom bar only
        Assert.Equal(4.4, result.Materials.Where(m => m.MaterialId == "MAT-UPVC-GSK").Sum(m => m.Quantity), 6);
        Assert.Contains(result.Bom, l => l.Category == BomCategory.Reinforcement && l.ItemId == "PRF-RI-FRM-62");
        Assert.Contains(result.CutList, c => c.DefinitionId == "PRF-RI-FRM-62" && c.CutLengthMm == 1100);
    }

    [Fact]
    public void ReinforcementFromALength_SkipsShortBars()
    {
        var frame = FrameIn(Upvc, 1000, 500);
        Assert.True(FrameEditor.TryAddDivision(frame, MemberAxis.Vertical, null, null, Rules, out _).Success);   // a mullion of about 376 mm (< 600 mm)

        var result = Calculate(frame);

        Assert.DoesNotContain(result.Profiles, p => p.DefinitionId == "PRF-RI-MUL-62");
    }

    [Fact]
    public void HardwareSets_CountBySize()
    {
        var small = FrameIn(Upvc, 800, 1200);
        Open(small, OpeningType.SideHungLeft);
        var tall = FrameIn(Upvc, 800, 1600);
        Open(tall, OpeningType.SideHungLeft);

        var result = Calculate(small, tall);

        double Hinges(Frame f) => result.Materials.Where(m => m.FrameId == f.Id && m.MaterialId == "MAT-HINGE").Sum(m => m.Quantity);
        Assert.Equal(2, Hinges(small));
        Assert.Equal(3, Hinges(tall));
        Assert.Equal(1, result.Materials.Where(m => m.FrameId == small.Id && m.MaterialId == "MAT-MPLOCK").Sum(m => m.Quantity));
        Assert.All(result.Openings, o => Assert.True(o.HasHardwareSet));
        Assert.Contains(result.Profiles, p => p.DefinitionId == "PRF-RI-SASH-62");    // sash bars are reinforced too
    }

    [Fact]
    public void SlidingFrame_GetsTheTrackOnTheBottomBar_AndTheSashItsInterlockAndRollers()
    {
        var frame = FrameIn(Sliding, 1500, 1200);
        Open(frame, OpeningType.SlidingLeft);

        var result = Calculate(frame);

        var track = result.Profiles.Single(p => p.DefinitionId == "PRF-SL-TRACK");
        Assert.Equal(1500, track.CutLengthMm);
        Assert.Equal("Sliding 2-track frame", track.PartOf);
        var interlock = result.Profiles.Single(p => p.DefinitionId == "PRF-SL-INTERLOCK");
        var sashHeight = result.SashBarsOf(frame.GlassPanels[0].Id).First(p => p.Role == ProfileType.Sash && p.PartOf is null && p.CutLengthMm < 1200).CutLengthMm;
        Assert.True(interlock.CutLengthMm > 1000);
        Assert.Equal(2, result.Materials.Where(m => m.MaterialId == "MAT-ROLLER").Sum(m => m.Quantity));
        Assert.Contains(result.Profiles, p => p.DefinitionId == "PRF-SL-SASH-45");
        Assert.True(sashHeight > 0);
    }

    [Fact]
    public void GlassOutsideTheSystemsRange_IsAWarning()
    {
        var frame = FrameIn(Upvc);
        FrameEditor.AssignGlass(frame, frame.GlassPanels.Select(g => g.Id).ToList(), "GLS-CLR-6", Library, Rules);

        var result = Calculate(frame);

        Assert.Contains(result.Issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("20–28 mm"));
    }

    [Fact]
    public void RatesStandInOnlyForWhatTheLibraryDoesNotList()
    {
        var frame = FrameIn(Upvc);
        Open(frame, OpeningType.SideHungLeft);
        var project = new Project();
        project.Frames.Add(frame);
        var result = new CalculationEngine().Calculate(project, Library, new CalculationRules());
        var pricing = PriceStructure.Default();
        pricing.Heads.Clear();
        pricing.Rates.CasementHardware = 1000;
        pricing.Rates.ReinforcementPerMetre = 50;

        var price = PricingEngine.Price(project, result, pricing);

        Assert.Equal(result.Frames[0].Cost.Total, price.Designs[0].MaterialCost);
        Assert.Equal(0, price.Designs[0].RatedCost);                    // hardware set and steel are in the library cost
    }

    // ── Database and library service ────────────────────────────────

    [Fact]
    public void SystemsBundlesAndRules_AreStoredAndReadBack()
    {
        using var db = new TempDatabase();
        var store = db.Open();
        store.Library.Import(Library);

        var reopened = db.Open().Library.Current;

        Assert.Equal(LibrarySerializer.Serialize(Library), LibrarySerializer.Serialize(reopened));
    }

    [Fact]
    public void ACatalogue_UpdatesTheLibrary_KeepsTheCompanysPrices_AndRetiresWhatIsGone()
    {
        using var db = new TempDatabase();
        var service = db.Open().Library;
        service.ApplyCatalogue(CatalogueSelector.Select(Library, new CatalogueSelection { SystemIds = new[] { Casement, Upvc } }));
        var frame = service.Current.FindProfile("PRF-UPVC-FRM-62")!;
        service.Update(frame with { CostPerMetre = 299 });                // the company's own price

        var next = CatalogueSelector.Select(Library, new CatalogueSelection { SystemIds = new[] { Upvc } });
        var renamed = new ProductLibrary(next.Profiles.Select(p => p.Id == frame.Id ? p with { Name = "62mm Frame (new)" } : p),
            next.Glass, next.Materials, next.Defaults, next.Currency, next.Systems, next.Bundles);
        var result = service.ApplyCatalogue(renamed);

        var updated = service.Current.FindProfile(frame.Id)!;
        Assert.Equal("62mm Frame (new)", updated.Name);
        Assert.Equal(299, updated.CostPerMetre);
        Assert.Contains(frame.Id, result.Updated);
        Assert.Contains(Casement, result.Retired);
        Assert.False(service.Current.FindSystem(Casement)!.IsActive);     // kept for saved quotes
        Assert.False(service.Current.FindProfile("PRF-SASH-55")!.IsActive);
        Assert.False(service.ApplyCatalogue(renamed).Changed);            // the same catalogue again changes nothing
    }

    [Fact]
    public void SystemsInUse_CannotBeDeleted()
    {
        using var db = new TempDatabase();
        var service = db.Open().Library;
        service.Import(Library);

        Assert.NotEmpty(service.FindBlockers(LibraryItemKind.System, Upvc));
        Assert.Throws<LibraryOperationException>(() => service.Delete(LibraryItemKind.Profile, "PRF-RI-FRM-62"));
        service.Delete(LibraryItemKind.Bundle, "BND-UPVC-TT-HW");
        Assert.Null(service.Current.FindBundle("BND-UPVC-TT-HW"));
    }

    [Fact]
    public void AnOldLibrary_GetsTheShippedSystemsOnce()
    {
        using var db = new TempDatabase();
        var old = new ProductLibrary(Library.Profiles.Where(p => p.Reinforcement is null && p.UsedWith is null).Select(p => p with { UsedWith = null }),
            null, Library.Materials.Select(m => m with { UsedWith = null }));
        db.Open().Library.Import(old);

        var store = db.Open(TempDatabase.ShippedLibraryPath);

        Assert.Equal(3, store.Library.Current.Systems.Count);
        Assert.Equal(Casement, store.Library.Current.Defaults.SystemId);
        Assert.Contains(store.StartupMessages, m => m.Contains("product systems"));
        Assert.DoesNotContain(db.Open(TempDatabase.ShippedLibraryPath).StartupMessages, m => m.Contains("product systems"));
    }

    // ── Designer ────────────────────────────────────────────────────

    [Fact]
    public void NewFrames_AreInTheDefaultSystem_AndTheSystemCanBeChanged()
    {
        var vm = new MainViewModel(Library);
        vm.CreateFrame();
        var frame = vm.Project.Frames.Single();
        Assert.Equal(Casement, frame.SystemId);
        vm.Select(frame.Id);
        Assert.NotNull(vm.Properties.SystemPicker);

        Assert.Null(vm.AssignSystem(Upvc));
        Assert.Equal(Upvc, frame.SystemId);
        vm.UndoCommand.Execute(null);
        Assert.Equal(Casement, frame.SystemId);
    }

    [Fact]
    public void Pickers_OfferOnlyWhatFitsTheFramesSystem()
    {
        var vm = new MainViewModel(Library);
        vm.CreateFrame();
        var frame = vm.Project.Frames.Single();
        vm.AssignSystem(Upvc);
        vm.Select(frame.Id);

        var glass = vm.Properties.GlassPicker!.Options.Select(o => o.Id).ToList();
        var profiles = vm.Properties.ProfilePicker!.Options.Select(o => o.Id).ToList();

        Assert.Equal(new[] { "GLS-DGU-24" }, glass);
        Assert.Contains("PRF-UPVC-FRM-62", profiles);
        Assert.DoesNotContain("PRF-FRM-60", profiles);
    }

    [Fact]
    public void LibraryManager_EditsSystemsAndBundles()
    {
        using var db = new TempDatabase();
        var service = db.Open().Library;
        service.Import(Library);
        var manager = new LibraryManagerViewModel(service) { Kind = LibraryItemKind.Bundle };
        Assert.Equal(Library.Bundles.Count, manager.Items.Count);

        manager.NewCommand.Execute(null);
        var bundle = Assert.IsType<BundleEditorViewModel>(manager.Editor);
        bundle.Id = "BND-NEW";
        bundle.Name = "Extra handle";
        bundle.GoesWithOpenings = true;
        bundle.OpeningTypes.Single(o => o.Value == OpeningType.TopHung).IsChecked = true;
        bundle.AddPartCommand.Execute(null);
        bundle.Parts[0].Item = bundle.Parts[0].Items.Single(i => i.Id == "MAT-HANDLE");
        bundle.Parts[0].QuantityText = "1";
        manager.SaveCommand.Execute(null);

        Assert.False(manager.MessageIsError, manager.Message);
        var saved = service.Current.FindBundle("BND-NEW")!;
        Assert.True(saved.IsOpeningSet);
        Assert.Equal(new[] { OpeningType.TopHung }, saved.OpeningTypes);

        manager.Kind = LibraryItemKind.System;
        manager.SelectedItem = manager.Items.Single(i => i.Id == Upvc);
        var system = Assert.IsType<SystemEditorViewModel>(manager.Editor);
        system.GlassMax = "32";
        manager.SaveCommand.Execute(null);
        Assert.Equal(32, service.Current.FindSystem(Upvc)!.GlassMaxThicknessMm);
    }

    [Fact]
    public void PricesOnlyLibraryManager_ChangesPricesButNothingElse()
    {
        using var db = new TempDatabase();
        var service = db.Open().Library;
        service.Import(Library);
        var manager = new LibraryManagerViewModel(service, pricesOnly: true);

        Assert.False(manager.NewCommand.CanExecute(null));
        Assert.False(manager.ImportCommand.CanExecute(null));
        manager.SelectedItem = manager.Items.Single(i => i.Id == "PRF-FRM-60");
        Assert.False(manager.ItemEditor!.CanEditDetails);
        Assert.False(manager.DeleteCommand.CanExecute(null));
        manager.ItemEditor.CostPerMetre = "555";
        manager.SaveCommand.Execute(null);

        Assert.Equal(555, service.Current.FindProfile("PRF-FRM-60")!.CostPerMetre);
        manager.Kind = LibraryItemKind.System;
        manager.SelectedItem = manager.Items.First();
        Assert.False(manager.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void AnOtherPart_KeepsItsRole_WhenItsPriceIsSaved()
    {
        // A clip or cleat cut from a bar has the "other part" role; saving its price must not lose it.
        var clip = Library.FindProfile("PRF-FRM-60")! with { Id = "PRF-CLIP", Name = "Mesh Clip", Roles = new[] { ProfileType.Generic } };
        var editor = LibraryItemEditorViewModel.For(clip, library: Library);
        Assert.True(editor.RoleGeneric);
        editor.CostPerMetre = "85";
        var built = (ProfileDefinition)editor.Build(out string? error)!;

        Assert.Null(error);
        Assert.Equal(new[] { ProfileType.Generic }, built.Roles);
        Assert.Equal(85, built.CostPerMetre);
    }

    [Fact]
    public void ProfileEditor_SetsReinforcementAndUsedWith()
    {
        var editor = LibraryItemEditorViewModel.For(Library.FindProfile("PRF-UPVC-FRM-70")!, library: Library);
        Assert.Equal("PRF-RI-FRM-62", editor.Reinforcement.Id);
        Assert.True(editor.UsedWithSystems.Single(s => s.Value == Upvc).IsChecked);

        editor.Reinforcement = editor.ReinforcementChoices[0];             // (none)
        editor.UsedWithSystems.Single(s => s.Value == Casement).IsChecked = true;
        var built = (ProfileDefinition)editor.Build(out string? error)!;

        Assert.Null(error);
        Assert.Null(built.Reinforcement);
        Assert.Equal(new[] { Casement, Upvc }, built.UsedWith!.SystemIds);
    }
}
