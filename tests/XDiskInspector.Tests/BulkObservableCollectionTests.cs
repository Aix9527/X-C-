using System.Collections.Specialized;
using XDiskInspector.App;

namespace XDiskInspector.Tests;

public sealed class BulkObservableCollectionTests
{
    [Fact]
    public void ReplaceAll_RaisesSingleReset_AndReplacesContents()
    {
        var collection = new BulkObservableCollection<int> { 1, 2, 3 };
        var events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);

        collection.ReplaceAll(new[] { 10, 20, 30, 40 });

        Assert.Equal(new[] { 10, 20, 30, 40 }, collection);
        var change = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Reset, change.Action);
    }

    [Fact]
    public void ReplaceAll_WithEmptySequence_RaisesSingleReset_AndClearsContents()
    {
        var collection = new BulkObservableCollection<string> { "a", "b" };
        var events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);

        collection.ReplaceAll(Array.Empty<string>());

        Assert.Empty(collection);
        var change = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Reset, change.Action);
    }
}
