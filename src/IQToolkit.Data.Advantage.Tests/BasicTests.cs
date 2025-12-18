using Xunit;
using IQToolkit.Data.Advantage;

namespace IQToolkit.Data.Advantage.Tests
{
    public class BasicTests
    {
        [Fact]
        public void CanReferenceAdvantageProvider()
        {
            // Just checking if we can reference the type
            var type = typeof(AdvantageQueryProvider);
            Assert.NotNull(type);
        }
    }
}
