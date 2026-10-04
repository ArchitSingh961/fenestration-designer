using Mark.Core.Commands;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Sales;

/// <summary>Saving a quote keeps you where you are.</summary>
public class SaveStaysTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(AppView.Drawing)]
    [InlineData(AppView.Client)]
    [InlineData(AppView.Designs)]
    [InlineData(AppView.Pricing)]
    public void Saving_StaysOnTheSameTab(AppView view)
    {
        var vm = new MainViewModel(_temp.Open(TempDatabase.ShippedLibraryPath), null, new FakeDialogs { PromptAnswer = "Quote" });
        vm.NewQuote();
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 0, 0, 1200, 1500, vm.Rules));
        vm.ShowView(view);
        var area = vm.Area;
        Assert.Equal(view, vm.CurrentView);

        vm.SaveProjectCommand.Execute(null);
        Assert.Equal(view, vm.CurrentView);
        Assert.Equal(area, vm.Area);

        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 2000, 0, 1200, 1500, vm.Rules));
        vm.SaveProjectCommand.Execute(null);                             // a second save, of a quote saved before
        Assert.Equal(view, vm.CurrentView);
        Assert.Equal(area, vm.Area);
    }
}
