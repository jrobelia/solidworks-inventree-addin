using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace SwInventreeAddin.Tests
{
    // Brand metadata on the shipped DLL — read off the compiled add-in
    // assembly so the tests certify the file properties an engineer sees,
    // never a private constant. The COM GUID pin guards the registration
    // identity the rename must not touch.
    [TestFixture]
    public class AssemblyMetadataTests
    {
        private static readonly Assembly AddinAssembly = typeof(AssemblyVersionInfo).Assembly;

        [Test]
        public void Product_WhenReadFromAssembly_IsFullProductName()
        {
            Assert.That(AddinAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product,
                        Is.EqualTo("InvenTree Link for SOLIDWORKS"));
        }

        [Test]
        public void Title_WhenReadFromAssembly_IsFullProductName()
        {
            Assert.That(AddinAssembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title,
                        Is.EqualTo("InvenTree Link for SOLIDWORKS"));
        }

        [Test]
        public void Description_WhenReadFromAssembly_IsTheTagline()
        {
            Assert.That(AddinAssembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description,
                        Is.EqualTo("Exchange part and assembly data between SOLIDWORKS and InvenTree."));
        }

        [Test]
        public void Company_WhenReadFromAssembly_IsThePublisher()
        {
            Assert.That(AddinAssembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company,
                        Is.EqualTo("Jon Robelia"));
        }

        [Test]
        public void AssemblyName_RemainsTheTechnicalNameSwInventreeAddin()
        {
            Assert.That(AddinAssembly.GetName().Name, Is.EqualTo("SwInventreeAddin"));
            Assert.That(Path.GetFileName(AddinAssembly.Location),
                        Is.EqualTo("SwInventreeAddin.dll"));
        }

        [Test]
        public void ComGuid_OnSwAddin_RemainsTheRegisteredAddinGuid()
        {
            var guid = typeof(AddIn.SwAddin)
                .GetCustomAttribute<GuidAttribute>()?.Value;

            Assert.That(guid, Is.EqualTo("7C3E9A2F-4B61-4D8F-A2E5-1F6C9B3D8E47"));
        }
    }
}
