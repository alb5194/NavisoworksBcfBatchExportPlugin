using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using NavisworksBcfClash.Bcf;
using NavisworksBcfClash.Navis;

namespace NavisworksBcfClash.UI
{
    public partial class ExportWindow : Window
    {
        private const string AllStatuses = "All statuses";

        private readonly List<ClashNode> _roots;
        private readonly string _defaultFolder;
        private readonly string _defaultName;

        public ExportWindow(List<ClashNode> roots, string documentPath)
        {
            InitializeComponent();

            _roots = roots;
            _defaultFolder = string.IsNullOrEmpty(documentPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Path.GetDirectoryName(documentPath);
            _defaultName = (string.IsNullOrEmpty(documentPath) ? "Clashes" : Path.GetFileNameWithoutExtension(documentPath))
                           + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".bcf";

            ClashTree.ItemsSource = _roots;
            AuthorBox.Text = Environment.UserName;

            StatusFilter.Items.Add(AllStatuses);
            foreach (string status in _roots.SelectMany(r => r.AllResults()).Select(r => r.StatusText).Distinct().OrderBy(s => s))
                StatusFilter.Items.Add(status);
            StatusFilter.SelectedIndex = 0;

            if (_roots.Count == 1)
                _roots[0].IsExpanded = true;

            UpdateCount();
        }

        public ExportOptions Options { get; private set; }
        public IList<ClashNode> SelectedResults { get; private set; }

        private IEnumerable<ClashNode> CheckedResults() => _roots.SelectMany(r => r.CheckedResults());

        private void UpdateCount()
        {
            int total = _roots.Sum(r => r.CountResults());
            CountText.Text = $"{CheckedResults().Count()} of {total} clashes selected";
        }

        private void OnNodeChecked(object sender, RoutedEventArgs e) => UpdateCount();

        private void OnCheckAll(object sender, RoutedEventArgs e) => SetAll(true);

        private void OnCheckNone(object sender, RoutedEventArgs e) => SetAll(false);

        private void SetAll(bool value)
        {
            foreach (var root in _roots.Where(r => r.IsVisible))
                root.IsChecked = value;
            UpdateCount();
        }

        private void OnExpandAll(object sender, RoutedEventArgs e) => SetExpanded(_roots, true);

        private void OnCollapseAll(object sender, RoutedEventArgs e) => SetExpanded(_roots, false);

        private static void SetExpanded(IEnumerable<ClashNode> nodes, bool value)
        {
            foreach (var node in nodes.Where(n => n.Kind != ClashNodeKind.Result))
            {
                node.IsExpanded = value;
                SetExpanded(node.Children, value);
            }
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            if (_roots == null)
                return;

            string text = SearchBox.Text?.Trim() ?? string.Empty;
            string status = StatusFilter.SelectedItem as string;
            bool filtering = text.Length > 0 || (status != null && status != AllStatuses);

            foreach (var root in _roots)
                ApplyFilter(root, text, status, filtering);
        }

        /// <summary>Returns true when the node (or any descendant) matches. Matching test/group names show all their children.</summary>
        private static bool ApplyFilter(ClashNode node, string text, string status, bool filtering, bool parentMatched = false)
        {
            bool nameMatches = text.Length == 0 || node.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;

            bool visible;
            if (node.Kind == ClashNodeKind.Result)
            {
                bool statusMatches = status == null || status == AllStatuses || node.StatusText == status;
                visible = statusMatches && (nameMatches || parentMatched);
            }
            else
            {
                bool selfMatched = parentMatched || (text.Length > 0 && nameMatches);
                visible = false;
                foreach (var child in node.Children)
                    visible |= ApplyFilter(child, text, status, filtering, selfMatched);
                if (filtering && visible)
                    node.IsExpanded = true;
            }

            node.IsVisible = visible;
            return visible;
        }

        private void OnExport(object sender, RoutedEventArgs e)
        {
            var selected = CheckedResults().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Check at least one clash result to export.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save BCF",
                Filter = "BCF file (*.bcf)|*.bcf|BCF zip (*.bcfzip)|*.bcfzip",
                DefaultExt = "bcf",
                AddExtension = true,
                InitialDirectory = _defaultFolder,
                FileName = _defaultName,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true)
                return;

            Options = new ExportOptions
            {
                OutputPath = dialog.FileName,
                Version = VersionCombo.SelectedIndex == 1 ? BcfVersion.V30 : BcfVersion.V21,
                Others = (OthersDisplay)Math.Max(0, OthersCombo.SelectedIndex),
                Author = string.IsNullOrWhiteSpace(AuthorBox.Text) ? Environment.UserName : AuthorBox.Text.Trim(),
                IncludeSnapshot = SnapshotCheck.IsChecked == true,
                IncludeComments = CommentsCheck.IsChecked == true,
                SectionBox = SectionBoxCheck.IsChecked == true
            };
            SelectedResults = selected;
            DialogResult = true;
        }
    }
}
