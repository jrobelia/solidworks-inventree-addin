namespace SwInventreeAddin.Bom
{
    public class BomDiffLine
    {
        public BomDiffState      State         { get; set; }
        public SwBomLine?        SwLine        { get; set; }   // null for InvenTreeOnly
        public InventreeBomLine? ItLine        { get; set; }   // null for New/NoIpn/IpnNotFound/Ambiguous
        public int               SubPartPk     { get; set; }
        public string            DisplayIpn    { get; set; } = string.Empty;
        /// <summary>Set after a successful CreateBomLineAsync so the line can be updated later.</summary>
        public int               NewBomLinePk  { get; set; }
    }
}
