namespace SwInventreeAddin.InvenTree
{
    /// <summary>
    /// The set of InvenTree boolean flags sent when a new part is created.
    /// CopyCategoryParameters is a creation-time option only and is not stored
    /// on the part after creation.
    /// </summary>
    public class PartCreationFlags
    {
        public bool Assembly                   { get; set; }
        public bool Component                  { get; set; }
        public bool Purchaseable               { get; set; }
        public bool Salable                    { get; set; }
        public bool Trackable                  { get; set; }
        public bool Testable                   { get; set; }
        public bool CopyCategoryParameters     { get; set; }
    }
}
