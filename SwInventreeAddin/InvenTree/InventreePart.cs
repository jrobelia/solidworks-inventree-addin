namespace SwInventreeAddin.InvenTree
{
    public class InventreePart
    {
        public int     Pk           { get; set; }
        public string  Name         { get; set; } = string.Empty;
        public string  Description  { get; set; } = string.Empty;
        public string  Notes        { get; set; } = string.Empty;
        public string  Revision     { get; set; } = string.Empty;
        public string  Ipn          { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public decimal InStock      { get; set; }
        public decimal Ordering     { get; set; }
        public bool    Active       { get; set; }
    }
}
