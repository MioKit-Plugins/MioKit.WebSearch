namespace WebSearch.Nodes;

[EavType("2A14CC9F-ED8C-4E35-9F6A-BDE41DE0BF02")]
public sealed class BookmarkSearchScope : SearchScopeFeatureBase
{
    private readonly BrowserDatabaseSearchNode _searchNode;

    public BookmarkSearchScope(string id) : base(id)
    {
        Name = "浏览器收藏夹";
        Description = "在浏览器收藏夹中搜索";
        _searchNode = new BrowserDatabaseSearchNode($"{id}-results");
    }

    public override Guid MioType => WebSearchConst.BookmarkScopeType;
    public override IReadOnlyList<string> SearchCommands { get; } = ["bookmark-web", "favorites-web"];

    protected override IEnumerable<ISearchableFeature> GetSearchableFeatures()
    {
        yield return _searchNode;
    }
}
