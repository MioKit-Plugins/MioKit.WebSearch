using System.Collections.ObjectModel;
using System.Collections.Specialized;
using WebSearch.Models;
using WebSearch.Services;

namespace WebSearch.Tests;

public sealed class WebsiteRefreshTests
{
    [Fact]
    public void SupersededRefreshKeepsEveryPendingWebsiteAndOnlyInvalidatesRequestedIcons()
    {
        var requests = new WebsiteRefreshRequests();
        requests.Add("first", false);
        var stale = requests.Capture();
        requests.Add("second", true);
        requests.Add("second", false);

        Assert.False(requests.Complete(stale));
        var merged = requests.Capture();
        Assert.False(merged.All);
        Assert.False(merged.AllIcons);
        Assert.True(merged.Ids.SetEquals(["first", "second"]));
        Assert.True(merged.IconIds.SetEquals(["second"]));
        Assert.True(requests.Complete(merged));
        Assert.Empty(requests.Capture().Ids);
        Assert.Empty(requests.Capture().IconIds);
    }

    [Fact]
    public void FullRefreshSurvivesInterveningIndividualChanges()
    {
        var requests = new WebsiteRefreshRequests();
        requests.Add(null, true);
        var stale = requests.Capture();
        requests.Add("first", false);
        Assert.False(requests.Complete(stale));
        var merged = requests.Capture();
        Assert.True(merged.All);
        Assert.True(merged.AllIcons);
        Assert.True(requests.Complete(merged));
        Assert.False(requests.Capture().All);
        Assert.False(requests.Capture().AllIcons);
    }

    [Fact]
    public void EditingOneRowDoesNotResetCollectionOrReplaceUnchangedRows()
    {
        var first = new WebsiteDto { Id = "first", Name = "A" };
        var second = new WebsiteDto { Id = "second", Name = "B" };
        var rows = new ObservableCollection<WebsiteDto> { first, second };
        var events = new List<NotifyCollectionChangedEventArgs>();
        rows.CollectionChanged += (_, e) => events.Add(e);

        WebSearchJsService.SynchronizeWebsiteRows(rows, [first with { Name = "AA" }, second with { }]);

        Assert.Same(second, rows[1]);
        Assert.Equal("AA", rows[0].Name);
        var change = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Replace, change.Action);
    }

    [Fact]
    public void RenamingAddingAndDeletingReordersRowsWithoutReset()
    {
        var a = new WebsiteDto { Id = "a", Name = "A" };
        var b = new WebsiteDto { Id = "b", Name = "B" };
        var c = new WebsiteDto { Id = "c", Name = "C" };
        var rows = new ObservableCollection<WebsiteDto> { a, b, c };
        rows.CollectionChanged += (_, e) => Assert.NotEqual(NotifyCollectionChangedAction.Reset, e.Action);
        var expected = new[] { c with { Name = "AA" }, a, new WebsiteDto { Id = "d", Name = "D" } };
        WebSearchJsService.SynchronizeWebsiteRows(rows, expected);
        Assert.Equal(expected, rows);
        Assert.Same(a, rows[1]);
    }
}
