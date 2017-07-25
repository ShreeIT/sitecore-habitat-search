namespace Sitecore.Feature.Exhibitions.Repositories
{
	using System;
	using System.Linq;
	using Sitecore.ContentSearch;
	using Sitecore.ContentSearch.SearchTypes;
	using Sitecore.Foundation.Indexing.Models;
	using Sitecore.Foundation.Indexing.Services;
	using Sitecore.Feature.Exhibitions.Index;


	public class ExhibitionSearchService : SearchService<ExhibitionSearchResultItem>
	{

		public ExhibitionSearchService(ISearchSettings settings) : base(settings)
		{
		}


		public override IQueryable<ExhibitionSearchResultItem> CreateAndInitializeQuery(IProviderSearchContext context)
		{
			var queryable = base.CreateAndInitializeQuery(context);
			queryable = queryable.Where(x => x.StartDate > DateTime.Now);
			return queryable;
		}

	}
}