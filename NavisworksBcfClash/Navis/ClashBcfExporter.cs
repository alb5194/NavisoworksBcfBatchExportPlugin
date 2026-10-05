using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisworksBcfClash.Bcf;
using NavisApp = Autodesk.Navisworks.Api.Application;
using NavisColor = Autodesk.Navisworks.Api.Color;

namespace NavisworksBcfClash.Navis
{
    public enum OthersDisplay
    {
        /// <summary>Same as Clash Detective "Dim Other": everything else greyed and transparent.</summary>
        Dim,
        /// <summary>Same as Clash Detective "Hide Other": only the two clashing items visible.</summary>
        Hide,
        /// <summary>Leave the rest of the model as it is.</summary>
        Normal
    }

    public class ExportOptions
    {
        public string OutputPath { get; set; }
        public BcfVersion Version { get; set; } = BcfVersion.V21;
        public OthersDisplay Others { get; set; } = OthersDisplay.Dim;
        public bool IncludeSnapshot { get; set; } = true;
        public int MaxSnapshotWidth { get; set; } = 1920;
        public bool IncludeComments { get; set; } = true;

        /// <summary>Write a section box (6 BCF clipping planes) around the clash area.</summary>
        public bool SectionBox { get; set; }
        public string Author { get; set; } = Environment.UserName;
    }

