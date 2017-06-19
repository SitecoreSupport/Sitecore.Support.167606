namespace Sitecore.Support.Cloud.ContentSearch
{
    using System.Reflection;
    using Sitecore.ContentSearch;
    using Sitecore.ContentSearch.Maintenance;
    using Sitecore.ContentSearch.Security;
    public class CloudSearchProviderIndex : Sitecore.Cloud.ContentSearch.CloudSearchProviderIndex
    {
        public CloudSearchProviderIndex(string name, string totalParallelServices, IIndexPropertyStore propertyStore, string @group) : base(name, totalParallelServices, propertyStore, @group)
        {
        }

        public override IProviderSearchContext CreateSearchContext(SearchSecurityOptions options = SearchSecurityOptions.EnableSecurityCheck)
        {
            if (EnsureInitializedMi != null)
            {
                EnsureInitializedMi.Invoke(this, new object[] { });
            }
            return new Sitecore.Support.Cloud.ContentSearch.CloudSearchSearchContext(this, options);
        }

        private static readonly MethodInfo EnsureInitializedMi;
        static CloudSearchProviderIndex()
        {
            EnsureInitializedMi =
                typeof(Sitecore.Cloud.ContentSearch.CloudSearchProviderIndex).GetMethod("EnsureInitialized",
                    BindingFlags.Instance | BindingFlags.NonPublic);
        }
    }
}