using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class CreatePartViewModelTests
    {
        private StubInventreeClient         _client;
        private StubDocumentPropertyService _propertyService;
        private const string DefaultName = "10K Resistor";

        [SetUp]
        public void SetUp()
        {
            _client          = new StubInventreeClient();
            _propertyService = new StubDocumentPropertyService();
        }

        private CreatePartViewModel CreateVm(string name = DefaultName, bool waitForAutoPartNumber = false) =>
            new CreatePartViewModel(_client, _propertyService, name, ipnPollDelayMs: 0, waitForAutoPartNumber: waitForAutoPartNumber);

        private static CategoryNode MakeNode(int pk = 1, string name = "Resistors") =>
            new CategoryNode(new InventreeCategory { Pk = pk, Name = name });

        // ── CreateEnabled gate ───────────────────────────────────────────────

        [Test]
        public void CreateEnabled_NoCategory_IsFalse()
        {
            var vm = CreateVm();
            Assert.That(vm.CreateEnabled, Is.False);
        }

        [Test]
        public void CreateEnabled_EmptyName_IsFalse()
        {
            var vm = CreateVm(string.Empty);
            vm.SelectedCategory = MakeNode();
            Assert.That(vm.CreateEnabled, Is.False);
        }

        [Test]
        public void CreateEnabled_NameAndCategory_IsTrue()
        {
            var vm = CreateVm();
            vm.SelectedCategory = MakeNode();
            Assert.That(vm.CreateEnabled, Is.True);
        }

        [Test]
        public void CreateEnabled_WhileBusy_IsFalse()
        {
            var vm = CreateVm();
            vm.SelectedCategory = MakeNode();
            Assert.That(vm.CreateEnabled, Is.True);

            // Simulate busy by starting a long-running async op via a blocking stub.
            // Easier to verify via the CanCreate logic: set IsBusy through reflection.
            var isBusyField = typeof(CreatePartViewModel)
                .GetField("_isBusy",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
            isBusyField!.SetValue(vm, true);
            // Re-evaluate CreateEnabled by triggering the setter path.
            vm.SelectedCategory = MakeNode();          // re-triggers CanCreate()

            Assert.That(vm.CreateEnabled, Is.False);
        }

        // ── LoadRootCategoriesAsync ──────────────────────────────────────────

        [Test]
        public async Task LoadRootCategoriesAsync_PopulatesRootCategories()
        {
            _client.CategoriesToReturn = new List<InventreeCategory>
            {
                new InventreeCategory { Pk = 7, Name = "Resistors", HasChildren = true  },
                new InventreeCategory { Pk = 8, Name = "Capacitors", HasChildren = false },
            };

            var vm = CreateVm();
            await vm.LoadRootCategoriesAsync();

            Assert.That(vm.RootCategories.Count, Is.EqualTo(2));
            Assert.That(vm.RootCategories[0].Category.Name, Is.EqualTo("Resistors"));
            // HasChildren=true → sentinel child added
            Assert.That(vm.RootCategories[0].Children.Count, Is.EqualTo(1));
            // HasChildren=false → no sentinel
            Assert.That(vm.RootCategories[1].Children.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task LoadRootCategoriesAsync_OnError_SetsStatusText()
        {
            _client.ThrowOnGetCategories = true;
            var vm = CreateVm();
            await vm.LoadRootCategoriesAsync();

            Assert.That(vm.StatusText, Does.Contain("Error"));
            Assert.That(vm.IsBusy, Is.False);
        }

        // ── LoadChildrenAsync ────────────────────────────────────────────────

        [Test]
        public async Task LoadChildrenAsync_HasSentinel_ReplacesWithChildren()
        {
            _client.CategoriesToReturn = new List<InventreeCategory>
            {
                new InventreeCategory { Pk = 10, Name = "SMD" },
            };

            // A node with HasChildren=true starts with one sentinel null child.
            var node = new CategoryNode(new InventreeCategory { Pk = 7, Name = "Resistors", HasChildren = true });
            Assert.That(node.Children.Count, Is.EqualTo(1));

            var vm = CreateVm();
            await vm.LoadChildrenAsync(node);

            Assert.That(node.Children.Count,                   Is.EqualTo(1));
            Assert.That(node.Children[0]!.Category.Name,       Is.EqualTo("SMD"));
            Assert.That(node.IsLoading,                        Is.False);
        }

        [Test]
        public async Task LoadChildrenAsync_AlreadyLoaded_DoesNotCallClient()
        {
            // Node with no sentinel (already loaded / truly empty)
            var node = new CategoryNode(new InventreeCategory { Pk = 7, Name = "Resistors", HasChildren = false });
            Assert.That(node.Children.Count, Is.EqualTo(0));

            var vm = CreateVm();
            await vm.LoadChildrenAsync(node);

            // GetCategoriesAsync should NOT have been called
            Assert.That(_client.LastGetCategoriesParentId, Is.Null);
        }

        // ── CreateAsync ──────────────────────────────────────────────────────

        [Test]
        public async Task CreateAsync_Success_WritesIpnAndNameToDocument()
        {
            const int newPk = 99;
            _client.PkToReturnOnCreate = newPk;
            _client.PartByPkToReturn   = new InventreePart
            {
                Pk   = newPk,
                Ipn  = "R-NEW-001",
                Name = "New Resistor",
            };
            _propertyService.Seed("PartNo",      string.Empty);
            _propertyService.Seed("Description", string.Empty);

            var vm = CreateVm();
            vm.SelectedCategory = MakeNode(pk: 7);
            await vm.CreateAsync();

            Assert.That(_propertyService.GetCustomProperty("PartNo"),      Is.EqualTo("R-NEW-001"));
            Assert.That(_propertyService.GetCustomProperty("Description"), Is.EqualTo("New Resistor"));
        }

        [Test]
        public async Task CreateAsync_Success_RaisesPartCreatedWithPart()
        {
            _client.PkToReturnOnCreate = 99;
            _client.PartByPkToReturn   = new InventreePart { Pk = 99, Ipn = "R-NEW-001", Name = "New Resistor" };

            InventreePart? raisedPart = null;
            var vm = CreateVm();
            vm.PartCreated += (_, part) => raisedPart = part;
            vm.SelectedCategory = MakeNode(pk: 7);

            await vm.CreateAsync();

            Assert.That(raisedPart,     Is.Not.Null);
            Assert.That(raisedPart!.Ipn, Is.EqualTo("R-NEW-001"));
        }

        [Test]
        public async Task CreateAsync_CreateFails_SetsStatusText_NoDocWrite()
        {
            _client.ThrowOnCreate = true;
            _propertyService.Seed("PartNo", "ORIGINAL");

            var vm = CreateVm();
            vm.SelectedCategory = MakeNode(pk: 7);
            await vm.CreateAsync();

            Assert.That(vm.StatusText, Does.Contain("Error"));
            Assert.That(vm.IsBusy,     Is.False);
            // Original value must be unchanged
            Assert.That(_propertyService.GetCustomProperty("PartNo"), Is.EqualTo("ORIGINAL"));
        }

        [Test]
        public async Task CreateAsync_ServerValidationError_StatusTextContainsResponseBody()
        {
            const string errorBody = @"{""ipn"": [""Part with this IPN already exists.""]}";
            _client.ThrowOnCreateException = new HttpRequestException(
                $"InvenTree API returned 400 BadRequest: {errorBody}");

            var vm = CreateVm();
            vm.SelectedCategory = MakeNode(pk: 7);
            vm.IpnEntry         = "DUP-001";
            await vm.CreateAsync();

            Assert.That(vm.StatusText, Does.Contain("Part with this IPN already exists."));
            Assert.That(vm.IsBusy,     Is.False);
        }

        [Test]
        public async Task CreateAsync_RefetchReturnsNull_SetsStatusText_NoDocWrite()
        {
            _client.PkToReturnOnCreate = 99;
            _client.PartByPkToReturn   = null;   // re-fetch fails
            _propertyService.Seed("PartNo", "ORIGINAL");

            var vm = CreateVm();
            vm.SelectedCategory = MakeNode(pk: 7);
            await vm.CreateAsync();

            Assert.That(vm.StatusText, Does.Contain("IPN not yet written"));
            Assert.That(vm.IsBusy,     Is.False);
            Assert.That(_propertyService.GetCustomProperty("PartNo"), Is.EqualTo("ORIGINAL"));
        }

        [Test]
        public async Task CreateAsync_IpnAppearsOnSecondPoll_WritesIpnAndRaisesEvent()
        {
            // First fetch returns no IPN (plugin not yet run); second fetch has it.
            _client.PkToReturnOnCreate = 99;
            _client.QueuePartByPkResponses(
                new InventreePart { Pk = 99, Ipn = string.Empty, Name = "New Resistor" },
                new InventreePart { Pk = 99, Ipn = "R-NEW-001",  Name = "New Resistor" });
            _propertyService.Seed("PartNo",      string.Empty);
            _propertyService.Seed("Description", string.Empty);

            InventreePart? raisedPart = null;
            var vm = CreateVm(waitForAutoPartNumber: true);
            vm.PartCreated += (_, p) => raisedPart = p;
            vm.SelectedCategory = MakeNode(pk: 7);

            await vm.CreateAsync();

            Assert.That(_propertyService.GetCustomProperty("PartNo"), Is.EqualTo("R-NEW-001"),
                "IPN should be written once the poll succeeds");
            Assert.That(raisedPart?.Ipn, Is.EqualTo("R-NEW-001"));
        }

        [Test]
        public async Task CreateAsync_IpnNeverArrives_SetsStatusText_DoesNotWriteIpn()
        {
            // All poll fetches return empty IPN — simulates plugin not installed.
            const int newPk = 99;
            _client.PkToReturnOnCreate = newPk;
            // Seed the queue with enough empty responses to exhaust the 20-attempt poll.
            var emptyParts = new InventreePart[21];
            for (int i = 0; i < emptyParts.Length; i++)
                emptyParts[i] = new InventreePart { Pk = newPk, Ipn = string.Empty, Name = "New Part" };
            _client.QueuePartByPkResponses(emptyParts);
            _propertyService.Seed("PartNo", string.Empty);

            var vm = CreateVm(waitForAutoPartNumber: true);
            vm.SelectedCategory = MakeNode(pk: 7);
            await vm.CreateAsync();

            Assert.That(_propertyService.GetCustomProperty("PartNo"), Is.EqualTo(string.Empty),
                "Should NOT write empty IPN to the document");
            Assert.That(vm.StatusText, Does.Contain("refresh manually"));
            Assert.That(vm.IsBusy, Is.False);
        }

        [Test]
        public async Task CreateAsync_WhileNotEnabled_DoesNothing()
        {
            // No category selected → CreateEnabled=false
            var vm = CreateVm();
            await vm.CreateAsync();

            Assert.That(_client.LastCreateCategoryPk, Is.EqualTo(0));
        }

        [Test]
        public async Task CreateAsync_UserProvidesIpn_PassesIpnToClient()
        {
            _client.PkToReturnOnCreate = 42;
            _client.PartByPkToReturn   = new InventreePart { Pk = 42, Ipn = "FAB-001", Name = "Custom" };
            _propertyService.Seed("PartNo",      string.Empty);
            _propertyService.Seed("Description", string.Empty);

            var vm = CreateVm();
            vm.SelectedCategory = MakeNode();
            vm.IpnEntry         = "FAB-001";
            await vm.CreateAsync();

            Assert.That(_client.LastCreateIpn, Is.EqualTo("FAB-001"));
        }

        // ── WaitForAutoPartNumber toggle ─────────────────────────────────────────

        [Test]
        public async Task CreateAsync_WaitOff_BlankIpn_SkipsPollAndRaisesPartCreated()
        {
            // Toggle off: poll is skipped even when initial re-fetch returns no IPN.
            const int newPk = 55;
            _client.PkToReturnOnCreate = newPk;
            _client.PartByPkToReturn   = new InventreePart { Pk = newPk, Ipn = string.Empty, Name = "IPN-less Part" };
            _propertyService.Seed("Description", string.Empty);

            InventreePart? raisedPart = null;
            var vm = CreateVm(waitForAutoPartNumber: false);
            vm.PartCreated      += (_, p) => raisedPart = p;
            vm.SelectedCategory  = MakeNode();
            await vm.CreateAsync();

            Assert.That(raisedPart,  Is.Not.Null, "PartCreated must fire even with blank IPN");
            Assert.That(vm.IsBusy,   Is.False);
            Assert.That(vm.StatusText, Does.Not.Contain("refresh manually"),
                "refresh-manually message only appears when the poll ran and timed out");
        }

        [Test]
        public async Task CreateAsync_WaitOff_BlankIpn_WritesPkToDocument()
        {
            // After a poll-skipped creation the InvenTree Part PK is written to the SW document.
            const int newPk = 55;
            _client.PkToReturnOnCreate = newPk;
            _client.PartByPkToReturn   = new InventreePart { Pk = newPk, Ipn = string.Empty, Name = "IPN-less Part" };

            var vm = CreateVm(waitForAutoPartNumber: false);
            vm.SelectedCategory = MakeNode();
            await vm.CreateAsync();

            Assert.That(_propertyService.GetCustomProperty("InvenTree PK"), Is.EqualTo(newPk.ToString()));
        }

        [Test]
        public async Task CreateAsync_ManualIpn_WaitOn_ClosesImmediatelyWithoutPoll()
        {
            // When user enters an IPN manually the poll never runs — toggle has no effect.
            const int newPk = 77;
            _client.PkToReturnOnCreate = newPk;
            _client.PartByPkToReturn   = new InventreePart { Pk = newPk, Ipn = "FAB-123", Name = "Manual Part" };
            _propertyService.Seed("PartNo",      string.Empty);
            _propertyService.Seed("Description", string.Empty);

            InventreePart? raisedPart = null;
            var vm = CreateVm(waitForAutoPartNumber: true);
            vm.PartCreated      += (_, p) => raisedPart = p;
            vm.SelectedCategory  = MakeNode();
            vm.IpnEntry          = "FAB-123";
            await vm.CreateAsync();

            Assert.That(_client.LastCreateIpn, Is.EqualTo("FAB-123"));
            Assert.That(raisedPart?.Ipn,        Is.EqualTo("FAB-123"));
            Assert.That(vm.IsBusy,              Is.False);
        }
    }
}
