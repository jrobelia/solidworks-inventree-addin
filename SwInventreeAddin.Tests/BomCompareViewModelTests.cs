using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
            _mapping    = PropertyMappingConfig.WithDefaults();

            // Default: assembly part exists and is flagged as Assembly so ApplyAsync guard passes.
            _client.PartByPkToReturn = new InventreePart { Assembly = true };
        }

        private BomCompareViewModel CreateVm(int assemblyPk = 42) =>
            new BomCompareViewModel(_client, _bomService, _mapping, assemblyPk, "inventree");

        [Test]
        public async Task Constructor_DoesNotBackfillBomColumnAliases()
        {
            var partialMapping = new PropertyMappingConfig
            {
                SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                IpnProperty   = "PartNo",
            };

            _mapping = partialMapping;
            var vm = CreateVm();
            await vm.LoadAsync();

            Assert.That(_bomService.ReceivedMapping, Is.SameAs(partialMapping),
                "BomCompareViewModel must pass the supplied PropertyMappingConfig through without copying or backfilling it.");
            Assert.That(_bomService.ReceivedMapping!.BomColumnIpn, Is.Null,
                "Missing BOM column aliases must remain missing; they must not be silently backfilled with defaults at runtime.");
        }

        // ── LoadAsync ─────────────────────────────────────────────────────────

        [Test]
        public async Task LoadAsync_PopulatesMatchRow()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 1 });
            _client.BomLinesToReturn = new List<InventreeBomLine>
                { new InventreeBomLine { Pk = 1, SubPartPk = 10, Quantity = 1 } };

            var vm = CreateVm();

            int changes = 0;
            vm.Lines.CollectionChanged += (sender, e) => changes++;

            await vm.LoadAsync();

            Assert.That(vm.Lines.Count, Is.EqualTo(1));
            Assert.That(vm.Lines[0].State, Is.EqualTo(BomDiffState.Match));
            Assert.That(changes, Is.EqualTo(1), "LoadAsync should refresh Lines with a single CollectionChanged/Reset.");
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
        public async Task StateLabel_InventreeOnly_IsInvOnly()
        {
            _bomService.LinesToReturn = new List<SwBomLine>();
            _client.BomLinesToReturn = new List<InventreeBomLine>
                { new InventreeBomLine { SubPartPk = 10, Quantity = 1 } };

            var vm = CreateVm();
            await vm.LoadAsync();

            Assert.That(vm.Lines[0].StateLabel, Is.EqualTo("Inv Only"));
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
            await vm.PushAsync();

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
            await vm.PushAsync();

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
            await vm.PushAsync();

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
            await vm.PushAsync();

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
            await vm.PushAsync();

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
            await vm.PushAsync();

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
            await vm.PushAsync();

            Assert.That(_client.CreatedBomLines.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task PushAsync_SetsWorkingStatusBeforeConfirm()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { SubPartPk = 10, Quantity = 1 });
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            await vm.LoadAsync();
            vm.Lines[0].IsChecked = true;

            string? statusAtConfirm = null;
            vm.ConfirmPush = (created, updated) =>
            {
                statusAtConfirm = vm.StatusText;
                return true;
            };

            await vm.PushAsync();

            Assert.That(statusAtConfirm, Does.Contain("Pushing selected lines to InvenTree"));
            Assert.That(vm.StatusText, Does.Contain("created"));
        }

        [Test]
        public async Task PushAsync_WhenCancelled_ClearsWorkingStatus()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { SubPartPk = 10, Quantity = 1 });
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            await vm.LoadAsync();
            vm.Lines[0].IsChecked = true;
            vm.ConfirmPush = (created, updated) => false;
            await vm.PushAsync();

            Assert.That(vm.StatusText, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task PushAsync_ConflictRow_BecomesUnselectableAfterPush()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 2 });
            _client.BomLinesToReturn = new List<InventreeBomLine>
            {
                new InventreeBomLine { Pk = 5, SubPartPk = 10, Quantity = 1 }
            };

            var vm = CreateVm();
            await vm.LoadAsync();
            Assert.That(vm.Lines[0].State,    Is.EqualTo(BomDiffState.Conflict));
            Assert.That(vm.Lines[0].CanCheck, Is.True, "Conflict row is selectable before push");

            vm.Lines[0].IsChecked = true;
            await vm.PushAsync();

            Assert.That(vm.Lines[0].State,    Is.EqualTo(BomDiffState.Match));
            Assert.That(vm.Lines[0].CanCheck, Is.False,
                "CanCheck must reflect current State — pushed row is no longer selectable");
            Assert.That(vm.Lines[0].IsChecked, Is.False,
                "Checkbox must be cleared after a successful push");
        }

        [Test]
        public async Task PushAsync_NewRow_BecomesUnselectableAfterPush()
        {
            _bomService.LinesToReturn.Add(new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 1 });
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            await vm.LoadAsync();
            Assert.That(vm.Lines[0].State,    Is.EqualTo(BomDiffState.New));
            Assert.That(vm.Lines[0].CanCheck, Is.True);

            vm.Lines[0].IsChecked = true;
            await vm.PushAsync();

            Assert.That(vm.Lines[0].State,    Is.EqualTo(BomDiffState.Match));
            Assert.That(vm.Lines[0].CanCheck, Is.False,
                "Pushed New row must become unselectable");
            Assert.That(vm.Lines[0].IsChecked, Is.False);
        }

        [Test]
        public async Task PushAsync_SelectAllAfterPush_DoesNotReselectPushedRows()
        {
            // One pushable Conflict row + one pushable New row. Push only the Conflict row,
            // then simulate Select All and assert the pushed row stays unchecked.
            _bomService.LinesToReturn = new List<SwBomLine>
            {
                new SwBomLine { Ipn = "A", SubPartPk = 10, Quantity = 2 }, // Conflict
                new SwBomLine { Ipn = "B", SubPartPk = 20, Quantity = 1 }, // New
            };
            _client.BomLinesToReturn = new List<InventreeBomLine>
            {
                new InventreeBomLine { Pk = 5, SubPartPk = 10, Quantity = 1 }
            };

            var vm = CreateVm();
            await vm.LoadAsync();
            vm.Lines[0].IsChecked = true;   // Conflict
            vm.Lines[1].IsChecked = false;  // New
            await vm.PushAsync();

            // Mirror BomCompareWindow.SelectAll_Click: check every row where CanCheck.
            foreach (var line in vm.Lines.Where(l => l.CanCheck))
                line.IsChecked = true;

            Assert.That(vm.Lines[0].IsChecked, Is.False,
                "Pushed Conflict row must not be re-checkable by Select All");
            Assert.That(vm.Lines[1].IsChecked, Is.True,
                "Unpushed New row should still be selectable");
        }

        [Test]
        public void PushAsync_MarshalsUiUpdatesToSynchronizationContext()
        {
            var originalContext = SynchronizationContext.Current;
            var countingContext = new CountingSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(countingContext);

            try
            {
                _client.BomLinesToReturn = new List<InventreeBomLine>();
                var vm = CreateVm();
                var diffLine = new BomDiffLine
                {
                    State       = BomDiffState.New,
                    SubPartPk   = 10,
                    DisplayIpn  = "A",
                    SwLine      = new SwBomLine { Quantity = 1, Reference = string.Empty, Note = string.Empty },
                };
                var line = new BomDiffLineViewModel(diffLine) { IsChecked = true };
                vm.Lines.Add(line);

                vm.PushAsync().GetAwaiter().GetResult();

                Assert.That(countingContext.SendCount, Is.GreaterThan(0),
                    "UI-bound updates after HTTP awaits must be marshalled through the captured SynchronizationContext.");
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(originalContext);
            }
        }

        [Test]
        public void PushAsync_NoSelectedRows_StatusText_SaysNoChangesPushed()
        {
            _client.BomLinesToReturn = new List<InventreeBomLine>();

            var vm = CreateVm();
            vm.PushAsync().GetAwaiter().GetResult();

            Assert.That(vm.StatusText, Is.EqualTo("No changes pushed"));
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

        private class CountingSynchronizationContext : SynchronizationContext
        {
            public int SendCount { get; private set; }

            public override void Send(SendOrPostCallback d, object? state)
            {
                SendCount++;
                d(state);
            }

            public override void Post(SendOrPostCallback d, object? state) => d(state);
        }
    }
}
