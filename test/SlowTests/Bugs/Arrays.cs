using FastTests;
using Xunit;

namespace SlowTests.Bugs
{
    public class Arrays : RavenTestBase
    {
        [Fact]
        public void CanRetrieveMultiDimensionalArray()
        {
            using (var store = GetDocumentStore())
            {
                var arrayValue = new double[,] 
                {
                    { 1, 2 }, 
                    { 3, 4 }, 
                    { 5, 6 } 
                };

                using (var session = store.OpenSession())
                {
                    session.Store(new ArrayHolder
                        {
                            ArrayValue = arrayValue
                        });

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var arrayHolder = session.Load<ArrayHolder>("arrayholders/1");

                    Assert.Equal(arrayValue, arrayHolder.ArrayValue);
                }
            }
        }

        private class ArrayHolder
        {
            public double[,] ArrayValue { get; set; }
        }
    }
}
