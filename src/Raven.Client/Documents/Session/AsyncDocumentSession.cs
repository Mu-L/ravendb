//-----------------------------------------------------------------------
// <copyright file="AsyncDocumentSession.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Commands.MultiGet;
using Raven.Client.Documents.Identity;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Queries;
using Raven.Client.Documents.Queries.Facets;
using Raven.Client.Documents.Session.Operations;
using Raven.Client.Documents.Session.Operations.Lazy;
using Raven.Client.Extensions;
using Raven.Client.Http;
using Raven.Client.Json.Converters;
using Sparrow.Json;

namespace Raven.Client.Documents.Session
{
    /// <summary>
    /// Implementation for async document session 
    /// </summary>
    public partial class AsyncDocumentSession : InMemoryDocumentSessionOperations, IAsyncDocumentSessionImpl, IAsyncAdvancedSessionOperations, IDocumentQueryGenerator
    {
        private AsyncDocumentKeyGeneration _asyncDocumentKeyGeneration;
        private OperationExecuter _operations;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncDocumentSession"/> class.
        /// </summary>
        public AsyncDocumentSession(string dbName, DocumentStore documentStore, RequestExecuter requestExecuter, Guid id)
            : base(dbName, documentStore, requestExecuter, id)
        {
            GenerateDocumentKeysOnStore = false;
        }

        public string GetDocumentUrl(object entity)
        {
            throw new NotImplementedException();
        }

        public async Task RefreshAsync<T>(T entity, CancellationToken token = default(CancellationToken))
        {
            DocumentInfo documentInfo;
            if (DocumentsByEntity.TryGetValue(entity, out documentInfo) == false)
                throw new InvalidOperationException("Cannot refresh a transient instance");
            IncrementRequestCount();

            var command = new GetDocumentCommand
            {
                Ids = new[] { documentInfo.Id },
                Context = Context
            };
            await RequestExecuter.ExecuteAsync(command, Context, token);

            RefreshInternal(entity, command, documentInfo);
        }

        public Task<Operation> DeleteByIndexAsync<T, TIndexCreator>(Expression<Func<T, bool>> expression, CancellationToken token = default(CancellationToken)) where TIndexCreator : AbstractIndexCreationTask, new()
        {
            var indexCreator = new TIndexCreator();
            return DeleteByIndexAsync(indexCreator.IndexName, expression, token);
        }

        public Task<Operation> DeleteByIndexAsync<T>(string indexName, Expression<Func<T, bool>> expression, CancellationToken token = default(CancellationToken))
        {
            var query = Query<T>(indexName).Where(expression);
            var indexQuery = new IndexQuery(Conventions)
            {
                Query = query.ToString()
            };
            if (_operations == null)
                _operations = new OperationExecuter(_documentStore, _requestExecuter, Context);

            return _operations.SendAsync(new DeleteByIndexOperation(indexName, indexQuery), token);
        }

        /// <summary>
        /// Get the accessor for advanced operations
        /// </summary>
        /// <remarks>
        /// Those operations are rarely needed, and have been moved to a separate
        /// property to avoid cluttering the API
        /// </remarks>
        public IAsyncAdvancedSessionOperations Advanced => this;

        protected override string GenerateKey(object entity)
        {
            throw new NotSupportedException("Async session cannot generate keys synchronously");
        }

        protected override void RememberEntityForDocumentKeyGeneration(object entity)
        {
            EnsureAsyncDocumentKeyGeneration();
            _asyncDocumentKeyGeneration.Add(entity);
        }

        private void EnsureAsyncDocumentKeyGeneration()
        {
            if (_asyncDocumentKeyGeneration != null) return;
            _asyncDocumentKeyGeneration = new AsyncDocumentKeyGeneration(this, DocumentsByEntity.TryGetValue,
                (key, entity, metadata) => key);
        }

        protected override Task<string> GenerateKeyAsync(object entity)
        {
            return Conventions.GenerateDocumentIdAsync(DatabaseName, entity);
        }

        public IAsyncEagerSessionOperations Eagerly => this;

        public IAsyncLazySessionOperations Lazily => this;

        /// <summary>
        /// Begins the async save changes operation
        /// </summary>
        /// <returns></returns>
        public async Task SaveChangesAsync(CancellationToken token = default(CancellationToken))
        {
            if (_asyncDocumentKeyGeneration != null)
            {
                await _asyncDocumentKeyGeneration.GenerateDocumentKeysForSaveChanges().WithCancellation(token).ConfigureAwait(false);
            }

            var saveChangesOperation = new BatchOperation(this);

            using (var command = saveChangesOperation.CreateRequest())
            {
                if (command == null)
                    return;

                await RequestExecuter.ExecuteAsync(command, Context, token);
                saveChangesOperation.SetResult(command.Result);
            }
        }
    }
}
