//#define HIGH_QUALITY

using System.Runtime.CompilerServices;
using System.Linq;
using System;

using Unknown6656.Mathematics.LinearAlgebra;

namespace PathTracer
{
    public enum RenderMode
    {
        COLOR,
        DEPTH,
        NORMAL,
        NORMAL_ABS,
        RAY_DIR,
        RAY_DEPTH
    }

    public static partial class Program
    {
#if HIGH_QUALITY
        public const int MAX_DEPTH = 2048;
        public const int WIDTH = 1920;
        public const int HEIGHT = 1080;
        public const int SUBPIXELS = 4;
        public const int SAMPLES = 128;
#else
        public const int MAX_DEPTH = 4;
        public const int WIDTH = 640;
        public const int HEIGHT = 360;
        public const int SUBPIXELS = 1;
        public const int SAMPLES = 2;
#endif
        public static readonly Vector3 EYE = (-50, 90, 300);
        public static readonly Vector3 LOOK_AT = (10, 30, 0);
        public const double CAM_DIST = 130;
        public const double FOV = 0.5135;

        public static readonly Vector3 AMBIENT = (.01, .01, .01);
        public const RenderMode MODE = RenderMode.COLOR;
        public const bool ALPHA_PREMULTIPLIED = false;
        public const bool PARALLEL = true;
        public const double EPSILON = 1e-5;
        public const double ETA_AIR = 1;
        public const double GAMMA = 2.2;



#pragma warning disable CS0162 // unreachable code
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Trace(Ray ray)
        {
            Vector3 result = default;
            Vector3 eta = ray.Eta;
            double intensity = 1;
            bool @continue = true;

            while (@continue
                && ray.Depth < MAX_DEPTH
                && intensity > EPSILON
                && !ray.Direction.IsZero
                && (from o in _objects
                    let ints = o.Intersect(ray)
                    where ints.HasValue
                    let d = ints!.Value.distance
                    orderby d ascending
                    select (o, d, ints!.Value.inside)).ToArray()
                    is var intersections
                && intersections.Length > 0)
            {
                (Shape shape, double T, bool inside) = intersections[0];

                if (MODE == RenderMode.DEPTH)
                    return new Vector3(1 - T / (double)(EYE - LOOK_AT).Length);

                Vector3 pos = ray.Evaluate(T);
                Vector3 normal = shape.NormalAt(pos);

                if (MODE == RenderMode.NORMAL)
                    return .5 + normal / 2;
                if (MODE == RenderMode.NORMAL_ABS)
                    return normal.ComponentwiseAbsolute();

                Vector3 color = shape.Material.Color;
                Vector3 dir = ray.Direction;
                double luminance = 0;

                switch (shape.Material)
                {
                    case Diffuse diffuse:
                        color = CalculateDiffuse(pos, diffuse);
                        @continue = _rng.NextByte() < 32;

                        if (@continue)
                        {
                            dir = Vector3.GetRandomUnitVector();

                            Scalar θ = dir * normal;

                            if (θ.IsNegative)
                                @continue = false;

                            luminance = θ ^ 5;
                        }

                        // TODO:
                        @continue = false;

                        break;
                    case Glow glow:
                        intensity *= glow.Intensity;
                        @continue = false;

                        break;
                    case Reflective reflective:
                        color *= 1 - reflective.Reflectiveness;
                        luminance = reflective.Reflectiveness;
                        dir = dir.Reflect(normal);

                        break;
                    case Refractive refractive:
                        color *= 1 - refractive.Refractiveness;

                        for (int i = 0; i < 2; ++i)
                        {
                            if ((inside ^= true) != ray.Inside)
                                eta = inside ? eta.ComponentwiseMultiply(refractive.RefractiveIndex) : eta.ComponentwiseDivide(refractive.RefractiveIndex);

                            if (dir.Refract(-normal, eta[i], out Vector3 rdir))
                                ray = new Ray(pos + EPSILON * rdir, rdir, eta, inside, ray.Depth + 1);
                            else
                            {
                                rdir = dir.Reflect(-normal);
                                ray = new Ray(pos + EPSILON * rdir, rdir, eta, true, ray.Depth + 1);
                            }

                            color += Trace(ray)[i] * refractive.Refractiveness;
                        }

                        // TODO : fresnel effect

                        luminance = refractive.Refractiveness;
                        @continue = false;

                        break;
                    case Specular specular:
                        color = CalculateSpecularLights(pos, dir, specular.SpecularIndex);
                        @continue = false;

                        // TODO : more stuff

                        break;
                    default:
                        return default;
                        // throw new ArgumentOutOfRangeException();
                }

                if (shape.Material.Opacity < 1 - EPSILON)
                {
                    color *= shape.Material.Opacity;
                    color += (1 - shape.Material.Opacity) * Trace(new Ray(ray.Evaluate(EPSILON), ray.Direction, eta, inside, ray.Depth + 1));
                }

                result += color * intensity;
                intensity *= luminance;
                dir = dir.Normalized;
                ray = new Ray(pos + dir * EPSILON, dir, eta, inside, ray.Depth + 1);
            }

            if (MODE == RenderMode.RAY_DIR)
                return ray.Direction / 2 + .5;
            else if (MODE == RenderMode.RAY_DEPTH)
                return new Vector3(ray.Depth / (double)MAX_DEPTH);

            return result;
        }
#pragma warning restore

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 CalculateSpecularLights(in Vector3 dir, in Vector3 position, double index)
        {
            Vector3 col = AMBIENT;

            foreach (Light light in _lights)
            {
                (Vector3 ldir, double dist) = light.GenerateShadowRay(position);
                Ray ray = new Ray(position + EPSILON * ldir, ldir, default, default);
                Scalar θ = ldir * dir;

                if (θ.IsNegative)
                    continue;

                if (_objects.Any(o => o.Intersect(ray) is (var d, _) && d > 0 && d < dist))
                    continue;

                θ ^= index;
                col += light.GetIntensity(-dir, dist) * light.Material.PremultipliedColor * θ;
            }

            return col;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 CalculateLights(in Vector3 position)
        {
            Vector3 col = AMBIENT;

            foreach (Light light in _lights)
            {
                (Vector3 dir, double dist) = light.GenerateShadowRay(position);
                Ray ray = new Ray(position + EPSILON * dir, dir, default, default);

                if (_objects.Any(o => o.Intersect(ray) is (var d, _) && d > 0 && d < dist))
                    continue;

                col += light.GetIntensity(-dir, dist) * light.Material.PremultipliedColor;
            }

            return col;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 CalculateDiffuse(in Vector3 position, Diffuse material) => CalculateLights(position).ComponentwiseMultiply(material.Color);


        //public static Vector3 Refract(Vector3 dir, Vector3 normal, double eta_in, double eta_out, out double pr)
        //{
        //    bool out_to_in = normal * dir < 0;
        //    Vector3 normal_l = out_to_in ? normal : -normal;



        //}




    }
}
