
///////////////////////////////////////////////////////////////////
/////////////////////////// BACKUP FILE ///////////////////////////
///////////////////////////////////////////////////////////////////


#define PARALLEL
// #define HIGH_QUALITY

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using System.IO;
using System;

using MathLibrary.LinearAlgebra;
using MathLibrary.Numerics;
using MathLibrary;

namespace PathTracer
{
    public enum RenderMode
    {
        COLOR,
        DEPTH,
        NORMAL,
    }

    public static unsafe class Program
    {
#if HIGH_QUALITY
        public const int MAX_DEPTH = 2048;
        public const int WIDTH = 1920;
        public const int HEIGHT = 1080;
        public const int SUBPIXELS = 5;
        public const int SAMPLES = 128;
#else
        public const int MAX_DEPTH = 32;
        public const int WIDTH = 320;
        public const int HEIGHT = 180;
        public const int SUBPIXELS = 1;
        public const int SAMPLES = 8;
#endif
        public const RenderMode MODE = RenderMode.COLOR;
        public static readonly Vector3 EYE = (-50, 100, 300);
        public static readonly Vector3 LOOK_AT = (10, 30, 0);
        public const double CAM_DIST = 130;
        public const double FOV = 0.5135;

        public const double REFRACTIVE_INDEX_OUT = 1.0;
        public const double REFRACTIVE_INDEX_IN = 1.5;

        private static readonly XorShift _rng = new XorShift();
        public static readonly Shape[] _scene =
        {
            new Plane( // back
                (-100, 0, -100),
                (+100, 0, -100),
                (+100, 100, -100),
                (-100, 100, -100),
                new Material(SurfaceType.DIFFUSE, (.5, .5, 1))
            ),
            new Plane( // left
                (-100, 0, -100),
                (-100, 0, +100),
                (-100, 100, +100),
                (-100, 100, -100),
                new Material(SurfaceType.DIFFUSE, (1, .5, .5))
            ),
            new Plane( // right
                (+100, 0, -100),
                (+100, 0, +100),
                (+100, 100, +100),
                (+100, 100, -100),
                new Material(SurfaceType.DIFFUSE, (.5, 1, .5))
            ),
            new Plane( // bottom
                (-100, 0, +100),
                (+100, 0, +100),
                (+100, 0, -100),
                (-100, 0, -100),
                new Material(SurfaceType.DIFFUSE, (.5, .5, .5))
            ),

            new Sphere((-60, 35, 0),          35, new Material(SurfaceType.SPECULAR,   (1, .2, .2), 4)),
            new Sphere((10, 25, 0),            25, new Material(SurfaceType.SPECULAR,   new Vector3(.999), 1)), //Mirror
            new Sphere((60, 20, 0),           20, new Material(SurfaceType.REFRACTIVE, new Vector3(.999), 1)), //Glass

            // new Sphere((0, 600, 100), 600,  new Material(SurfaceType.DIFFUSE,    default,  12)) //Light
        };


