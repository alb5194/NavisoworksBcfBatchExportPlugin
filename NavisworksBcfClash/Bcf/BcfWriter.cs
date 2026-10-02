using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace NavisworksBcfClash.Bcf
{
    /// <summary>
    /// Writes a .bcfzip (BCF 2.1 or 3.0) from in-memory topics. Has no Navisworks dependency.
    /// </summary>
    public class BcfWriter
    {
        // BCF 2.1 restricts FieldOfView to [45, 60] degrees; BCF 3.0 allows (0, 180).
        private const double Bcf21MinFov = 45.0;
        private const double Bcf21MaxFov = 60.0;

        public BcfVersion Version { get; set; } = BcfVersion.V21;
        public string ProjectName { get; set; }
        public string SourceFileName { get; set; }

        public void Write(string path, IEnumerable<BcfTopic> topics)
        {
            var topicList = topics.ToList();

            if (File.Exists(path))
                File.Delete(path);

            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                AddXml(zip, "bcf.version", BuildVersion());

                if (!string.IsNullOrWhiteSpace(ProjectName))
                    AddXml(zip, "project.bcfp", BuildProject());

                if (Version == BcfVersion.V30)
                    AddXml(zip, "extensions.xml", BuildExtensions(topicList));

                foreach (var topic in topicList)
                {
                    string folder = topic.Guid.ToString() + "/";
                    AddXml(zip, folder + "markup.bcf", Version == BcfVersion.V30 ? BuildMarkup30(topic) : BuildMarkup21(topic));

                    if (topic.Viewpoint != null)
                    {
                        AddXml(zip, folder + "viewpoint.bcfv", BuildVisInfo(topic.Viewpoint));

                        if (topic.Viewpoint.SnapshotPng != null)
                        {
                            var entry = zip.CreateEntry(folder + "snapshot.png", CompressionLevel.Optimal);
                            using (var s = entry.Open())
                                s.Write(topic.Viewpoint.SnapshotPng, 0, topic.Viewpoint.SnapshotPng.Length);
                        }
                    }
                }
            }
        }

        private XDocument BuildVersion()
        {
            if (Version == BcfVersion.V30)
                return new XDocument(new XElement("Version", new XAttribute("VersionId", "3.0")));

            return new XDocument(
                new XElement("Version",
                    new XAttribute("VersionId", "2.1"),
                    new XElement("DetailedVersion", "2.1")));
        }

        private XDocument BuildProject()
        {
            var projectId = Guid.NewGuid().ToString();
            if (Version == BcfVersion.V30)
            {
                return new XDocument(
                    new XElement("ProjectInfo",
                        new XElement("Project",
                            new XAttribute("ProjectId", projectId),
                            new XElement("Name", ProjectName))));
            }

            return new XDocument(
                new XElement("ProjectExtension",
                    new XElement("Project",
                        new XAttribute("ProjectId", projectId),
                        new XElement("Name", ProjectName)),
                    new XElement("ExtensionSchema")));
        }

        private static XDocument BuildExtensions(List<BcfTopic> topics)
        {
            XElement List(string container, string item, IEnumerable<string> values) =>
                new XElement(container, values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().Select(v => new XElement(item, v)));

            return new XDocument(
                new XElement("Extensions",
                    List("TopicTypes", "TopicType", topics.Select(t => t.TopicType)),
                    List("TopicStatuses", "TopicStatus", new[] { "Open", "Closed" }.Concat(topics.Select(t => t.TopicStatus))),
                    List("Priorities", "Priority", topics.Select(t => t.Priority)),
                    List("TopicLabels", "TopicLabel", topics.SelectMany(t => t.Labels)),
                    List("Users", "User", topics.SelectMany(t => new[] { t.CreationAuthor, t.AssignedTo }.Concat(t.Comments.Select(c => c.Author))))));
        }

        private XElement BuildHeader()
        {
            if (string.IsNullOrWhiteSpace(SourceFileName))
                return null;

            var file = new XElement("File",
                new XAttribute(Version == BcfVersion.V30 ? "IsExternal" : "isExternal", "true"),
                new XElement("Filename", SourceFileName),
                new XElement("Date", FormatDate(DateTime.UtcNow)));

            return Version == BcfVersion.V30
                ? new XElement("Header", new XElement("Files", file))
                : new XElement("Header", file);
        }

        private XDocument BuildMarkup21(BcfTopic t)
        {
            var topic = new XElement("Topic",
                new XAttribute("Guid", t.Guid),
                new XAttribute("TopicType", t.TopicType ?? "Clash"),
                new XAttribute("TopicStatus", t.TopicStatus ?? "Open"),
                new XElement("Title", t.Title),
                Opt("Priority", t.Priority),
                t.Labels.Select(l => new XElement("Labels", l)),
                new XElement("CreationDate", FormatDate(t.CreationDate)),
                new XElement("CreationAuthor", t.CreationAuthor),
                Opt("AssignedTo", t.AssignedTo),
                Opt("Description", t.Description));

            var markup = new XElement("Markup",
                BuildHeader(),
                topic,
                t.Comments.Select(c => BuildComment(c, t.Viewpoint)));

            if (t.Viewpoint != null)
                markup.Add(BuildViewpointRef(t.Viewpoint, "Viewpoints"));

            return new XDocument(markup);
        }

        private XDocument BuildMarkup30(BcfTopic t)
        {
            var topic = new XElement("Topic",
                new XAttribute("Guid", t.Guid),
                new XAttribute("TopicType", t.TopicType ?? "Clash"),
                new XAttribute("TopicStatus", t.TopicStatus ?? "Open"),
                new XElement("Title", t.Title),
                Opt("Priority", t.Priority),
                t.Labels.Count > 0 ? new XElement("Labels", t.Labels.Select(l => new XElement("Label", l))) : null,
                new XElement("CreationDate", FormatDate(t.CreationDate)),
                new XElement("CreationAuthor", t.CreationAuthor),
                Opt("AssignedTo", t.AssignedTo),
                Opt("Description", t.Description),
                t.Comments.Count > 0 ? new XElement("Comments", t.Comments.Select(c => BuildComment(c, t.Viewpoint))) : null,
                t.Viewpoint != null ? new XElement("Viewpoints", BuildViewpointRef(t.Viewpoint, "ViewPoint")) : null);

            return new XDocument(new XElement("Markup", BuildHeader(), topic));
        }

        private static XElement BuildComment(BcfComment c, BcfViewpoint vp)
        {
            return new XElement("Comment",
                new XAttribute("Guid", c.Guid),
                new XElement("Date", FormatDate(c.Date)),
                new XElement("Author", string.IsNullOrWhiteSpace(c.Author) ? "Unknown" : c.Author),
                new XElement("Comment", string.IsNullOrWhiteSpace(c.Text) ? "-" : c.Text),
                vp != null ? new XElement("Viewpoint", new XAttribute("Guid", vp.Guid)) : null);
        }

        private static XElement BuildViewpointRef(BcfViewpoint vp, string elementName)
        {
            return new XElement(elementName,
                new XAttribute("Guid", vp.Guid),
                new XElement("Viewpoint", "viewpoint.bcfv"),
                vp.SnapshotPng != null ? new XElement("Snapshot", "snapshot.png") : null);
        }

        private XDocument BuildVisInfo(BcfViewpoint vp)
        {
            var root = new XElement("VisualizationInfo", new XAttribute("Guid", vp.Guid));

            var visibility = new XElement("Visibility", new XAttribute("DefaultVisibility", vp.DefaultVisibility ? "true" : "false"));
            var hints = new XElement("ViewSetupHints",
                new XAttribute("SpacesVisible", "false"),
                new XAttribute("SpaceBoundariesVisible", "false"),
                new XAttribute("OpeningsVisible", "false"));

            if (Version == BcfVersion.V30)
                visibility.Add(hints);
            if (vp.VisibilityExceptions.Count > 0)
                visibility.Add(new XElement("Exceptions", vp.VisibilityExceptions.Select(BuildComponent)));

            var coloring = vp.Coloring.Where(c => c.Components.Count > 0).ToList();

            root.Add(new XElement("Components",
                Version == BcfVersion.V21 ? hints : null,
                vp.Selection.Count > 0 ? new XElement("Selection", vp.Selection.Select(BuildComponent)) : null,
                visibility,
                coloring.Count > 0
                    ? new XElement("Coloring", coloring.Select(c =>
                        new XElement("Color",
                            new XAttribute("Color", c.Color.ToUpperInvariant()),
                            Version == BcfVersion.V30
                                ? (object)new XElement("Components", c.Components.Select(BuildComponent))
                                : c.Components.Select(BuildComponent))))
                    : null));

            if (vp.Camera != null)
                root.Add(BuildCamera(vp.Camera));

            return new XDocument(root);
        }

        private XElement BuildCamera(BcfCamera cam)
        {
            Vec3 position = cam.Position;
            double fov = cam.FieldOfView;

            if (cam.IsPerspective && Version == BcfVersion.V21 && (fov < Bcf21MinFov || fov > Bcf21MaxFov))
            {
                // Clamp to the 2.1 range and dolly the camera along its view direction so the
                // focal point keeps the same on-screen size -> same framing of the clash.
                double clamped = Math.Max(Bcf21MinFov, Math.Min(Bcf21MaxFov, fov));
                if (cam.FocalDistance > 1e-9)
                {
                    Vec3 dir = cam.Direction.Normalized();
                    Vec3 target = position + dir * cam.FocalDistance;
                    double newDistance = cam.FocalDistance * Math.Tan(DegToRad(fov) / 2) / Math.Tan(DegToRad(clamped) / 2);
                    position = target - dir * newDistance;
                }
                fov = clamped;
            }

            if (cam.IsPerspective)
            {
                return new XElement("PerspectiveCamera",
                    Point("CameraViewPoint", position),
                    Point("CameraDirection", cam.Direction.Normalized()),
                    Point("CameraUpVector", cam.Up.Normalized()),
                    new XElement("FieldOfView", Num(fov)),
                    Version == BcfVersion.V30 ? new XElement("AspectRatio", Num(cam.AspectRatio)) : null);
            }

            return new XElement("OrthogonalCamera",
                Point("CameraViewPoint", position),
                Point("CameraDirection", cam.Direction.Normalized()),
                Point("CameraUpVector", cam.Up.Normalized()),
                new XElement("ViewToWorldScale", Num(cam.ViewToWorldScale)),
                Version == BcfVersion.V30 ? new XElement("AspectRatio", Num(cam.AspectRatio)) : null);
        }

        private static XElement BuildComponent(BcfComponent c)
        {
            return new XElement("Component",
                string.IsNullOrEmpty(c.IfcGuid) ? null : new XAttribute("IfcGuid", c.IfcGuid),
                Opt("OriginatingSystem", c.OriginatingSystem),
                Opt("AuthoringToolId", c.AuthoringToolId));
        }

        private static XElement Point(string name, Vec3 v) =>
            new XElement(name, new XElement("X", Num(v.X)), new XElement("Y", Num(v.Y)), new XElement("Z", Num(v.Z)));

        private static XElement Opt(string name, string value) =>
            string.IsNullOrWhiteSpace(value) ? null : new XElement(name, value);

        private static string Num(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        private static string FormatDate(DateTime d) =>
            d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        private static double DegToRad(double deg) => deg * Math.PI / 180.0;

        private static void AddXml(ZipArchive zip, string name, XDocument doc)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
            using (var stream = entry.Open())
            using (var writer = XmlWriter.Create(stream, settings))
                doc.Save(writer);
        }
    }
}
