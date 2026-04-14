using System.Collections.Generic;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Bom
{
    public static class BomDiffEngine
    {
        public static IReadOnlyList<BomDiffLine> Diff(
            IEnumerable<SwBomLine>                             swLines,
            IEnumerable<InventreeBomLine>                      itLines,
            IDictionary<string, IReadOnlyList<InventreePart>>  ipnLookup)
        {
            var result = new List<BomDiffLine>();
            var itByPk = new Dictionary<int, InventreeBomLine>();
            foreach (var it in itLines) itByPk[it.SubPartPk] = it;

            foreach (var sw in swLines)
            {
                int key;

                if (sw.SubPartPk > 0)
                {
                    key = sw.SubPartPk;
                }
                else if (!string.IsNullOrWhiteSpace(sw.Ipn))
                {
                    if (!ipnLookup.TryGetValue(sw.Ipn, out var parts) || parts.Count == 0)
                    {
                        result.Add(new BomDiffLine { State = BomDiffState.IpnNotFound,
                            SwLine = sw, DisplayIpn = sw.Ipn });
                        continue;
                    }
                    if (parts.Count > 1)
                    {
                        result.Add(new BomDiffLine { State = BomDiffState.Ambiguous,
                            SwLine = sw, DisplayIpn = sw.Ipn });
                        continue;
                    }
                    key = parts[0].Pk;
                }
                else
                {
                    result.Add(new BomDiffLine { State = BomDiffState.NoIpn,
                        SwLine = sw, DisplayIpn = string.Empty });
                    continue;
                }

                if (itByPk.TryGetValue(key, out var itLine))
                {
                    itByPk.Remove(key);
                    bool match = sw.Quantity  == itLine.Quantity
                              && sw.Reference == itLine.Reference
                              && sw.Note      == itLine.Note;
                    result.Add(new BomDiffLine
                    {
                        State      = match ? BomDiffState.Match : BomDiffState.Conflict,
                        SwLine     = sw,
                        ItLine     = itLine,
                        SubPartPk  = key,
                        DisplayIpn = string.IsNullOrEmpty(itLine.SubPartIpn) ? sw.Ipn : itLine.SubPartIpn,
                    });
                }
                else
                {
                    result.Add(new BomDiffLine { State = BomDiffState.New,
                        SwLine = sw, SubPartPk = key, DisplayIpn = sw.Ipn });
                }
            }

            foreach (var remaining in itByPk.Values)
                result.Add(new BomDiffLine
                {
                    State      = BomDiffState.InvenTreeOnly,
                    ItLine     = remaining,
                    SubPartPk  = remaining.SubPartPk,
                    DisplayIpn = remaining.SubPartIpn,
                });

            return result;
        }
    }
}
