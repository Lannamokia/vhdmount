using Xunit;

namespace VHDMounter.Tests
{
    public class VersionComparisonTests
    {
        [Theory]
        [InlineData("1.10.0", "1.9.0", 1)]
        [InlineData("1.9.0", "1.10.0", -1)]
        [InlineData("2026.05.02.1", "2026.5.2.0", 1)]
        [InlineData("1.0.0", "1.0", 0)]
        public void Compare_UsesNumericVersionOrdering(string left, string right, int expectedSign)
        {
            var actual = VersionComparison.Compare(left, right);
            Assert.Equal(expectedSign, actual == 0 ? 0 : actual > 0 ? 1 : -1);
        }
    }
}
