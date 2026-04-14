namespace SwInventreeAddin.Bom
{
    public class SwBomLine
    {
        public string  Reference { get; set; } = string.Empty;
        public string  Ipn       { get; set; } = string.Empty;   // empty = no IPN in table
        public int     SubPartPk { get; set; }                   // 0 = not resolved from custom property
        public decimal Quantity  { get; set; }
        public string  Note      { get; set; } = string.Empty;
    }
}
