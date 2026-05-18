namespace SwInventreeAddin.InvenTree
{
    public class InventreeCategory
    {
        public int    Pk       { get; set; }
        public string Name     { get; set; } = string.Empty;
        public int?   ParentPk { get; set; }

        /// <summary>True when the server might have children we haven't loaded yet.</summary>
        public bool HasChildren { get; set; }
    }
}
