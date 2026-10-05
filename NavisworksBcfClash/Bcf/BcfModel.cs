using System;
using System.Collections.Generic;

namespace NavisworksBcfClash.Bcf
{
    public enum BcfVersion
    {
        V21,
        V30
    }

    public struct Vec3
    {
        public double X, Y, Z;

        public Vec3(double x, double y, double z)
        {
            X = x; Y = y; Z = z;
        }

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public Vec3 Normalized()
        {
            double len = Length;
            return len > 1e-12 ? new Vec3(X / len, Y / len, Z / len) : this;
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
    }

    public class BcfCamera
    {
        public bool IsPerspective { get; set; }

        /// <summary>Camera position, in meters.</summary>
        public Vec3 Position { get; set; }

        public Vec3 Direction { get; set; }
        public Vec3 Up { get; set; }

        /// <summary>Vertical field of view in degrees (perspective only).</summary>
        public double FieldOfView { get; set; }

        /// <summary>Visible vertical extent of the view in meters (orthogonal only).</summary>
        public double ViewToWorldScale { get; set; }

        /// <summary>Viewport width / height.</summary>
        public double AspectRatio { get; set; }

        /// <summary>Distance from camera to the point of interest, in meters. Used to keep framing when FOV is clamped.</summary>
        public double FocalDistance { get; set; }
    }

    public class BcfComponent
    {
        public string IfcGuid { get; set; }
        public string AuthoringToolId { get; set; }
        public string OriginatingSystem { get; set; }

        public string Key => IfcGuid ?? AuthoringToolId ?? string.Empty;
    }

    public class BcfColoring
    {
        /// <summary>RRGGBB or AARRGGBB hex.</summary>
        public string Color { get; set; }
        public List<BcfComponent> Components { get; } = new List<BcfComponent>();
    }

    public class BcfViewpoint
    {
        public Guid Guid { get; set; } = Guid.NewGuid();
        public BcfCamera Camera { get; set; }
        public byte[] SnapshotPng { get; set; }

        public List<BcfComponent> Selection { get; } = new List<BcfComponent>();
        public bool DefaultVisibility { get; set; } = true;
        public List<BcfComponent> VisibilityExceptions { get; } = new List<BcfComponent>();
        public List<BcfColoring> Coloring { get; } = new List<BcfColoring>();

        /// <summary>Clipping planes in meters. Per BCF, Direction points to the clipped (invisible) half-space.</summary>
        public List<BcfClippingPlane> ClippingPlanes { get; } = new List<BcfClippingPlane>();

        /// <summary>Adds the 6 planes of an axis-aligned section box (meters); everything outside is clipped.</summary>
        public void AddSectionBox(Vec3 min, Vec3 max)
        {
            // Locations sit on the box faces (face centers), which some viewers expect.
            Vec3 c = (min + max) * 0.5;
            ClippingPlanes.Add(new BcfClippingPlane(new Vec3(max.X, c.Y, c.Z), new Vec3(1, 0, 0)));
            ClippingPlanes.Add(new BcfClippingPlane(new Vec3(min.X, c.Y, c.Z), new Vec3(-1, 0, 0)));
            ClippingPlanes.Add(new BcfClippingPlane(new Vec3(c.X, max.Y, c.Z), new Vec3(0, 1, 0)));
            ClippingPlanes.Add(new BcfClippingPlane(new Vec3(c.X, min.Y, c.Z), new Vec3(0, -1, 0)));
            ClippingPlanes.Add(new BcfClippingPlane(new Vec3(c.X, c.Y, max.Z), new Vec3(0, 0, 1)));
            ClippingPlanes.Add(new BcfClippingPlane(new Vec3(c.X, c.Y, min.Z), new Vec3(0, 0, -1)));
        }
    }

    public class BcfClippingPlane
    {
        public BcfClippingPlane(Vec3 location, Vec3 direction)
        {
            Location = location;
            Direction = direction;
        }

        public Vec3 Location { get; }
        public Vec3 Direction { get; }
    }

    public class BcfComment
    {
        public Guid Guid { get; set; } = Guid.NewGuid();
        public DateTime Date { get; set; }
        public string Author { get; set; }
        public string Text { get; set; }
    }

    public class BcfTopic
    {
        public Guid Guid { get; set; } = Guid.NewGuid();
        public string Title { get; set; }
        public string Description { get; set; }
        public string TopicType { get; set; } = "Clash";
        public string TopicStatus { get; set; } = "Open";
        public string Priority { get; set; }
        public DateTime CreationDate { get; set; } = DateTime.UtcNow;
        public string CreationAuthor { get; set; }
        public string AssignedTo { get; set; }
        public List<string> Labels { get; } = new List<string>();
        public List<BcfComment> Comments { get; } = new List<BcfComment>();
        public BcfViewpoint Viewpoint { get; set; }
    }
}
