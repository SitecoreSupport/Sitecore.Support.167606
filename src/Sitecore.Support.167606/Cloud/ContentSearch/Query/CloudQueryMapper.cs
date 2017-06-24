using System.Linq;
using System.Reflection;

namespace Sitecore.Support.Cloud.ContentSearch.Query
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using Sitecore.Cloud.ContentSearch.Query;
    using Sitecore.ContentSearch.Linq.Common;
    using Sitecore.ContentSearch.Linq.Helpers;
    using Sitecore.ContentSearch.Linq.Nodes;
    using Sitecore.ContentSearch.Linq.Parsing;
    public class CloudQueryMapper : Sitecore.Cloud.ContentSearch.Query.CloudQueryMapper
    {
        public CloudQueryMapper(CloudIndexParameters parameters) : base(parameters)
        {
        }

        public CloudQueryMapper(FieldNameTranslator translator, Type indexedFieldType, IIndexValueFormatter formatter) : base(translator, indexedFieldType, formatter)
        {
        }

        protected override string HandleEqual(EqualNode node)
        {
            if (node.LeftNode is ConstantNode && node.RightNode is ConstantNode)
            {
                string expression = ((ConstantNode) node.LeftNode).Value.Equals(((ConstantNode) node.RightNode).Value)
                    ? "*"
                    : "This_Is_Equal_ConstNode_Return_Nothing";
                return "&search=" + expression;
            }

            FieldNode fieldNode = QueryHelper.GetFieldNode(node);
            ConstantNode valueNode = QueryHelper.GetValueNode(node, fieldNode.FieldType);

            string indexFieldName = this.FieldNameTranslator.GetIndexFieldName(fieldNode.FieldKey, this.IndexedFieldType);
            object obj = base.ValueFormatter.FormatValueForIndexStorage(valueNode.Value, indexFieldName);

            if (fieldNode.FieldType == typeof(List<string>))
            {
                return
                    (string)
                    cloudWildCardSearchMethodInfo.Invoke(this, new object[] {fieldNode.FieldKey, valueNode.Value});
            }

            string result = string.Empty;

            if (obj is string)
            {
                result = string.Format("&search=({0}:'{1}')", indexFieldName, obj);
            }
            else
            {
                result =
                    (string) constructFilterExpressionMethodInfo.Invoke(null, new object[] {"eq", indexFieldName, obj});
            }

            
            if (fieldNode.FieldType == typeof(string[]) || fieldNode.FieldType == typeof(List<string>) ||
                fieldNode.FieldType == typeof(IList<string>) || fieldNode.FieldType == typeof(ICollection<string>) ||
                fieldNode.FieldType == typeof(IEnumerable<string>) ||
                fieldNode.FieldType == typeof(ReadOnlyCollection<string>))
            {
                result = string.Format("&$filter=({0}/any(t:t eq '{1}'))", indexFieldName, obj);
            }
            else if (fieldNode.FieldType == typeof(List<Guid>) || fieldNode.FieldType == typeof(Guid[]) ||
                     fieldNode.FieldType == typeof(IList<Guid>) || fieldNode.FieldType == typeof(ICollection<Guid>) ||
                     fieldNode.FieldType == typeof(IEnumerable<Guid>) ||
                     fieldNode.FieldType == typeof(ReadOnlyCollection<Guid>))
            {
                obj = valueNode.Value.ToString();
                result = string.Format("&$filter=({0}/any(t:t eq '{1}'))", indexFieldName, obj);
            }
            return result;
        }

        private static readonly FieldInfo wildcardSearchInfoFieldInfo;
        private static readonly MethodInfo cloudWildCardSearchMethodInfo;
        private static readonly MethodInfo constructFilterExpressionMethodInfo;

        static CloudQueryMapper()
        {
            cloudWildCardSearchMethodInfo =
                typeof(Sitecore.Cloud.ContentSearch.Query.CloudQueryMapper).GetMethod("CloudWildCardSearch",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            constructFilterExpressionMethodInfo =
                typeof(Sitecore.Cloud.ContentSearch.Query.CloudQueryMapper).GetMethod("ConstructFilterExpression",
                    BindingFlags.Static | BindingFlags.NonPublic);
            wildcardSearchInfoFieldInfo =
                typeof(Sitecore.Cloud.ContentSearch.Query.CloudQueryMapper).GetField("wildcardSearchInfo",
                    BindingFlags.Instance | BindingFlags.NonPublic);          
        }
        protected override string HandleOr(OrNode node, CloudQueryMapper.CloudQueryMapperState mappingState)
        {
            string query = this.HandleCloudQuery(node.LeftNode, mappingState);
            string query2 = this.HandleCloudQuery(node.RightNode, mappingState);
            return CloudQueryMapper.ConstructFilterExpressionWithOperands(query, query2, "OR");
        }
        protected override string HandleAnd(AndNode node, CloudQueryMapper.CloudQueryMapperState mappingState)
        {
            string query = this.HandleCloudQuery(node.LeftNode, mappingState);
            string query2 = this.HandleCloudQuery(node.RightNode, mappingState);
            return CloudQueryMapper.ConstructFilterExpressionWithOperands(query, query2, "AND");
        }

        protected override string HandleWhere(WhereNode node, CloudQueryMapperState mappingState)
        {
            string str = this.HandleCloudQuery(node.PredicateNode, mappingState);
            string str2 = this.HandleCloudQuery(node.SourceNode, mappingState);
            return ConstructFilterExpressionWithOperands(str, str2, "AND");
        }

        public override CloudQuery MapQuery(IndexQuery query)
        {
            CloudQueryMapper.CloudQueryMapperState cloudQueryMapperState = new CloudQueryMapper.CloudQueryMapperState(new IExecutionContext[0]);
            string text = this.HandleCloudQuery(query.RootNode, cloudQueryMapperState);
            if (text == null)
            {
                text = string.Empty;
            }
            if (string.IsNullOrEmpty(text) && cloudQueryMapperState.AdditionalQueryMethods.Count == 0 && cloudQueryMapperState.FacetQueries.Count == 0)
            {
                text = "&search=*";
            }
            if (text.Contains("isWildcard") || (!string.IsNullOrEmpty(cloudQueryMapperState.FilterQuery) && cloudQueryMapperState.FilterQuery.Contains("isWildcard")))
            {
                string text2 = MyConstructWildcardExpression();
                text = text.Replace("isWildcard", "");
                if (!text.Contains("&search="))
                {
                    text = "&search=" + text2 + text;
                }
                else
                {
                    text = text.Replace("&search=", "&search=(" + text2 + ") AND ");
                }
                if (!string.IsNullOrEmpty(cloudQueryMapperState.FilterQuery))
                {
                    cloudQueryMapperState.FilterQuery = cloudQueryMapperState.FilterQuery.Replace("isWildcard", "");
                }
            }
            if (!string.IsNullOrEmpty(cloudQueryMapperState.FilterQuery))
            {
                text = CloudQueryMapper.ConstructFilterExpressionWithOperands(text, cloudQueryMapperState.FilterQuery, "AND");
            }
            if (!text.Contains("queryType=") && text.Contains("search="))
            {
                text = "&queryType=full" + text;
            }
            return new CloudQuery(text, cloudQueryMapperState.AdditionalQueryMethods, cloudQueryMapperState.FacetQueries);
        }

        private string MyConstructWildcardExpression()
        {
            var wildcardSearchInfo = (IDictionary<string, string>) wildcardSearchInfoFieldInfo.GetValue(this);
            if (wildcardSearchInfo == null || wildcardSearchInfo.Count == 0)
            {
                return string.Empty;
            }
            
            return string.Join(" OR ", wildcardSearchInfo.Select(kvp => kvp.Key + ":" + kvp.Value));
        }

        private static string ConstructFilterExpressionWithOperands(string query1, string query2, string operand)
        {
            string text = string.Empty;
            if (!string.IsNullOrEmpty(query1) && !string.IsNullOrEmpty(query2))
            {
                if (query1.Contains(query2.Replace("&$filter=", "")))
                {
                    text = query1;
                }
                else if (query2.Contains(query1.Replace("&$filter=", "")))
                {
                    text = query2;
                }
                else if (query1 == "isWildcard" || query2 == "isWildcard" || (query1.Contains("&search=") && query2.Contains("&$filter=")) || (query1.Contains("&$filter=") && query2.Contains("&search=")))
                {
                    text = string.Format("{0}{1}", query1, query2);
                }
                else if (query1.Contains("&search=") && query2.Contains("&search="))
                {
                    text = string.Format("&search=(({0}){1}({2}))", query1.Replace("&search=", ""), operand.ToUpperInvariant(), query2.Replace("&search=", ""));
                }
                else
                {
                    text = string.Format("{0} {1} {2}", query1, operand.ToLowerInvariant(), query2.Replace("&$filter=", ""));
                }
            }
            else if (string.IsNullOrEmpty(query1) && !string.IsNullOrEmpty(query2))
            {
                text = query2;
            }
            else if (!string.IsNullOrEmpty(query1) && string.IsNullOrEmpty(query2))
            {
                text = query1;
            }
            if (text.Contains("&$filter="))
            {
                text = text.Replace("&$filter=", "&$filter=(") + ")";
            }
            return text;
        }
    }
}