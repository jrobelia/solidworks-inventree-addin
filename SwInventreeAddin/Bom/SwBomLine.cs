namespace SwInventreeAddin.Bom
{
    public class SwBomLine
    {
        public string Reference { get; set; } = string.Empty;
        public string Ipn { get; set; } = string.Empty;   // empty = no IPN in table
        public int SubPartPk { get; set; }                   // 0 = not resolved from custom property
        public decimal Quantity { get; set; }

        /// <summary>
        /// The raw quantity cell text when a mapped Qty column existed but did
        /// not parse as a decimal; null when the cell parsed or no column is
        /// mapped. A non-null value means <see cref="Quantity"/> was never read —
        /// the line must surface as Ambiguous, never push the silent 0.
        /// </summary>
        public string? UnparsedQuantity { get; set; }
        public string Note { get; set; } = string.Empty;
    }
}
