using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace NavisworksBcfClash.Navis
{
    public enum ClashNodeKind
    {
        Test,
        Group,
        Result
    }

    /// <summary>Tree node (test / group / result) with tri-state check propagation for the export dialog.</summary>
    public class ClashNode : INotifyPropertyChanged
    {
        private bool? _isChecked = false;
        private bool _isExpanded;
        private bool _isVisible = true;

        public ClashNode(ClashNodeKind kind, string name, ClashNode parent)
        {
            Kind = kind;
            Name = name;
            Parent = parent;
        }

        public ClashNodeKind Kind { get; }
        public string Name { get; }
        public ClashNode Parent { get; }
        public ObservableCollection<ClashNode> Children { get; } = new ObservableCollection<ClashNode>();

        public ClashTest Test { get; set; }
        public ClashResult Result { get; set; }
        public string StatusText { get; set; }

        public string Display => Kind == ClashNodeKind.Result
            ? $"{Name}  [{StatusText}]"
            : $"{Name}  ({CountResults()})";

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); }
        }

        public bool? IsChecked
        {
            get => _isChecked;
            set => SetIsChecked(value, updateChildren: true, updateParent: true);
        }

        private void SetIsChecked(bool? value, bool updateChildren, bool updateParent)
        {
            if (value == _isChecked)
                return;

            _isChecked = value;

            if (updateChildren && value.HasValue)
            {
                foreach (var child in Children.Where(c => c.IsVisible))
                    child.SetIsChecked(value, true, false);
            }

            if (updateParent)
                Parent?.RecomputeFromChildren();

            OnPropertyChanged(nameof(IsChecked));
        }

        private void RecomputeFromChildren()
        {
            bool? state = null;
            bool first = true;
            foreach (var child in Children)
            {
                if (first) { state = child.IsChecked; first = false; }
                else if (state != child.IsChecked) { state = null; break; }
            }
            SetIsChecked(first ? false : state, false, true);
        }

        public int CountResults() =>
            Kind == ClashNodeKind.Result ? 1 : Children.Sum(c => c.CountResults());

        public IEnumerable<ClashNode> CheckedResults()
        {
            if (Kind == ClashNodeKind.Result)
            {
                if (IsChecked == true)
                    yield return this;
                yield break;
            }

            foreach (var child in Children)
                foreach (var r in child.CheckedResults())
                    yield return r;
        }

        public IEnumerable<ClashNode> AllResults()
        {
            if (Kind == ClashNodeKind.Result)
            {
                yield return this;
                yield break;
            }

            foreach (var child in Children)
                foreach (var r in child.AllResults())
                    yield return r;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    internal static class ClashTreeBuilder
    {
        public static List<ClashNode> Build(Document doc)
        {
            var roots = new List<ClashNode>();
            DocumentClash clash = doc.GetClash();
            if (clash == null)
                return roots;

            foreach (SavedItem item in clash.TestsData.Tests)
            {
                if (!(item is ClashTest test))
                    continue;

                var testNode = new ClashNode(ClashNodeKind.Test, test.DisplayName, null) { Test = test };
                AddChildren(testNode, test.Children, test);
                if (testNode.Children.Count > 0)
                    roots.Add(testNode);
            }
            return roots;
        }

        private static void AddChildren(ClashNode parent, SavedItemCollection items, ClashTest test)
        {
            foreach (SavedItem item in items)
            {
                if (item is ClashResult result)
                {
                    parent.Children.Add(new ClashNode(ClashNodeKind.Result, result.DisplayName, parent)
                    {
                        Test = test,
                        Result = result,
                        StatusText = result.Status.ToString()
                    });
                }
                else if (item is ClashResultGroup group)
                {
                    var groupNode = new ClashNode(ClashNodeKind.Group, group.DisplayName, parent) { Test = test };
                    AddChildren(groupNode, group.Children, test);
                    if (groupNode.Children.Count > 0)
                        parent.Children.Add(groupNode);
                }
            }
        }
    }
}
