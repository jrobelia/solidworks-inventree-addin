using NUnit.Framework;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class AssemblyVersionInfoTests
    {
        [Test]
        public void Version_WhenReadFromAssembly_IsNotNullOrEmpty()
        {
            var versionInfo = CreateVersionInfo();

            Assert.That(versionInfo.Version, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Version_WhenReadFromAssembly_MatchesAssemblyVersion()
        {
            var versionInfo = CreateVersionInfo();
            var expected = typeof(AssemblyVersionInfo).Assembly.GetName().Version!.ToString();

            Assert.That(versionInfo.Version, Is.EqualTo(expected));
        }

        private static AssemblyVersionInfo CreateVersionInfo() =>
            new AssemblyVersionInfo();
    }
}
