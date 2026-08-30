using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SwInventreeAddin.Bom;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.SolidWorks
{
    public class SwAssemblyBomService : IAssemblyBomService
    {
        private readonly ISldWorks _swApp;

        public SwAssemblyBomService(ISldWorks swApp) { _swApp = swApp; }

        public bool HasBomTable(string keyword) =>
            FindBomTableAnnotation(keyword) != null;

        public string GetBomTableName(string keyword)
        {
            var feature = FindBomFeature(keyword);
            return feature?.Name ?? keyword;
        }

        public IReadOnlyList<SwBomLine> GetBomLines(string keyword, PropertyMappingConfig mapping)
        {
            var bomTable = FindBomTableAnnotation(keyword)
                ?? throw new InvalidOperationException(
                    $"No BOM table containing '{keyword}' found in the active assembly.");

            // The COM object that implements IBomTableAnnotation also implements ITableAnnotation,
            // which exposes RowCount, ColumnCount, and Text2.
            var table = (ITableAnnotation)bomTable;

            int rowCount = table.RowCount;
            if (rowCount < 2) return new List<SwBomLine>();

            int colIpn       = FindColumn(table, mapping.BomColumnIpn);
            int colQty       = FindColumn(table, mapping.BomColumnQty);
            int colReference = FindColumn(table, mapping.BomColumnReference);
            int colNote      = FindColumn(table, mapping.BomColumnNote);

            var lines = new List<SwBomLine>();
            for (int row = 1; row < rowCount; row++)
            {
                var line = new SwBomLine
                {
                    Ipn       = (colIpn       >= 0 ? table.Text2[row, colIpn, false]       ?? "" : "").Trim(),
                    Reference = (colReference >= 0 ? table.Text2[row, colReference, false] ?? "" : "").Trim(),
                    Note      = (colNote      >= 0 ? table.Text2[row, colNote, false]       ?? "" : "").Trim(),
                };

                if (colQty >= 0 &&
                    decimal.TryParse((table.Text2[row, colQty, false] ?? "").Trim(), out var qty))
                    line.Quantity = qty;

                try
                {
                    var components = (object[])bomTable.GetComponents2(row, "");
                    if (components?.Length > 0 && components[0] is IComponent2 comp)
                    {
                        var model = comp.GetModelDoc2() as IModelDoc2;
                        if (model != null && !string.IsNullOrEmpty(mapping.PkProperty))
                        {
                            var mgr = (ICustomPropertyManager)model.Extension.CustomPropertyManager[""];
                            mgr.Get4(mapping.PkProperty!, false, out _, out var resolved);
                            if (int.TryParse(resolved, out var pk) && pk > 0)
                                line.SubPartPk = pk;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[SwInventreeAddin] GetBomLines: COM error reading SubPartPk — {ex.Message}"); }

                lines.Add(line);
            }
            return lines;
        }

        private IFeature? FindBomFeature(string keyword)
        {
            var doc = _swApp.IActiveDoc2 as IModelDoc2;
            if (doc == null) return null;
            var features = (object[])doc.FeatureManager.GetFeatures(false);
            if (features == null) return null;

            foreach (var featureObj in features)
            {
                if (!(featureObj is IFeature feature)) continue;
                if (feature.GetTypeName2() != "BomFeat") continue;
                if (feature.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
                return feature;
            }
            return null;
        }

        private IBomTableAnnotation? FindBomTableAnnotation(string keyword)
        {
            var feature    = FindBomFeature(keyword);
            var bomFeature = feature?.GetSpecificFeature2() as IBomFeature;
            var annotations = (object[]?)bomFeature?.GetTableAnnotations();
            if (annotations?.Length > 0)
                return annotations[0] as IBomTableAnnotation;
            return null;
        }

        private static int FindColumn(ITableAnnotation table, string? aliasCsv)
        {
            if (string.IsNullOrEmpty(aliasCsv)) return -1;
            var aliases  = aliasCsv!.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).ToArray();
            int colCount = table.ColumnCount;
            for (int col = 0; col < colCount; col++)
            {
                var header = (table.Text2[0, col, false] ?? "").Trim();
                foreach (var alias in aliases)
                    if (string.Equals(header, alias, StringComparison.OrdinalIgnoreCase))
                        return col;
            }
            return -1;
        }
    }
}
