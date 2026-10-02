using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisworksBcfClash.Navis;
using NavisworksBcfClash.UI;
using NavisApp = Autodesk.Navisworks.Api.Application;

namespace NavisworksBcfClash
{
    [Plugin("ClashToBcf", "PCMR",
        DisplayName = "Clash to BCF",
        ToolTip = "Export selected Clash Detective results to BCF with the same view of the clashing elements")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class ClashBcfPlugin : AddInPlugin
    {
        private const string Caption = "Clash to BCF";

        public override int Execute(params string[] parameters)
        {
            try
            {
                Document doc = NavisApp.ActiveDocument;
                if (doc == null || doc.IsClear)
                {
                    MessageBox.Show("Open a model first.", Caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return 0;
                }

                var roots = ClashTreeBuilder.Build(doc);
                if (roots.Count == 0)
                {
                    MessageBox.Show("No clash results found. Run a clash test in Clash Detective first.",
                        Caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return 0;
                }

                var window = new ExportWindow(roots, doc.FileName);
                new WindowInteropHelper(window).Owner = NavisApp.Gui.MainWindow.Handle;
                if (window.ShowDialog() != true)
                    return 0;

                var exporter = new ClashBcfExporter(doc, window.Options);
                ExportResult result = exporter.Export(window.SelectedResults);

                string message = $"Exported {result.Exported} clash(es) to:\n{window.Options.OutputPath}";
                if (result.Cancelled)
                    message += "\n\nExport was cancelled; only the clashes processed so far were written.";
                if (result.Warnings.Count > 0)
                    message += $"\n\n{result.Warnings.Count} clash(es) failed:\n" + string.Join("\n", result.Warnings.Take(10));
                message += "\n\nOpen the containing folder?";

                if (MessageBox.Show(message, Caption, MessageBoxButton.YesNo,
                        result.Warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information) == MessageBoxResult.Yes
                    && result.Exported > 0)
                {
                    Process.Start("explorer.exe", $"/select,\"{window.Options.OutputPath}\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed:\n" + ex, Caption, MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return 0;
        }
    }
}
