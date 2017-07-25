namespace Sitecore.Foundation.Indexing.Services
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Linq.Expressions;
	using System.Web.Mvc;
	using Sitecore.ContentSearch;
	using Sitecore.ContentSearch.Linq;
	using Sitecore.ContentSearch.Linq.Utilities;
	using Sitecore.ContentSearch.SearchTypes;
	using Sitecore.ContentSearch.Utilities;
	using Sitecore.Data;
	using Sitecore.Diagnostics;
	using Sitecore.Foundation.Indexing.Models;
	using Sitecore.Foundation.Indexing.Repositories;
	using Sitecore.Mvc.Common;

	public class SearchService : SearchService<SearchResultItem>
	{
		public SearchService(ISearchSettings settings): base(settings)
		{
		}

		public virtual ISearchResults Search(IQuery query)
		{
			using (var context = this.SearchIndexResolver.GetIndex(this.ContextItem).CreateSearchContext())
			{
				var queryable = this.CreateAndInitializeQuery(context);

				queryable = this.AddContentPredicates(queryable, query);
				queryable = this.AddFacets(queryable, query);
				queryable = this.AddPaging(queryable, query);
				var results = queryable.Cast<SearchResultItem>().GetResults();
				return this.SearchResultsFactory.Create(results, query);
			}
		}

	}

	public class SearchService<T1> where T1 : SearchResultItem
	{
		public SearchService(ISearchSettings settings)
		{
			this.Settings = settings;
			this.SearchIndexResolver = DependencyResolver.Current.GetService<SearchIndexResolver>();
			this.SearchResultsFactory = DependencyResolver.Current.GetService<SearchResultsFactory>();
		}

		public ISearchSettings Settings { get; set; }

		public SitecoreIndexableItem ContextItem
		{
			get
			{
				var contextItem = this.Settings.Root ?? Context.Item;
				Assert.IsNotNull(contextItem, "Could not determine a context item for the search");
				return contextItem;
			}
		}

		internal SearchIndexResolver SearchIndexResolver { get; }
		internal SearchResultsFactory SearchResultsFactory { get; }

		public IEnumerable<IQueryRoot> QueryRoots => IndexingProviderRepository.QueryRootProviders.Union(new[] {this.Settings});

		internal IQueryable<T1> AddPaging(IQueryable<T1> queryable, IQuery query)
		{
			return queryable.Page(query.Page < 0 ? 0 : query.Page, query.NoOfResults <= 0 ? 10 : query.NoOfResults);
		}
		
		public virtual ISearchResults FindAll()
		{
			return this.FindAll(0, 0);
		}

		public virtual ISearchResults FindAll(int skip, int take)
		{
			using (var context = ContentSearchManager.GetIndex(this.ContextItem).CreateSearchContext())
			{
				var queryable = this.CreateAndInitializeQuery(context);

				if (skip > 0)
				{
					queryable = queryable.Skip(skip);
				}
				if (take > 0)
				{
					queryable = queryable.Take(take);
				}

				var results = queryable.GetResults<SearchResultItem>();
				return this.SearchResultsFactory.Create(results, null);
			}
		}

		public virtual IQueryable<T1> CreateAndInitializeQuery(IProviderSearchContext context)
		{
			var queryable = context.GetQueryable<T1>();
			queryable = this.InitializeQuery(queryable);
			return queryable;
		}

		private IQueryable<T1> InitializeQuery(IQueryable<T1> queryable)
		{
			queryable = this.SetQueryRoots(queryable);
			queryable = this.FilterOnLanguage(queryable);
			queryable = this.FilterOnVersion(queryable);
			if (this.Settings.MustHaveFormatter)
			{
				queryable = this.FilterOnHasSearchResultFormatter(queryable);
			}
			if (this.Settings.Templates != null && this.Settings.Templates.Any())
			{
				queryable = queryable.Cast<IndexedItem>().Where(this.GetTemplatePredicates(this.Settings.Templates)).Cast<T1>();
			}
			else
			{
				queryable = this.FilterOnItemsMarkedAsIndexable(queryable);
			}
			return queryable;
		}

		private IQueryable<T1> FilterOnHasSearchResultFormatter(IQueryable queryable)
		{
			var query = queryable.Cast<IndexedItem>().Where(i => i.HasSearchResultFormatter);
			return query.Cast<T1>();
		}

		private IQueryable<T1> FilterOnItemsMarkedAsIndexable(IQueryable<T1> queryable)
		{
			var indexedItemPredicate = this.GetPredicateForItemDerivesFromIndexedItem();
			var contentTemplatePredicates = this.GetPredicatesForContentTemplates();
			var query = queryable.Cast<IndexedItem>().Where(indexedItemPredicate.And(contentTemplatePredicates));
			return query.Cast<T1>();
		}

		private Expression<Func<IndexedItem, bool>> GetPredicatesForContentTemplates()
		{
			var contentTemplatePredicates = PredicateBuilder.False<IndexedItem>();
			foreach (var provider in IndexingProviderRepository.QueryPredicateProviders)
			{
				contentTemplatePredicates = contentTemplatePredicates.Or(this.GetTemplatePredicates(provider.SupportedTemplates));
			}
			return contentTemplatePredicates;
		}

		private Expression<Func<IndexedItem, bool>> GetPredicateForItemDerivesFromIndexedItem()
		{
			var notIndexedItem = PredicateBuilder.Create<IndexedItem>(i => !i.AllTemplates.Contains(IdHelper.NormalizeGuid(Templates.IndexedItem.ID)));
			var indexedItemWithShowInResults = PredicateBuilder.And<IndexedItem>(i => i.AllTemplates.Contains(IdHelper.NormalizeGuid(Templates.IndexedItem.ID)), i => i.ShowInSearchResults);

			return notIndexedItem.Or(indexedItemWithShowInResults);
		}

		private Expression<Func<IndexedItem, bool>> GetTemplatePredicates(IEnumerable<ID> templates)
		{
			var expression = PredicateBuilder.False<IndexedItem>();
			foreach (var template in templates)
			{
				expression = expression.Or(i => i.AllTemplates.Contains(IdHelper.NormalizeGuid(template)));
			}
			return expression;
		}

		internal IQueryable<T1> AddFacets(IQueryable<T1> queryable, IQuery query)
		{
			var facets = GetFacetsFromProviders();

			var addedFacetPredicate = false;
			var facetPredicate = PredicateBuilder.True<T1>();
			foreach (var facet in facets)
			{
				if (string.IsNullOrEmpty(facet.FieldName))
					continue;

				if (query.Facets != null && query.Facets.ContainsKey(facet.FieldName))
				{
					var facetValues = query.Facets[facet.FieldName];

					var facetValuePredicate = PredicateBuilder.False<T1>();
					foreach (var facetValue in facetValues)
					{
						if (facetValue == null)
							continue;
						facetValuePredicate = facetValuePredicate.Or(item => item[facet.FieldName] == facetValue);
					}
					facetPredicate = facetPredicate.And(facetValuePredicate);
					addedFacetPredicate = true;
				}
				queryable = queryable.FacetOn(item => item[facet.FieldName]);
			}
			if (addedFacetPredicate)
				queryable = queryable.Where(facetPredicate);

			return queryable;
		}

		private static IEnumerable<IQueryFacet> GetFacetsFromProviders()
		{
			return IndexingProviderRepository.QueryFacetProviders.SelectMany(provider => provider.GetFacets()).Distinct(new GenericEqualityComparer<IQueryFacet>((facet, queryFacet) => facet.FieldName == queryFacet.FieldName, facet => facet.FieldName.GetHashCode()));
		}

		private IQueryable<T1> FilterOnLanguage(IQueryable<T1> queryable)
		{
			queryable = queryable.Filter(item => item.Language == Context.Language.Name);
			return queryable;
		}

		private IQueryable<T1> FilterOnVersion(IQueryable<T1> queryable)
		{
			queryable = queryable.Cast<IndexedItem>().Filter(item => item.IsLatestVersion).Cast<T1>();
			return queryable;
		}

		private IQueryable<T1> SetQueryRoots(IQueryable<T1> queryable)
		{
			var rootPredicates = PredicateBuilder.False<T1>();

			foreach (var provider in this.QueryRoots)
			{
				if (provider.Root == null)
				{
					continue;
				}
				rootPredicates = rootPredicates.Or(item => item.Path.StartsWith(provider.Root.Paths.FullPath));
			}

			return queryable.Where(rootPredicates);
		}

		internal IQueryable<T1> AddContentPredicates(IQueryable<T1> queryable, IQuery query)
		{
			var contentPredicates = PredicateBuilder.False<SearchResultItem>();
			foreach (var provider in IndexingProviderRepository.QueryPredicateProviders)
			{
				contentPredicates = contentPredicates.Or(provider.GetQueryPredicate(query));
			}
			return queryable.Where(contentPredicates).Cast<T1>();
		}

	}
}