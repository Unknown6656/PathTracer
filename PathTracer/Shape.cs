using System.Runtime.CompilerServices;
using System;

using Unknown6656.Mathematics.LinearAlgebra;

namespace PathTracer
{
    public abstract class Shape
    {
        public Material Material { get; }


        protected Shape(Material material) => Material = material;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract (double distance, bool inside)? Intersect(Ray ray);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract Vector3 NormalAt(Vector3 p);
    }

    public abstract class Light
        : Shape
    {
        protected Light(Color color, double intensity)
            : base(new Glow(color, intensity))
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract (Vector3 direction_to_light, double distance) GenerateShadowRay(Vector3 from_pos);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract double GetIntensity(Vector3 direction_to_object, double distance);
    }

    public class PointLight
        : Light
    {
        public Vector3 Position { get; }
        public double Falloff { get; }


        public PointLight(Vector3 position, Color color, double intensity, double falloff)
            : base(color, intensity) => (Position, Falloff) = (position, falloff);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override (Vector3 direction_to_light, double distance) GenerateShadowRay(Vector3 from_pos)
        {
            Vector3 δ = Position - from_pos;
            return (δ.Normalized, δ.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override double GetIntensity(Vector3 direction_to_object, double distance) => (Material as Glow)!.Intensity / (distance * distance * Falloff);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override (double distance, bool inside)? Intersect(Ray _) => null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Vector3 NormalAt(Vector3 _) => default;
    }

    public sealed class SpotLight
        : PointLight
    {
        public Vector3 Direction { get; }
        public double Sharpness { get; }


        public SpotLight(Vector3 position, Vector3 direction, Color color, double intensity, double falloff, double sharpness)
            : base(position, color, intensity, falloff) => (Direction, Sharpness) = (direction.Normalized, sharpness);

        public override double GetIntensity(Vector3 direction_to_object, double distance)
        {
            Scalar dot = Direction * direction_to_object;

            return dot > 0 ? (dot.AbsoluteValue ^ Sharpness) * base.GetIntensity(direction_to_object, distance) : 0;
        }
    }

    public sealed class Triangle
        : Shape
    {
        public Vector3 CornerA { get; }
        public Vector3 CornerB { get; }
        public Vector3 CornerC { get; }
        private Vector3 Normal { get; }


        public Triangle(Vector3 a, Vector3 b, Vector3 c, Material material)
            : base(material)
        {
            CornerA = a;
            CornerB = b;
            CornerC = c;
            Normal = (b - a).Cross(c - a).Normalized;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Vector3 NormalAt(Vector3 _) => Normal;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override (double distance, bool inside)? Intersect(Ray ray)
        {
            Vector3 v0v1 = CornerB - CornerA;
            Vector3 v0v2 = CornerC - CornerA;
            Vector3 pvec = ray.Direction.Cross(v0v2);

            if (v0v1 * pvec is { } det && Math.Abs(det) > Program.EPSILON)
            {
                double invDet = 1 / det;
                Vector3 tvec = ray.Origin - CornerA;
                double u = (double)(tvec * pvec) * invDet;

                if (u < 0 || u > 1)
                    return null;

                Vector3 qvec = tvec.Cross(v0v1);
                double v = (double)(ray.Direction * qvec) * invDet;

                if (v < 0 || u + v > 1)
                    return null;

                return ((double)(v0v2 * qvec) * invDet, false);
            }

            return null;
        }
    }

    public sealed class Plane
        : Shape
    {
        private readonly Triangle _t1, _t2;

        public Plane(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Material material)
            : base(material)
        {
            _t1 = new Triangle(a, b, d, material);
            _t2 = new Triangle(b, c, d, material);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override (double distance, bool inside)? Intersect(Ray ray) => _t1.Intersect(ray) ?? _t2.Intersect(ray);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Vector3 NormalAt(Vector3 p) => (_t1.NormalAt(p) + _t2.NormalAt(p)).Normalized;
    }

    public sealed class Sphere
        : Shape
    {
        public Vector3 Center { get; }
        public double Radius { get; }

        private readonly double _rad2;


        public Sphere(Vector3 pos, double radius, Material material)
            : base(material)
        {
            Radius = radius;
            Center = pos;
            _rad2 = radius * radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Vector3 NormalAt(Vector3 p) => (p - Center).Normalized;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override (double distance, bool inside)? Intersect(Ray ray)
        {
            Vector3 L = Center - ray.Origin;
            double tca = L * ray.Direction;

            if (tca < 0)
                return null;

            double d2 = (double)(L * L) - tca * tca;

            if (d2 > _rad2)
                return null;

            double thc = Math.Sqrt(_rad2 - d2);
            double t0 = tca - thc;
            double t1 = tca + thc;
            bool inside = false;

            double t = Math.Min(t0, t1);

            if (t < 0)
            {
                inside = true;
                t = Math.Max(t0, t1);
            }

            if (t < 0)
                return null;

            return (t, inside);
        }
    }

    public class Volume
        : Shape
    {
        private readonly Plane _p1, _p2, _p3, _p4, _p5, _p6;

        public Volume(Vector3 center, Vector3 left, Vector3 up, Vector3 back, double dim_left, double dim_up, double dim_back, Material material)
            : base(material)
        {
            left = left.Normalized * dim_left;
            up = up.Normalized * dim_up;
            back = back.Normalized * dim_back;

            Vector3 lbf = center - left - up - back;
            Vector3 lbb = center - left - up + back;
            Vector3 luf = center - left + up - back;
            Vector3 lub = center - left + up + back;
            Vector3 rbf = center + left - up - back;
            Vector3 rbb = center + left - up + back;
            Vector3 ruf = center + left + up - back;
            Vector3 rub = center + left + up + back;

            _p1 = new Plane(lbf, lbb, lub, luf, material);
            _p2 = new Plane(lbf, rbf, rbb, lbb, material);
            _p3 = new Plane(lbf, rbf, ruf, luf, material);
            _p4 = new Plane(rbf, rbb, rub, ruf, material);
            _p5 = new Plane(luf, ruf, rub, lub, material);
            _p6 = new Plane(lbb, rbb, rub, lub, material);
        }

        public override (double distance, bool inside)? Intersect(Ray ray) => _p1.Intersect(ray)
                                                                           ?? _p2.Intersect(ray)
                                                                           ?? _p3.Intersect(ray)
                                                                           ?? _p4.Intersect(ray)
                                                                           ?? _p5.Intersect(ray)
                                                                           ?? _p6.Intersect(ray); // TODO : real intersection tests

        public override Vector3 NormalAt(Vector3 p)
        {
            throw new NotImplementedException();
        }
    }

    public sealed class Cube
        : Volume
    {
        public Cube(Vector3 center, Vector3 left, Vector3 up, Vector3 back, double side_length, Material material)
            : base(center, left, up, back, side_length, side_length, side_length, material)
        {
        }
    }
}