        public static void Main(string[] _)
        {
            //foreach (var nfo in typeof(Program).Assembly.DefinedTypes.SelectMany(t => t.GetMethods()).ToArray())
            //    if (!nfo.IsGenericMethod && !nfo.DeclaringType.IsGenericType)
            //        RuntimeHelpers.PrepareMethod(nfo.MethodHandle);

            Vector3 gaze = (LOOK_AT - EYE).Normalized;
            Vector3 cx = new Vector3(WIDTH * FOV / HEIGHT, 0, 0);
            Vector3 cy = cx.Cross(gaze).Normalized * FOV;
            COLOR[] cols = new COLOR[WIDTH * HEIGHT];

            Console.WriteLine($"generating {(decimal)WIDTH * HEIGHT * SUBPIXELS * SUBPIXELS * SAMPLES:N0} to {(decimal)WIDTH * HEIGHT * SUBPIXELS * SUBPIXELS * SAMPLES * MAX_DEPTH:N0} rays ...");

            fixed (COLOR* ptr = cols)
            {
                byte* bptr = (byte*)ptr;

                Render(EYE, gaze, cx, cy, 2.2, ptr);

                var sb = new StringBuilder().Append($"P3\n{WIDTH} {HEIGHT}\n255\n");

                for (int y = 0; y < HEIGHT; ++y)
                {
                    for (int x = 0; x < WIDTH; ++x)
                        for (int c = 0; c < 3; ++c)
                            sb.Append($"{bptr[(y * WIDTH + x) * 3 + c]} ");

                    sb.Append('\n');
                }

                File.WriteAllText("render.pbm", sb.ToString());
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Render(Vector3 eye, Vector3 gaze, Vector3 cx, Vector3 cy, double γ, COLOR* colors)
        {
            γ = 1 / γ;
#if PARALLEL
            Parallel.For(0, WIDTH * HEIGHT, i =>
#else
            for (int i = 0; i < WIDTH * HEIGHT; ++i)
#endif
            {
                int x = i % WIDTH;
                int y = i / WIDTH;
                double u1, u2, dx, dy;
                Vector3 clr = default;
                Vector3 L, d;

                for (int si = 0, sx, sy, s; si < SUBPIXELS * SUBPIXELS; ++si)
                {
                    sx = si % SUBPIXELS;
                    sy = si / SUBPIXELS;
                    L = default;

                    for (s = 0; s < SAMPLES; ++s)
                    {
                        u1 = 2 * _rng.NextFloat();
                        u2 = 2 * _rng.NextFloat();
                        dx = u1 < 1 ? Math.Sqrt(u1) - 1 : 1 - Math.Sqrt(2 - u1);
                        dy = u2 < 1 ? Math.Sqrt(u2) - 1 : 1 - Math.Sqrt(2 - u2);
                        dx = (sx + .5 + dx) / 2;
                        dy = (sy + .5 + dy) / 2;

                        d = cx * ((dx + x) / WIDTH - .5);
                        d += cy * ((dy + y) / HEIGHT - .5);
                        d += gaze;

                        L += Trace(new Ray(eye + d * CAM_DIST, d.Normalized));
                    }

                    L /= SAMPLES;
                    clr += L.Clamp();
                }

                clr /= SUBPIXELS * SUBPIXELS;
                i = (HEIGHT - 1 - y) * WIDTH + x;

                colors[i].R = (byte)(0xff * Math.Pow(clr.X, γ).Clamp());
                colors[i].G = (byte)(0xff * Math.Pow(clr.Y, γ).Clamp());
                colors[i].B = (byte)(0xff * Math.Pow(clr.Z, γ).Clamp());
            }
#if PARALLEL
            );
#endif
        }

#pragma warning disable CS0162 // unreachable code
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Trace(Ray ray)
        {
            Vector3 color = default;
            double luminance = 1;

            while (ray.Depth < MAX_DEPTH)
                if ((from o in _scene
                     let ints = o.Intersect(ray)
                     where ints.HasValue
                     let d = ints!.Value.distance
                     orderby d ascending
                     select (o, d, ints!.Value.inside)).ToArray()
                     is var intersections && intersections.Length > 0)
                {
                    (Shape shape, double T, bool inside) = intersections[0];
                    Vector3 pos = ray.Evaluate(T);
                    Vector3 normal = shape.NormalAt(pos);
                    Material mat = shape.Material;

                    if (MODE == RenderMode.DEPTH)
                        return new Vector3(1 - T / (EYE - LOOK_AT).Length);
                    else if (MODE == RenderMode.NORMAL)
                        return normal;

                    if (mat.SurfaceType == SurfaceType.DIFFUSE)
                    {
                        color += luminance * mat.Color;
                        luminance *= mat.Luminance;
                    }

                    if (ray.Depth > 4)
                    {
                        if (_rng.NextFloat() / 2 > mat.Luminance)
                            return color;

                        luminance /= mat.Luminance;
                    }

                    Vector3 dir = ray.Direction;

                    switch (mat.SurfaceType)
                    {
                        case SurfaceType.SPECULAR:
                            {
                                dir -= 2 * (double)(normal * dir) * normal;

                                break;
                            }
                        case SurfaceType.REFRACTIVE:
                            {
                                dir = IdealSpecularTransmit(dir, normal, REFRACTIVE_INDEX_OUT, REFRACTIVE_INDEX_IN, out double pr);
                                luminance *= pr;

                                break;
                            }
                        default:
                            {
                                return color;

                                Vector3 w = (normal * dir).IsNegative ? normal : -normal;
                                Vector3 u = (Math.Abs(w.X) > .1 ? Vector3.UnitY : Vector3.UnitX).Cross(w).Normalized;
                                Vector3 v = w.Cross(u);

                                (double x, double y, double z) = BRDF_cosine(_rng.NextFloat(), _rng.NextFloat());

                                dir = (x * u + y * v + z * w).Normalized;

                                break;
                            }
                    }

                    ray = new Ray(pos, dir, ray.Depth + 1);
                }
                else
                    break;

            return color;
        }
#pragma warning restore

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 IdealSpecularTransmit(Vector3 d, Vector3 n, double n_out, double n_in, out double pr)
        {
            Vector3 d_Re = d - 2 * (double)(n * d) * n;

            bool out_to_in = n.Dot(d) < 0;
            Vector3 nl = out_to_in ? n : -n;

            double cos_theta = d.Dot(nl);
            double nn = out_to_in ? n_out / n_in : n_in / n_out;
            double cos2_phi = 1.0 - nn * nn * (1 - cos_theta * cos_theta);

            // Total Internal Reflection
            if (cos2_phi < 0)
            {
                pr = 1.0;

                return d_Re;
            }

            Vector3 d_Tr = (nn * d - nl * (nn * cos_theta + Math.Sqrt(cos2_phi))).Normalized;
            double c = 1 - (out_to_in ? -cos_theta : (double)d_Tr.Dot(n));

            double Re = SchlickReflectance(n_out, n_in, c);
            double p_Re = .25 + .5 * Re;

            if (_rng.NextFloat() < p_Re)
            {
                pr = Re / p_Re;

                return d_Re;
            }
            else
            {
                (double Tr, double p_Tr) = (1.0 - Re, 1.0 - p_Re);

                pr = Tr / p_Tr;

                return d_Tr;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Reflectance0(double n1, double n2)
        {
            double sqrt_R0 = (n1 - n2) / (n1 + n2);
            return sqrt_R0 * sqrt_R0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SchlickReflectance(double n1, double n2, double c)
        {
            double R0 = Reflectance0(n1, n2);
            return R0 + (1 - R0) * c * c * c * c * c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 BRDF_uniform(double u1, double u2)
        {
            double sin_theta = Math.Sqrt(Math.Max(0, 1 - u1 * u1));
            double phi = 2 * Math.PI * u2;

            return (Math.Cos(phi) * sin_theta, Math.Sin(phi) * sin_theta, u1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 BRDF_cosine(double u1, double u2)
        {
            double cos_theta = Math.Sqrt(1 - u1);
            double sin_theta = Math.Sqrt(u1);
            double phi = 2 * Math.PI * u2;

            return (Math.Cos(phi) * sin_theta, Math.Sin(phi) * sin_theta, cos_theta);
        }


        [NativeCppClass, StructLayout(LayoutKind.Sequential)]
        private struct COLOR
        {
            public byte R;
            public byte G;
            public byte B;
        }
    }
}
