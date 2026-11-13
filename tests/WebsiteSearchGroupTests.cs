using System.Reflection;
using MioKit.Sdk;
using WebSearch.Models;
using WebSearch.Nodes;

namespace WebSearch.Tests;

[CollectionDefinition("SDK store factory", DisableParallelization = true)]
public sealed class SdkStoreFactoryCollection;

[Collection("SDK store factory")]
public sealed class WebsiteSearchGroupTests
{
    [Fact]
    public void BrowserSearchSettingsStoreEachSwitchIndependently()
    {
        var factoryProperty = typeof(MioObject).Assembly
            .GetType("MioKit.Sdk.StoreFactory", throwOnError: true)!
            .GetProperty("StoreFactoryFunc", BindingFlags.Static | BindingFlags.NonPublic)!;
        var originalFactory = factoryProperty.GetValue(null);
        Func<MioObject, DesignMioStore> designStoreFactory = owner => new DesignMioStore(owner);
        factoryProperty.SetValue(null, designStoreFactory);

        try
        {
            var settings = new WebSearchSettingsNode("settings-test");
            Assert.False(settings.HistoryGlobalEnabled);
            Assert.False(settings.BookmarkGlobalEnabled);
            settings.HistoryGlobalEnabled = true;
            Assert.True(settings.HistoryGlobalEnabled);
            Assert.False(settings.BookmarkGlobalEnabled);
            settings.BookmarkGlobalEnabled = true;
            Assert.True(settings.HistoryGlobalEnabled);
            Assert.True(settings.BookmarkGlobalEnabled);
        }
        finally
        {
            factoryProperty.SetValue(null, originalFactory);
        }
    }

    [Fact]
    public async Task TemplateQueriesProduceOneSearchResultAtBottom()
    {
        // MioObject obtains its store from an SDK-wide internal factory. Use the
        // SDK's in-memory design store for this test and restore the factory.
        var factoryProperty = typeof(MioObject).Assembly
            .GetType("MioKit.Sdk.StoreFactory", throwOnError: true)!
            .GetProperty("StoreFactoryFunc", BindingFlags.Static | BindingFlags.NonPublic)!;
        var originalFactory = factoryProperty.GetValue(null);
        Func<MioObject, DesignMioStore> designStoreFactory = owner => new DesignMioStore(owner);

        Assert.True(factoryProperty.PropertyType.IsAssignableFrom(designStoreFactory.GetType()));
        factoryProperty.SetValue(null, designStoreFactory);

        try
        {
            var group = new WebsiteSearchGroup("websites-test");
            var website = new WebsiteNode("bing-test")
            {
                Name = "Bing",
                UrlTemplate = "https://www.bing.com/search?q={query}",
                QuerySourcePolicy = QuerySourcePolicy.InputThenClipboard
            };
            // SetParent publishes host tree events, which require MioAppContext.
            // SearchAsync only reads Children, so populate that SDK collection.
            var childrenField = typeof(MioObject).GetField(
                "_children", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.NotNull(childrenField);
            Assert.IsType<List<MioObject>>(childrenField.GetValue(group)).Add(website);

            var results = new ResultList();
            await group.SearchAsync(
                new SearchRequest { SearchText = "Bing" },
                results,
                CancellationToken.None);

            var search = Assert.Single(results.ToList());
            Assert.Equal("使用 Bing 搜索“Bing”", search.Title);
            Assert.Equal(SortPriority.AlwaysBottom, search.PinMode);
            Assert.Equal("https://www.bing.com/search?q=Bing", Assert.IsType<OpenUrlPayload>(search.Payload).Url);

            var ordinaryResults = new ResultList();
            await group.SearchAsync(
                new SearchRequest { SearchText = "weather" },
                ordinaryResults,
                CancellationToken.None);
            var ordinarySearch = Assert.Single(ordinaryResults.ToList());
            Assert.Equal(SortPriority.AlwaysBottom, ordinarySearch.PinMode);
            Assert.Equal(
                "https://www.bing.com/search?q=weather",
                Assert.IsType<OpenUrlPayload>(ordinarySearch.Payload).Url);
        }
        finally
        {
            factoryProperty.SetValue(null, originalFactory);
        }
    }

    private sealed class ResultList : IWriteOnlyResultList<SearchResult>
    {
        private readonly List<SearchResult> _results = [];

        public int Count => _results.Count;
        public bool IsEmpty => _results.Count == 0;
        public void Add(SearchResult item) => _results.Add(item);
        public void AddRange(IEnumerable<SearchResult> items) => _results.AddRange(items);
        public List<SearchResult> ToList() => [.._results];
    }
}
