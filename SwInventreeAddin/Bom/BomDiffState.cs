namespace SwInventreeAddin.Bom
{
    public enum BomDiffState
    {
        Match,           // same SubPartPk, all comparable fields equal
        Conflict,        // same SubPartPk, one or more fields differ
        New,             // SW only — SubPartPk not in InvenTree BOM
        InvenTreeOnly,   // InvenTree only — never pushed
        NoIpn,           // SW row with blank IPN and SubPartPk == 0
        IpnNotFound,     // SW row: IPN non-empty, 0 InvenTree parts found
        Ambiguous,       // SW row: IPN lookup returned multiple parts
    }
}
