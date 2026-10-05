using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api.ComApi;
using ComApi = Autodesk.Navisworks.Api.Interop.ComApi;

namespace NavisworksBcfClash.Navis
{
    /// <summary>
    /// The .NET clash API does not expose result viewpoints, so they are read from the COM API:
    /// the result's saved viewpoint, or the "suitable" viewpoint Navisworks computes for the clash.
    /// </summary>
    internal class ClashComViews
    {
        private readonly ComApi.InwOpState10 _state;
        private readonly Dictionary<string, ComApi.InwOclTestResult> _results =
            new Dictionary<string, ComApi.InwOclTestResult>(StringComparer.Ordinal);

        public ClashComViews()
        {
            _state = ComApiBridge.State;

            foreach (object plugin in _state.Plugins())
            {
                if (!(plugin is ComApi.InwOpClashElement clashElement))
                    continue;

                foreach (ComApi.InwOclClashTest test in clashElement.Tests())
                    foreach (ComApi.InwOclTestResult result in test.results())
                        _results[Key(test.name, result.name)] = result;
                break;
            }
        }

        public int Count => _results.Count;

        /// <summary>Applies the clash viewpoint to the current view. Returns false if the result was not found.</summary>
        public bool TryApply(string testName, string resultName, bool hasSavedViewpoint)
        {
            if (!_results.TryGetValue(Key(testName, resultName), out var result))
                return false;

            ComApi.InwNvViewPoint viewpoint = hasSavedViewpoint ? result.ViewPoint : null;
            if (viewpoint == null)
                viewpoint = result.GetSuitableViewPoint();
            if (viewpoint == null)
                return false;

            _state.CurrentView.ViewPoint = (ComApi.InwNvViewPoint)viewpoint.Copy();
            return true;
        }

        private static string Key(string test, string result) => test + "\u001f" + result;
    }
}
