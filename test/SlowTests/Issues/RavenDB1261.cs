// -----------------------------------------------------------------------
//  <copyright file="QueryResultsStreaming.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System.Linq;
using System.Threading;
using FastTests;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Indexes;
using Sparrow.Json;
using Xunit;

namespace SlowTests.Issues
{
    public class RavenDB1261 : RavenTestBase
    {
        public class Student
        {
            public string Email { get; set; }
        }

        public class StudentIndex : AbstractIndexCreationTask<Student>
        {
            public StudentIndex()
            {
                Map = students => from s in students
                                  select new
                                  {
                                      s.Email
                                  };
            }
        }

        [Fact]
        public void Run()
        {
            using (var store = GetDocumentStore())
            {
                new StudentIndex().Execute(store);

                using (var session = store.OpenSession())
                {
                    session.Store(new Student { Email = "support@hibernatingrhinos.com" });
                    session.SaveChanges();
                }

                JsonOperationContext jsonOperationContext;
                var requestExecuter = store.GetRequestExecuter(store.DefaultDatabase);
                requestExecuter.ContextPool.AllocateOperationContext(out jsonOperationContext);
                var getStatsCommand = new GetStatisticsCommand();
                if (getStatsCommand != null)
                {
                    requestExecuter.Execute(getStatsCommand, jsonOperationContext);
                }
                var databaseStatistics = getStatsCommand.Result;
                while (databaseStatistics.StaleIndexes.Any())
                {
                    Thread.Sleep(10);
                    requestExecuter.Execute(getStatsCommand, jsonOperationContext);
                    databaseStatistics = getStatsCommand.Result;
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Student, StudentIndex>();

                    var stream = session.Advanced.Stream(query);

                    stream.MoveNext();

                    Assert.NotNull(stream.Current.Key);
                }
            }
        }
}
}
