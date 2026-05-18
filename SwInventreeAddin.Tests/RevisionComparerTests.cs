using NUnit.Framework;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class RevisionComparerTests
    {
        // ── Both blank ─────────────────────────────────────────────────────────

        [Test]
        public void BothBlank_ReturnsEqual()
        {
            Assert.That(RevisionComparer.Compare("", ""), Is.EqualTo(RevisionOrder.Equal));
        }

        [Test]
        public void BothWhitespace_ReturnsEqual()
        {
            Assert.That(RevisionComparer.Compare("  ", "  "), Is.EqualTo(RevisionOrder.Equal));
        }

        // ── One side blank ────────────────────────────────────────────────────

        [Test]
        public void SwBlank_ItHasValue_ReturnsAmbiguous()
        {
            Assert.That(RevisionComparer.Compare("", "A"), Is.EqualTo(RevisionOrder.Ambiguous));
        }

        [Test]
        public void ItBlank_SwHasValue_ReturnsSwIsNewer()
        {
            Assert.That(RevisionComparer.Compare("B", ""), Is.EqualTo(RevisionOrder.SwIsNewer));
        }

        // ── Scheme 1: numeric ─────────────────────────────────────────────────

        [Test]
        public void Numeric_Equal()
        {
            Assert.That(RevisionComparer.Compare("5", "5"), Is.EqualTo(RevisionOrder.Equal));
        }

        [Test]
        public void Numeric_SwIsNewer()
        {
            Assert.That(RevisionComparer.Compare("10", "9"), Is.EqualTo(RevisionOrder.SwIsNewer));
        }

        [Test]
        public void Numeric_ItIsNewer()
        {
            Assert.That(RevisionComparer.Compare("2", "10"), Is.EqualTo(RevisionOrder.ItIsNewer));
        }

        // ── Scheme 2: dot-numeric ─────────────────────────────────────────────

        [Test]
        public void DotNumeric_Equal()
        {
            Assert.That(RevisionComparer.Compare("1.1", "1.1"), Is.EqualTo(RevisionOrder.Equal));
        }

        [Test]
        public void DotNumeric_SwIsNewer_FirstSegment()
        {
            Assert.That(RevisionComparer.Compare("2.0", "1.9"), Is.EqualTo(RevisionOrder.SwIsNewer));
        }

        [Test]
        public void DotNumeric_ItIsNewer_SecondSegment()
        {
            Assert.That(RevisionComparer.Compare("1.0", "1.1"), Is.EqualTo(RevisionOrder.ItIsNewer));
        }

        [Test]
        public void DotNumeric_DifferentSegmentCounts_TreatedAsZero()
        {
            // 1.1 vs 1.1.0 should be equal (missing segments treated as 0)
            Assert.That(RevisionComparer.Compare("1.1", "1.1.0"), Is.EqualTo(RevisionOrder.Equal));
        }

        // ── Scheme 3: pure alpha ──────────────────────────────────────────────

        [Test]
        public void Alpha_Equal()
        {
            Assert.That(RevisionComparer.Compare("B", "B"), Is.EqualTo(RevisionOrder.Equal));
        }

        [Test]
        public void Alpha_SwIsNewer()
        {
            Assert.That(RevisionComparer.Compare("C", "B"), Is.EqualTo(RevisionOrder.SwIsNewer));
        }

        [Test]
        public void Alpha_ItIsNewer()
        {
            Assert.That(RevisionComparer.Compare("A", "B"), Is.EqualTo(RevisionOrder.ItIsNewer));
        }

        [Test]
        public void Alpha_CaseInsensitive()
        {
            Assert.That(RevisionComparer.Compare("b", "B"), Is.EqualTo(RevisionOrder.Equal));
        }

        [Test]
        public void Alpha_SingleLetterBeforeDoubleLetterZ_AA()
        {
            Assert.That(RevisionComparer.Compare("Z", "AA"), Is.EqualTo(RevisionOrder.ItIsNewer));
        }

        [Test]
        public void Alpha_DoubleLetterComparison()
        {
            Assert.That(RevisionComparer.Compare("AB", "AA"), Is.EqualTo(RevisionOrder.SwIsNewer));
        }

        // ── Scheme 3: alphanumeric (unified with alpha) ───────────────────────

        [Test]
        public void AlphaNumeric_Equal()
        {
            Assert.That(RevisionComparer.Compare("A1", "A1"), Is.EqualTo(RevisionOrder.Equal));
        }

        [Test]
        public void AlphaNumeric_SameLetterSwNumericIsNewer()
        {
            Assert.That(RevisionComparer.Compare("A2", "A1"), Is.EqualTo(RevisionOrder.SwIsNewer));
        }

        [Test]
        public void AlphaNumeric_SameLetterItNumericIsNewer()
        {
            Assert.That(RevisionComparer.Compare("A1", "A2"), Is.EqualTo(RevisionOrder.ItIsNewer));
        }

        [Test]
        public void AlphaNumeric_LetterTakesPrecedenceOverNumber()
        {
            // B1 > A99 because B > A
            Assert.That(RevisionComparer.Compare("B1", "A99"), Is.EqualTo(RevisionOrder.SwIsNewer));
        }

        // ── Shop schema: A < A1 < A2 < B < B1 < Z < AA ───────────────────────

        [TestCase("A",  "A1", ExpectedResult = RevisionOrder.ItIsNewer)]
        [TestCase("A1", "A2", ExpectedResult = RevisionOrder.ItIsNewer)]
        [TestCase("A2", "B",  ExpectedResult = RevisionOrder.ItIsNewer)]
        [TestCase("B",  "B1", ExpectedResult = RevisionOrder.ItIsNewer)]
        [TestCase("B1", "Z",  ExpectedResult = RevisionOrder.ItIsNewer)]
        [TestCase("Z",  "AA", ExpectedResult = RevisionOrder.ItIsNewer)]
        public RevisionOrder ShopSchema_SequenceIsOrdered(string sw, string it)
        {
            return RevisionComparer.Compare(sw, it);
        }

        // ── Cross-scheme → Ambiguous ──────────────────────────────────────────

        [Test]
        public void CrossScheme_NumericVsAlpha_IsAmbiguous()
        {
            Assert.That(RevisionComparer.Compare("1", "A"), Is.EqualTo(RevisionOrder.Ambiguous));
        }

        [Test]
        public void CrossScheme_DotNumericVsAlpha_IsAmbiguous()
        {
            Assert.That(RevisionComparer.Compare("1.0", "A"), Is.EqualTo(RevisionOrder.Ambiguous));
        }

        [Test]
        public void Unparseable_IsAmbiguous()
        {
            Assert.That(RevisionComparer.Compare("??", "B"), Is.EqualTo(RevisionOrder.Ambiguous));
        }
    }
}
