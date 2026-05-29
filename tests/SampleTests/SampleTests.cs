using Xunit;

namespace Testhide.Sample
{
    public class CalcTests
    {
        [Fact]
        [Trait("docstr", "Addition of two integers works.")]
        public void Add_Works()
        {
            Assert.Equal(4, 2 + 2);
        }

        [Fact]
        public void Add_Fails()
        {
            Assert.Equal(5, 2 + 2);
        }

        [Fact(Skip = "feature not ready")]
        public void Pending()
        {
            Assert.True(false);
        }

        [Theory]
        [InlineData(1, 1, 2)]
        [InlineData(2, 3, 5)]
        public void Add_Theory(int a, int b, int expected)
        {
            Assert.Equal(expected, a + b);
        }
    }
}
