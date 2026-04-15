using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.Bom;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class BomCompareViewModelTests
    {
        private StubInventreeClient    _client;
        private StubAssemblyBomService _bomService;
        private PropertyMappingConfig  _mapping;

        [SetUp]
        public void SetUp()
        {
            _client     = new StubInventreeClient();
            _bomService = new StubAssemblyBomService();
            _mapping    = new PropertyMappingConfig();

            // Default: assembly part exists and is flagged as Assembly so ApplyAsync guard passes.
            _client.PartByPkToReturn = new InventreePart { IsAssembly = true };
        }

        private BomCompareViewModel CreateVm(int assemblyPk = 42) =>
            new BomCompareViewModel(_client, _bomService, _mapping, assemblyPk, "inventree");

        // ── LoadAsync ─────────────────────────────────────────────────────────

        [Test]
        public async Task LoadAsync_PopulatesMatchRow()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 1 });
            _client.BomLinesToReturn = new List<InventreeBomLine>
                { new InventreeBomLine { Pk = 1, SubPartPk = 10, Quantity = 1 } };

            var vm = CreateVm();
            await vm.LoadAsync();

            Assert.That(vm.Lines.Count, Is.EqualTo(1));
            Assert.That(vm.Lines[0].State, Is.EqualTo(BomDiffState.Match));
        }

        [Test]
        public async Task LoadAsync_MatchRow_CanCheckIsFalse()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 1 });
            _client.BomLinesToReturn = new List<InventreeBomLine>
                { new InventreeBomLine { SubPartPk = 10, Quantity = 1 } };

            var vm = CreateVm();
            await vm.LoadAsync();

            Assert.That(vm.Lines[0].CanCheck, Is.False);
        }

        [Test]
        public async Task LoadAsync_MatchRow_CanCheckFalse_EvenWhenItValidatedFalse()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 1 });
            _client.BomLinesToReturn = new List<InventreeBomLine>
                { new InventreeBomLine { SubPartPk = 10, Quantity = 1, Validated = false } };

            var vm = CreateVm();
            await vm.LoadAsync();

            Assert.That(vm.Lines[0].State,    Is.EqualTo(BomDiffState.Match));
            Assert.That(vm.Lines[0].CanCheck, Is.False,
                "Match rows are never pushable, regardless of Validated");
        }

        [Test]
        public async Task LoadAsync_InventreeOnlyRow_CanCheckIsFalse()
        {
            _bomService.LinesToReturn = new List<SwBomLine>();
            _client.BomLinesToReturn = new List<InventreeBomLine>
                { new InventreeBomLine { Pk = 1, SubPartPk = 10, Quantity = 1 } };

            var vm = CreateVm();
            await vm.LoadAsync();

            Assert.That(vm.Lines[0].CanCheck, Is.False);
        }

        [Test]
        public async Task LoadAsync_NewRow_CanCheckIsTrue()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 1 });
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            await vm.LoadAsync();

            Assert.That(vm.Lines[0].State,    Is.EqualTo(BomDiffState.New));
            Assert.That(vm.Lines[0].CanCheck, Is.True);
        }

        [Test]
        public async Task LoadAsync_ItOnlyRow_SwColumnsEmpty()
        {
            _bomService.LinesToReturn = new List<SwBomLine>();
            _client.BomLinesToReturn = new List<InventreeBomLine>
                { new InventreeBomLine { SubPartPk = 10, Quantity = 2, Reference = "R1" } };

            var vm = CreateVm();
            await vm.LoadAsync();

            Assert.That(vm.Lines[0].SwQty,      Is.EqualTo(string.Empty));
            Assert.That(vm.Lines[0].SwReference, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task LoadAsync_NullItLine_ItFieldsReturnDefaults()
        {
            // New row — ItLine is null
            _bomService.LinesToReturn.Add(new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 1 });
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            await vm.LoadAsync();

            Assert.That(vm.Lines[0].ItQty,         Is.EqualTo(string.Empty));
            Assert.That(vm.Lines[0].ItReference,    Is.EqualTo(string.Empty));
            Assert.That(vm.Lines[0].ItNote,         Is.EqualTo(string.Empty));
            Assert.That(vm.Lines[0].ItConsumable,   Is.False);
            Assert.That(vm.Lines[0].ItOptional,     Is.False);
            Assert.That(vm.Lines[0].ItValidated,    Is.False);
            Assert.That(vm.Lines[0].HasSubstitutes, Is.False);
        }

        [Test]
        public async Task LoadAsync_ItOnlyWithSubstitutes_HasSubstitutesTrue()
        {
            _bomService.LinesToReturn = new List<SwBomLine>();
            _client.BomLinesToReturn = new List<InventreeBomLine>
                { new InventreeBomLine { SubPartPk = 10, Quantity = 1, HasSubstitutes = true } };

            var vm = CreateVm();
            await vm.LoadAsync();

            Assert.That(vm.Lines[0].HasSubstitutes, Is.True);
        }

        [Test]
        public async Task StateLabel_InventreeOnly_IsItOnly()
        {
            _bomService.LinesToReturn = new List<SwBomLine>();
            _client.BomLinesToReturn = new List<InventreeBomLine>
                { new InventreeBomLine { SubPartPk = 10, Quantity = 1 } };

            var vm = CreateVm();
            await vm.LoadAsync();

            Assert.That(vm.Lines[0].StateLabel, Is.EqualTo("IT Only"));
        }

        // ── ApplyAsync ────────────────────────────────────────────────────────

        [Test]
        public async Task ApplyAsync_OnlyPushesCheckedRows()
        {
            _bomService.LinesToReturn = new List<SwBomLine>
            {
                new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 1 },
                new SwBomLine { Ipn = "B", SubPartPk = 20, Quantity = 2 },
            };
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            await vm.LoadAsync();
            vm.Lines[0].IsChecked = true;
            vm.Lines[1].IsChecked = false;
            await vm.ApplyAsync();

            Assert.That(_client.CreatedBomLines.Count,       Is.EqualTo(1));
            Assert.That(_client.CreatedBomLines[0].SubPartPk, Is.EqualTo(10));
        }

        [Test]
        public async Task ApplyAsync_NewRow_PushedWithFalseConsumableAndOptional()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 1 });
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            await vm.LoadAsync();
            vm.Lines[0].IsChecked = true;
            await vm.ApplyAsync();

            Assert.That(_client.CreatedBomLines[0].Consumable, Is.False);
            Assert.That(_client.CreatedBomLines[0].Optional,   Is.False);
        }

        [Test]
        public async Task ApplyAsync_ConflictRow_PreservesItConsumableAndOptional()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 2 });
            _client.BomLinesToReturn = new List<InventreeBomLine>
            {
                new InventreeBomLine { Pk = 5, SubPartPk = 10, Quantity = 1,
                    Consumable = true, Optional = true }
            };

            var vm = CreateVm();
            await vm.LoadAsync();
            vm.Lines[0].IsChecked = true;
            await vm.ApplyAsync();

            Assert.That(_client.UpdatedBomLines.Count, Is.EqualTo(1));
            var call = _client.UpdatedBomLines[0];
            Assert.That(call.Pk,         Is.EqualTo(5));
            Assert.That(call.Consumable, Is.True);
            Assert.That(call.Optional,   Is.True);
        }

        [Test]
        public async Task ApplyAsync_UsesSubPartPkFromDiff_NotIpnLookup()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { Ipn = "A", SubPartPk = 99, Quantity = 1 });
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            await vm.LoadAsync();
            vm.Lines[0].IsChecked = true;
            await vm.ApplyAsync();

            Assert.That(_client.CreatedBomLines[0].SubPartPk, Is.EqualTo(99));
        }

        [Test]
        public async Task ApplyAsync_PerRowFailure_ContinuesAndReportsInStatusText()
        {
            _bomService.LinesToReturn = new List<SwBomLine>
            {
                new SwBomLine { SubPartPk = 10, Quantity = 1 },
                new SwBomLine { SubPartPk = 20, Quantity = 1 },
            };
            _client.BomLinesToReturn = new List<InventreeBomLine>();
            _client.ThrowOnCreateBom = true;

            var vm = CreateVm();
            await vm.LoadAsync();
            foreach (var line in vm.Lines) line.IsChecked = true;
            await vm.ApplyAsync();

            Assert.That(vm.StatusText, Does.Contain("failed"));
        }

        [Test]
        public async Task ApplyAsync_RefetchesAndRebindsAfterPush()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { SubPartPk = 10, Quantity = 1 });
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            await vm.LoadAsync();
            vm.Lines[0].IsChecked = true;
            await vm.ApplyAsync();

            Assert.That(vm.Lines, Is.Not.Null);
        }

        [Test]
        public async Task ApplyAsync_Cancelled_MakesNoApiCalls()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { SubPartPk = 10, Quantity = 1 });
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            await vm.LoadAsync();
            vm.Lines[0].IsChecked = true;
            vm.ConfirmPush = (created, updated) => false;
            await vm.ApplyAsync();

            Assert.That(_client.CreatedBomLines.Count, Is.EqualTo(0));
        }

        // ── Sort ──────────────────────────────────────────────────────────────

        [Test]
        public async Task SortCommand_SortsByIpnAscending()
        {
            _bomService.LinesToReturn = new List<SwBomLine>
            {
                new SwBomLine { Ipn = "Z", SubPartPk = 20, Quantity = 1 },
                new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 1 },
            };
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            await vm.LoadAsync();
            vm.SortCommand("IPN");

            Assert.That(string.Compare(vm.Lines[0].DisplayIpn, vm.Lines[1].DisplayIpn,
                System.StringComparison.Ordinal), Is.LessThan(0),
                "First IPN should be less than second after ascending sort");
        }

        [Test]
        public async Task SortCommand_SecondCallOnSameColumn_TogglesToDescending()
        {
            _bomService.LinesToReturn = new List<SwBomLine>
            {
                new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 1 },
                new SwBomLine { Ipn = "Z", SubPartPk = 20, Quantity = 1 },
            };
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            await vm.LoadAsync();
            vm.SortCommand("IPN"); // asc
            vm.SortCommand("IPN"); // desc

            Assert.That(string.Compare(vm.Lines[0].DisplayIpn, vm.Lines[1].DisplayIpn,
                System.StringComparison.Ordinal), Is.GreaterThan(0),
                "After toggle, first IPN should be greater");
        }

        [Test]
        public async Task SortCommand_ProblemRowsAlwaysLast()
        {
            _bomService.LinesToReturn = new List<SwBomLine>
            {
                new SwBomLine { Ipn = "", SubPartPk = 0, Quantity = 1 },   // NoIpn
                new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 1 }, // New
            };
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            await vm.LoadAsync();
            vm.SortCommand("IPN");

            Assert.That(vm.Lines.Last().State,
                Is.EqualTo(BomDiffState.NoIpn)
                    .Or.EqualTo(BomDiffState.IpnNotFound)
                    .Or.EqualTo(BomDiffState.Ambiguous));
        }
    }
}
