using VirtoCommerce.ExportModule.Core.Model;

namespace VirtoCommerce.CatalogModule.Data.ExportImport
{
    public class ProductExportDataQuery : ExportDataQuery
    {
        public bool SearchInVariations { get; set; }
        public string[] CatalogIds { get; set; }
        public string[] CategoryIds { get; set; }
        public string ResponseGroup { get; set; }
    }
}
