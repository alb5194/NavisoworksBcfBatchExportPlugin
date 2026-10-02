using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Navisworks.Api;
using NavisworksBcfClash.Bcf;

namespace NavisworksBcfClash.Navis
{
    /// <summary>
    /// Maps a Navisworks ModelItem to a BCF component (IfcGuid + authoring tool id),
    /// so that Revit / IFC viewers can find the same element when the BCF is opened.
    /// </summary>
    internal class ComponentResolver
    {
        private static readonly string[] IfcGuidPropertyNames =
        {
            "IfcGUID", "IFC GUID", "IfcGuid", "GlobalId", "GLOBALID", "Global Id", "IFC Guid"
        };

        private readonly Dictionary<ModelItem, BcfComponent> _cache = new Dictionary<ModelItem, BcfComponent>();

        public BcfComponent Resolve(ModelItem leaf)
        {
            if (leaf == null)
                return null;
            if (_cache.TryGetValue(leaf, out var cached))
                return cached;

            string ifcGuid = null;
            string authoringId = null;

            // The clashing item is a leaf geometry node; the element properties may live on it or on a parent.
            for (ModelItem item = leaf; item != null && (ifcGuid == null || authoringId == null); item = item.Parent)
            {
                if (ifcGuid == null)
                    ifcGuid = FindIfcGuid(item);
                if (authoringId == null)
                    authoringId = FindElementId(item);
            }

            if (ifcGuid == null && leaf.InstanceGuid != Guid.Empty)
                ifcGuid = IfcGuid.FromGuid(leaf.InstanceGuid);

            var component = new BcfComponent
            {
                IfcGuid = ifcGuid,
                AuthoringToolId = authoringId,
                OriginatingSystem = FindOriginatingSystem(leaf)
            };

            _cache[leaf] = component;
            return component;
        }

        private static string FindIfcGuid(ModelItem item)
        {
            foreach (PropertyCategory category in item.PropertyCategories)
            {
                foreach (DataProperty property in category.Properties)
                {
                    if (Array.IndexOf(IfcGuidPropertyNames, property.DisplayName) < 0)
                        continue;

                    string normalized = IfcGuid.Normalize(ValueToString(property));
                    if (normalized != null)
                        return normalized;
                }
            }
            return null;
        }

        private static string FindElementId(ModelItem item)
        {
            foreach (PropertyCategory category in item.PropertyCategories)
            {
                // Revit NWC: tab "Element ID" -> "Value", or tab "Element" -> "Id"
                bool elementIdTab = string.Equals(category.DisplayName, "Element ID", StringComparison.OrdinalIgnoreCase);
                bool elementTab = string.Equals(category.DisplayName, "Element", StringComparison.OrdinalIgnoreCase);
                if (!elementIdTab && !elementTab)
                    continue;

                foreach (DataProperty property in category.Properties)
                {
                    bool match = elementIdTab
                        ? string.Equals(property.DisplayName, "Value", StringComparison.OrdinalIgnoreCase)
                        : string.Equals(property.DisplayName, "Id", StringComparison.OrdinalIgnoreCase);
                    if (!match)
                        continue;

                    string value = ValueToString(property);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }
            return null;
        }

        private static string FindOriginatingSystem(ModelItem leaf)
        {
            for (ModelItem item = leaf; item != null; item = item.Parent)
            {
                if (!item.HasModel)
                    continue;

                string source = item.Model.SourceFileName ?? item.Model.FileName;
                switch (Path.GetExtension(source ?? string.Empty).ToLowerInvariant())
                {
                    case ".rvt": return "Autodesk Revit";
                    case ".ifc": return "IFC";
                    case ".dwg": return "AutoCAD";
                    case ".nwc":
                    case ".nwd": return "Autodesk Navisworks";
                    default: return string.IsNullOrEmpty(source) ? null : Path.GetFileName(source);
                }
            }
            return null;
        }

        private static string ValueToString(DataProperty property)
        {
            try
            {
                var v = property.Value;
                if (v == null)
                    return null;
                if (v.IsDisplayString) return v.ToDisplayString();
                if (v.IsIdentifierString) return v.ToIdentifierString();
                if (v.IsInt32) return v.ToInt32().ToString();
                return v.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
