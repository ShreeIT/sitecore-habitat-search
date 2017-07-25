using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Sitecore.Feature.Exhibitions.Index
{
	using System.Runtime.Serialization;
	using Sitecore.ContentSearch;
	using Sitecore.ContentSearch.SearchTypes;
	using Sitecore.Foundation.Indexing.Models;

	public class ExhibitionSearchResultItem: IndexedItem
	{
		[IndexField("exhibitionstartdate")]
		[DataMember]
		public virtual DateTime StartDate { get; set; }

		[IndexField("exhibitionenddate")]
		[DataMember]
		public virtual DateTime EndDate { get; set; }


	}
}