    public class ExportResult
    {
        public int Exported { get; set; }
        public bool Cancelled { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Reproduces the Clash Detective view for each clash result (viewpoint + red/green highlight +
    /// dim/hide other), captures it and writes the result as BCF topics.
    /// </summary>
    internal class ClashBcfExporter
    {
        // Clash Detective defaults: Item 1 red, Item 2 green.
        private static readonly NavisColor Item1Color = new NavisColor(1.0, 0.0, 0.0);
        private static readonly NavisColor Item2Color = new NavisColor(0.0, 1.0, 0.0);
        private static readonly NavisColor DimColor = new NavisColor(0.6, 0.6, 0.6);
        private const double DimTransparency = 0.85;
        private const string Item1Hex = "FF0000";
        private const string Item2Hex = "00FF00";

        private readonly Document _doc;
        private readonly ExportOptions _options;
        private readonly ComponentResolver _resolver = new ComponentResolver();
        private readonly double _toMeters;
        private readonly ClashComViews _comViews;

        // Items hidden by us for the current clash (so we can un-hide exactly those).
        private readonly ModelItemCollection _hiddenByUs = new ModelItemCollection();

        public ClashBcfExporter(Document doc, ExportOptions options)
        {
            _doc = doc;
            _options = options;
            _toMeters = UnitConversion.ScaleFactor(doc.Units, Units.Meters);

            try
            {
                _comViews = new ClashComViews();
            }
            catch
            {
                _comViews = null;
            }
        }

        public ExportResult Export(IList<ClashNode> results)
        {
            var outcome = new ExportResult();
            var topics = new List<BcfTopic>();

            // Clash Detective keeps the current viewing direction and zooms to the clash,
            // so auto-generated views start from the viewpoint active when the export begins.
            Viewpoint baseViewpoint = _doc.CurrentViewpoint.CreateCopy();
            var savedSelection = new ModelItemCollection();
            savedSelection.CopyFrom(_doc.CurrentSelection.SelectedItems);

            Progress progress = NavisApp.BeginProgress("Clash to BCF", "Exporting clash results...");
            try
            {
                // Selection highlight would tint the clashing items in the snapshot.
                _doc.CurrentSelection.Clear();

                for (int i = 0; i < results.Count; i++)
                {
                    progress.Update((double)i / results.Count);
                    if (progress.IsCanceled)
                    {
                        outcome.Cancelled = true;
                        break;
                    }

                    ClashNode node = results[i];
                    try
                    {
                        topics.Add(ExportResultTopic(node, baseViewpoint));
                    }
                    catch (Exception ex)
                    {
                        outcome.Warnings.Add($"{node.Test?.DisplayName} / {node.Name}: {ex.Message}");
                    }
                    finally
                    {
                        ClearClashDisplay();
                    }
                }
            }
            finally
            {
                ClearClashDisplay();
                _doc.CurrentViewpoint.CopyFrom(baseViewpoint);
                _doc.CurrentSelection.CopyFrom(savedSelection);
                NavisApp.EndProgress();
            }

            if (topics.Count > 0)
            {
                var writer = new BcfWriter
                {
                    Version = _options.Version,
                    ProjectName = Path.GetFileNameWithoutExtension(_doc.FileName),
                    SourceFileName = string.IsNullOrEmpty(_doc.FileName) ? null : Path.GetFileName(_doc.FileName)
                };
                writer.Write(_options.OutputPath, topics);
            }

            outcome.Exported = topics.Count;
            return outcome;
        }

        private BcfTopic ExportResultTopic(ClashNode node, Viewpoint baseViewpoint)
        {
            ClashResult result = node.Result;
            ClashTest test = node.Test;

            // 1) Same display as Clash Detective: item colors + dim/hide other
            ApplyClashDisplay(result);

            // 2) Same camera as Clash Detective: saved result viewpoint, otherwise Navisworks' own clash viewpoint
            ApplyClashViewpoint(node, baseViewpoint);

            // 3) Read back the camera actually used, and capture exactly what is rendered
            Viewpoint current = _doc.CurrentViewpoint.Value;
            var viewpoint = new BcfViewpoint
            {
                Camera = ToBcfCamera(current, result),
                SnapshotPng = _options.IncludeSnapshot ? CaptureSnapshot() : null
            };

            BcfComponent c1 = _resolver.Resolve(result.Item1);
            BcfComponent c2 = _resolver.Resolve(result.Item2);
            foreach (var c in new[] { c1, c2 }.Where(c => c != null))
                viewpoint.Selection.Add(c);

            if (c1 != null)
                viewpoint.Coloring.Add(new BcfColoring { Color = Item1Hex, Components = { c1 } });
            if (c2 != null)
                viewpoint.Coloring.Add(new BcfColoring { Color = Item2Hex, Components = { c2 } });

            if (_options.Others == OthersDisplay.Hide)
            {
                viewpoint.DefaultVisibility = false;
                foreach (var c in new[] { c1, c2 }.Where(c => c != null))
                    viewpoint.VisibilityExceptions.Add(c);
            }

            if (_options.SectionBox)
            {
                BoundingBox3D box = GetClashBounds(result);
                if (box != null && !box.IsEmpty)
                {
                    viewpoint.AddSectionBox(
                        new Vec3(box.Min.X, box.Min.Y, box.Min.Z) * _toMeters,
                        new Vec3(box.Max.X, box.Max.Y, box.Max.Z) * _toMeters);
                }
            }

            var topic = new BcfTopic
            {
                Title = $"{result.DisplayName} - {test?.DisplayName}",
                Description = BuildDescription(result, test),
                TopicType = "Clash",
                TopicStatus = MapStatus(result.Status),
                CreationAuthor = _options.Author,
                CreationDate = result.CreatedTime ?? DateTime.UtcNow,
                AssignedTo = result.AssignedTo,
                Viewpoint = viewpoint
            };

            if (test != null)
                topic.Labels.Add(test.DisplayName);
            topic.Labels.Add("Clash " + result.Status);

            if (_options.IncludeComments)
                topic.Comments.AddRange(ReadComments(result));

            return topic;
        }

        #region View reproduction

        private void ApplyClashViewpoint(ClashNode node, Viewpoint baseViewpoint)
        {
            ClashResult result = node.Result;
            try
            {
                if (_comViews != null && _comViews.TryApply(node.Test?.DisplayName, result.DisplayName, result.HasSavedViewpoint))
                    return;
            }
            catch
            {
                // fall back to zooming on the clash below
            }

            Viewpoint vp = baseViewpoint.CreateCopy();
            BoundingBox3D box = GetClashBounds(result);
            if (box != null && !box.IsEmpty)
                vp.ZoomBox(box);
            _doc.CurrentViewpoint.CopyFrom(vp);
        }

        private static BoundingBox3D GetClashBounds(ClashResult result)
        {
            BoundingBox3D box = result.ViewBounds;
            if (box != null && !box.IsEmpty)
                return box;

            BoundingBox3D b1 = result.Item1?.BoundingBox();
            BoundingBox3D b2 = result.Item2?.BoundingBox();
            if (b1 == null) return b2;
            if (b2 == null) return b1;
            return new BoundingBox3D(
                new Point3D(Math.Min(b1.Min.X, b2.Min.X), Math.Min(b1.Min.Y, b2.Min.Y), Math.Min(b1.Min.Z, b2.Min.Z)),
                new Point3D(Math.Max(b1.Max.X, b2.Max.X), Math.Max(b1.Max.Y, b2.Max.Y), Math.Max(b1.Max.Z, b2.Max.Z)));
        }

        private void ApplyClashDisplay(ClashResult result)
        {
            var item1 = Collect(result.Item1);
            var item2 = Collect(result.Item2);
            var models = _doc.Models;

            if (_options.Others == OthersDisplay.Dim)
            {
                models.OverrideTemporaryColor(models.RootItems, DimColor);
                models.OverrideTemporaryTransparency(models.RootItems, DimTransparency);
            }
            else if (_options.Others == OthersDisplay.Hide)
            {
                HideAllExcept(item1.Concat(item2));
            }

            // Overrides on the leaves win over the ones on their ancestors.
            models.OverrideTemporaryTransparency(item1, 0);
            models.OverrideTemporaryTransparency(item2, 0);
            models.OverrideTemporaryColor(item1, Item1Color);
            models.OverrideTemporaryColor(item2, Item2Color);
        }

        private void ClearClashDisplay()
        {
            _doc.Models.ResetAllTemporaryMaterials();
            if (_hiddenByUs.Count > 0)
            {
                _doc.Models.SetHidden(_hiddenByUs, false);
                _hiddenByUs.Clear();
            }
        }

        /// <summary>
        /// Hides siblings along the paths to the kept items instead of the whole tree,
        /// so the cost is proportional to tree depth, not model size.
        /// </summary>
        private void HideAllExcept(IEnumerable<ModelItem> keep)
        {
            var keepPaths = new HashSet<ModelItem>();
            foreach (ModelItem item in keep)
                for (ModelItem it = item; it != null; it = it.Parent)
                    keepPaths.Add(it);

            var toHide = new ModelItemCollection();

            void Consider(ModelItem candidate)
            {
                if (!keepPaths.Contains(candidate) && !candidate.IsHidden)
                    toHide.Add(candidate);
            }

            foreach (ModelItem root in _doc.Models.RootItems)
                Consider(root);
            foreach (ModelItem onPath in keepPaths)
                foreach (ModelItem child in onPath.Children)
                    Consider(child);

            if (toHide.Count > 0)
            {
                _doc.Models.SetHidden(toHide, true);
                _hiddenByUs.AddRange(toHide);
            }
        }

        private static ModelItemCollection Collect(ModelItem item)
        {
            var collection = new ModelItemCollection();
            if (item != null)
                collection.AddRange(item.DescendantsAndSelf);
            return collection;
        }

        private BcfCamera ToBcfCamera(Viewpoint vp, ClashResult result)
        {
            Point3D p = vp.Position;
            Rotation3D r = vp.Rotation;

            // Navisworks cameras look down local -Z with local +Y up.
            Vec3 direction = Rotate(r, new Vec3(0, 0, -1)).Normalized();
            Vec3 up = Rotate(r, new Vec3(0, 1, 0)).Normalized();
            Vec3 position = new Vec3(p.X, p.Y, p.Z) * _toMeters;

            // Distance to the clash along the view direction: used to keep framing if FOV must be clamped (BCF 2.1).
            Point3D c = result.Center;
            Vec3 toCenter = new Vec3(c.X, c.Y, c.Z) * _toMeters - position;
            double focal = toCenter.X * direction.X + toCenter.Y * direction.Y + toCenter.Z * direction.Z;

            View view = _doc.ActiveView;
            double aspect = view.Height > 0 ? (double)view.Width / view.Height : 1.0;

            bool perspective = vp.Projection == ViewpointProjection.Perspective;
            var camera = new BcfCamera
            {
                IsPerspective = perspective,
                Position = position,
                Direction = direction,
                Up = up,
                AspectRatio = aspect,
                FocalDistance = focal > 0 ? focal : 0
            };

            if (perspective)
            {
                // HeightField = vertical field of view in radians
                double fov = vp.HeightField;
                camera.FieldOfView = fov > Math.PI ? fov : fov * 180.0 / Math.PI;
            }
            else
            {
                // HeightField = visible height of the view in model units
                camera.ViewToWorldScale = vp.HeightField * _toMeters;
            }

            return camera;
        }

        private static Vec3 Rotate(Rotation3D q, Vec3 v)
        {
            // q = (A, B, C) vector part, D scalar part.  v' = v + 2w(q x v) + 2 q x (q x v)
            double x = q.A, y = q.B, z = q.C, w = q.D;
            double tx = 2 * (y * v.Z - z * v.Y);
            double ty = 2 * (z * v.X - x * v.Z);
            double tz = 2 * (x * v.Y - y * v.X);
            return new Vec3(
                v.X + w * tx + (y * tz - z * ty),
                v.Y + w * ty + (z * tx - x * tz),
                v.Z + w * tz + (x * ty - y * tx));
        }

        private byte[] CaptureSnapshot()
        {
            View view = _doc.ActiveView;
            int width = Math.Max(1, view.Width);
            int height = Math.Max(1, view.Height);

            // Keep the viewport aspect ratio so the image matches the BCF camera framing.
            if (_options.MaxSnapshotWidth > 0 && width > _options.MaxSnapshotWidth)
            {
                height = (int)Math.Round(height * (double)_options.MaxSnapshotWidth / width);
                width = _options.MaxSnapshotWidth;
            }

            using (Bitmap bitmap = view.GenerateImage(ImageGenerationStyle.ScenePlusOverlay, width, height, true))
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        #endregion

        #region Topic content

        private string BuildDescription(ClashResult result, ClashTest test)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Clash test: {test?.DisplayName}");
            sb.AppendLine($"Clash: {result.DisplayName}");
            sb.AppendLine($"Status: {result.Status}");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Distance: {0:0.###} m", result.Distance * _toMeters));
            Point3D c = result.Center;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Clash point: X={0:0.###} Y={1:0.###} Z={2:0.###} m",
                c.X * _toMeters, c.Y * _toMeters, c.Z * _toMeters));
            string description = result.Description;
            if (!string.IsNullOrWhiteSpace(description))
                sb.AppendLine($"Notes: {description}");
            sb.AppendLine($"Item 1 (red): {DescribeItem(result.Item1)}");
            sb.Append($"Item 2 (green): {DescribeItem(result.Item2)}");
            return sb.ToString();
        }

        private string DescribeItem(ModelItem item)
        {
            if (item == null)
                return "-";

            var names = new List<string>();
            for (ModelItem it = item; it != null; it = it.Parent)
                if (!string.IsNullOrWhiteSpace(it.DisplayName))
                    names.Add(it.DisplayName);
            names.Reverse();

            BcfComponent component = _resolver.Resolve(item);
            string id = component?.AuthoringToolId != null ? $" [Id {component.AuthoringToolId}]" : string.Empty;
            return string.Join(" > ", names) + id;
        }

        private static string MapStatus(ClashResultStatus status)
        {
            switch (status)
            {
                case ClashResultStatus.Resolved:
                case ClashResultStatus.Approved:
                    return "Closed";
                default:
                    return "Open";
            }
        }

        private IEnumerable<BcfComment> ReadComments(ClashResult result)
        {
            if (result.Comments == null)
                yield break;

            foreach (Comment comment in result.Comments)
            {
                if (string.IsNullOrWhiteSpace(comment.Body))
                    continue;

                yield return new BcfComment
                {
                    Text = comment.Body,
                    Author = string.IsNullOrWhiteSpace(comment.Author) ? _options.Author : comment.Author,
                    Date = comment.CreationDate
                };
            }
        }

        #endregion
    }
}
