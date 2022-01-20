﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Foundatio.Parsers.ElasticQueries.Visitors;
using Foundatio.Parsers.LuceneQueries.Extensions;
using Foundatio.Parsers.LuceneQueries.Nodes;

namespace Foundatio.Parsers.LuceneQueries.Visitors {
    public class ElasticFieldResolverVisitor : ChainableQueryVisitor {
        public override async Task VisitAsync(GroupNode node, IQueryVisitorContext context) {
            await ResolveFieldAsync(node, context).ConfigureAwait(false);
            await base.VisitAsync(node, context).ConfigureAwait(false);
        }

        public override Task VisitAsync(TermNode node, IQueryVisitorContext context) {
            return ResolveFieldAsync(node, context);
        }

        public override Task VisitAsync(TermRangeNode node, IQueryVisitorContext context) {
            return ResolveFieldAsync(node, context);
        }

        public override Task VisitAsync(ExistsNode node, IQueryVisitorContext context) {
            return ResolveFieldAsync(node, context);
        }

        public override Task VisitAsync(MissingNode node, IQueryVisitorContext context) {
            return ResolveFieldAsync(node, context);
        }

        private async Task ResolveFieldAsync(IFieldQueryNode node, IQueryVisitorContext context) {
            if (node.Parent == null || node.Field == null)
                return;

            if (context is not IElasticQueryVisitorContext elasticContext)
                throw new ArgumentException("Context must be of type IElasticQueryVisitorContext", nameof(context));

            // try to find Elasticsearch mapping
            // TODO: Need to test how runtime mappings defined on the server are handled
            // TODO: Mark fields resolved so that we don't try to do lookups multiple times
            var resolvedField = elasticContext.MappingResolver.GetMapping(node.Field);
            if (resolvedField.Found) {
                if (!resolvedField.FullPath.Equals(node.Field, StringComparison.Ordinal)) {
                    node.SetOriginalField(node.Field);
                    node.Field = resolvedField.FullPath;
                }

                return;
            }

            // try to resolve from the list of runtime fields that are defined for this query
            if (elasticContext.RuntimeFields != null && elasticContext.RuntimeFields.Count > 0) {
                var resolvedRuntimeField = elasticContext.RuntimeFields.FirstOrDefault(f => f.Name.Equals(node.Field, StringComparison.OrdinalIgnoreCase));
                if (resolvedRuntimeField != null) {
                    if (!resolvedRuntimeField.Name.Equals(node.Field, StringComparison.Ordinal)) {
                        node.SetOriginalField(node.Field);
                        node.Field = resolvedRuntimeField.Name;
                    }

                    return;
                }
            }

            // try to use the runtime field resolver to dynamically discover a new runtime field and, if so, add it to the list of runtime fields
            if ((elasticContext.EnableRuntimeFieldResolver.HasValue == false || elasticContext.EnableRuntimeFieldResolver.Value) && elasticContext.RuntimeFieldResolver != null) {
                var newRuntimeField = await elasticContext.RuntimeFieldResolver(node.Field);
                if (newRuntimeField != null) {
                    elasticContext.RuntimeFields.Add(newRuntimeField);
                    if (!newRuntimeField.Name.Equals(node.Field, StringComparison.Ordinal)) {
                        node.SetOriginalField(node.Field);
                        node.Field = newRuntimeField.Name;
                    }
                }
            }
        }

        public override async Task<IQueryNode> AcceptAsync(IQueryNode node, IQueryVisitorContext context) {
            await node.AcceptAsync(this, context).ConfigureAwait(false);
            return node;
        }

        public static Task<IQueryNode> RunAsync(IQueryNode node, RuntimeFieldResolver resolver, IElasticQueryVisitorContext context = null) {
            return new FieldResolverQueryVisitor().AcceptAsync(node, context ?? new ElasticQueryVisitorContext { RuntimeFieldResolver = resolver });
        }

        public static IQueryNode Run(IQueryNode node, RuntimeFieldResolver resolver, IElasticQueryVisitorContext context = null) {
            return RunAsync(node, resolver, context).GetAwaiter().GetResult();
        }
    }
}