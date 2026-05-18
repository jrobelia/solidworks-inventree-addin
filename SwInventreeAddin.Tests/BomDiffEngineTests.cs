using System.Collections.Generic;
using NUnit.Framework;
using SwInventreeAddin.Bom;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class BomDiffEngineTests
    {
        private static SwBomLine SwLine(string ipn, decimal qty = 1, int pk = 0,
            string reference = "", string note = "") =>
            new SwBomLine { Ipn = ipn, Quantity = qty, SubPartPk = pk,
                            Reference = reference, Note = note };

        private static InventreeBomLine ItLine(int subPartPk, decimal qty = 1,
            string ipn = "", string reference = "", string note = "") =>
            new InventreeBomLine { Pk = subPartPk * 10, SubPartPk = subPartPk,
                                   Quantity = qty, SubPartIpn = ipn,
                                   Reference = reference, Note = note };

        private static Dictionary<string, IReadOnlyList<InventreePart>> NoLookups =>
            new Dictionary<string, IReadOnlyList<InventreePart>>();

        private static Dictionary<string, IReadOnlyList<InventreePart>> Lookup(string ipn, int pk) =>
            new Dictionary<string, IReadOnlyList<InventreePart>>
            { [ipn] = new List<InventreePart> { new InventreePart { Pk = pk, Ipn = ipn } } };

        private static Dictionary<string, IReadOnlyList<InventreePart>> AmbiguousLookup(string ipn) =>
            new Dictionary<string, IReadOnlyList<InventreePart>>
            {
                [ipn] = new List<InventreePart>
                {
                    new InventreePart { Pk = 1, Ipn = ipn },
                    new InventreePart { Pk = 2, Ipn = ipn },
                }
            };

        [Test]
        public void Diff_SamePkAllFieldsEqual_IsMatch()
        {
            var result = BomDiffEngine.Diff(
                new[] { SwLine("ABC", qty: 2, pk: 10) },
                new[] { ItLine(10, qty: 2) },
                NoLookups);
            Assert.That(result[0].State, Is.EqualTo(BomDiffState.Match));
        }

        [Test]
        public void Diff_SamePkQtyDiffers_IsConflict()
        {
            var result = BomDiffEngine.Diff(
                new[] { SwLine("ABC", qty: 2, pk: 10) },
                new[] { ItLine(10, qty: 3) },
                NoLookups);
            Assert.That(result[0].State, Is.EqualTo(BomDiffState.Conflict));
        }

        [Test]
        public void Diff_SwPkNotInInventree_IsNew()
        {
            var result = BomDiffEngine.Diff(
                new[] { SwLine("ABC", pk: 10) },
                new InventreeBomLine[0],
                NoLookups);
            Assert.That(result[0].State, Is.EqualTo(BomDiffState.New));
        }

        [Test]
        public void Diff_InventreeLineNotInSw_IsInventreeOnly()
        {
            var result = BomDiffEngine.Diff(
                new SwBomLine[0],
                new[] { ItLine(10) },
                NoLookups);
            Assert.That(result[0].State, Is.EqualTo(BomDiffState.InvenTreeOnly));
        }

        [Test]
        public void Diff_BlankIpnAndNoPk_IsNoIpn()
        {
            var result = BomDiffEngine.Diff(
                new[] { SwLine("", pk: 0) },
                new InventreeBomLine[0],
                NoLookups);
            Assert.That(result[0].State, Is.EqualTo(BomDiffState.NoIpn));
        }

        [Test]
        public void Diff_IpnWithZeroResults_IsIpnNotFound()
        {
            var lookups = new Dictionary<string, IReadOnlyList<InventreePart>>
                { ["MISSING"] = new List<InventreePart>() };
            var result = BomDiffEngine.Diff(
                new[] { SwLine("MISSING", pk: 0) },
                new InventreeBomLine[0],
                lookups);
            Assert.That(result[0].State, Is.EqualTo(BomDiffState.IpnNotFound));
        }

        [Test]
        public void Diff_IpnWithMultipleResults_IsAmbiguous()
        {
            var result = BomDiffEngine.Diff(
                new[] { SwLine("DUP", pk: 0) },
                new InventreeBomLine[0],
                AmbiguousLookup("DUP"));
            Assert.That(result[0].State, Is.EqualTo(BomDiffState.Ambiguous));
        }

        [Test]
        public void Diff_MultipleAmbiguousRows_AllReported()
        {
            var lookups = AmbiguousLookup("DUP");
            lookups["DUP2"] = new List<InventreePart>
            {
                new InventreePart { Pk = 3, Ipn = "DUP2" },
                new InventreePart { Pk = 4, Ipn = "DUP2" },
            };
            var result = BomDiffEngine.Diff(
                new[] { SwLine("DUP", pk: 0), SwLine("DUP2", pk: 0) },
                new InventreeBomLine[0],
                lookups);
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].State, Is.EqualTo(BomDiffState.Ambiguous));
            Assert.That(result[1].State, Is.EqualTo(BomDiffState.Ambiguous));
        }

        [Test]
        public void Diff_IpnResolvesToMatch_UsesLookupPk()
        {
            var result = BomDiffEngine.Diff(
                new[] { SwLine("ABC", qty: 1, pk: 0) },
                new[] { ItLine(99, qty: 1) },
                Lookup("ABC", 99));
            Assert.That(result[0].State, Is.EqualTo(BomDiffState.Match));
            Assert.That(result[0].SubPartPk, Is.EqualTo(99));
        }

        [Test]
        public void Diff_ConflictRowHasSwAndItLinePopulated()
        {
            var result = BomDiffEngine.Diff(
                new[] { SwLine("ABC", qty: 2, pk: 10) },
                new[] { ItLine(10, qty: 5) },
                NoLookups);
            Assert.That(result[0].SwLine, Is.Not.Null);
            Assert.That(result[0].ItLine, Is.Not.Null);
        }

        [Test]
        public void Diff_InventreeOnlyRowHasNullSwLine()
        {
            var result = BomDiffEngine.Diff(
                new SwBomLine[0],
                new[] { ItLine(10) },
                NoLookups);
            Assert.That(result[0].SwLine, Is.Null);
            Assert.That(result[0].ItLine, Is.Not.Null);
        }
    }
}
