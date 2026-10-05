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
    /// <summary>
    /// "PCMR" ribbon tab. The layout is read from en-US\NavisworksBcfClash.xaml beside the DLL,
    /// and the button icons from the Images folder beside the DLL.
    /// </summary>
    [Plugin("ClashToBcf", "PCMR",
        DisplayName = "Clash to BCF",
        ToolTip = "Export selected Clash Detective results to BCF with the same view of the clashing elements")]
    [RibbonLayout("NavisworksBcfClash.xaml")]
    [RibbonTab(TabId, DisplayName = "PCMR")]
    [Command(ClashToBcfCommandId,
        DisplayName = "Clash to BCF",
        Icon = "clash_to_bcf_16.ico", LargeIcon = "clash_to_bcf_32.ico",
        ToolTip = "Export selected Clash Detective results to BCF with the same view of the clashing elements")]
    public class ClashBcfRibbon : CommandHandlerPlugin
    {
        public const string TabId = "ID_PCMR_Tab";
        public const string ClashToBcfCommandId = "ID_PCMR_ClashToBcf";

        public override int ExecuteCommand(string commandId, params string[] parameters)
        {
            if (commandId == ClashToBcfCommandId)
                ClashToBcfCommand.Run();
            return 0;
        }
    }

    internal static class ClashToBcfCommand
    {
        private const string Caption = "Clash to BCF";

        public static void Run()
        {
            try
            {
                Document doc = NavisApp.ActiveDocument;
                if (doc == null || doc.IsClear)
                {
                    MessageBox.Show("Open a model first.", Caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var roots = ClashTreeBuilder.Build(doc);
                if (roots.Count == 0)
                {
                    MessageBox.Show("No clash results found. Run a clash test in Clash Detective first.",
                        Caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var window = new ExportWindow(roots, doc.FileName);
                new WindowInteropHelper(window).Owner = NavisApp.Gui.MainWindow.Handle;
                if (window.ShowDialog() != true)
                    return;

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
        }
    }
}
