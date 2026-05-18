namespace SwInventreeAddin.Bom
{
    public class InventreeBomLine
    {
        public int     Pk             { get; set; }
        public int     SubPartPk      { get; set; }
        public string  SubPartIpn     { get; set; } = string.Empty;  // fetched for display
        public decimal Quantity       { get; set; }
        public string  Reference      { get; set; } = string.Empty;
        public string  Note           { get; set; } = string.Empty;
        public bool    Consumable     { get; set; }
        public bool    Optional       { get; set; }
        public bool    Validated      { get; set; }    // read from BOM line; never written by add-in
        public bool    HasSubstitutes { get; set; }    // true when substitutes array is non-empty
    }
}
