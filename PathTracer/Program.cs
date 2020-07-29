//#define PARALLEL

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO.MemoryMappedFiles;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using System.Text;
using System.Linq;
using System.IO;
using System;

using Unknown6656.Mathematics.LinearAlgebra;
using Unknown6656.Mathematics.Numerics;
using Unknown6656.Mathematics;
using Unknown6656.Common;

namespace PathTracer
{
    public static unsafe partial class Program
    {
        public const string PNG_PATH = "./render.png";
        public const string SCENE_PATH = "./scene.txt";
        public const string NETPBM_PATH = "./render.pbm";
        public const string MMF_NAME = "path_tracer.mmf";
        public const ConsoleKey KEY_EXIT = ConsoleKey.Escape;

        private static readonly XorShift _rng = new XorShift();
        private static readonly Shape[] _objects;
        private static readonly Light[] _lights;
        private static readonly Shape[] _scene;
        private static readonly int[] _idx;


        static Program()
        {
            _scene = ReadScene(File.ReadAllText(SCENE_PATH));
            _lights = _scene.FilterType<Shape, Light>().ToArray();
            _objects = _scene.ToArrayWhere(o => !(o is Light));
            _idx = Enumerable.Range(0, WIDTH * HEIGHT).ToArray();
            _idx.Shuffle();
        }

        public static void Main(string[] _)
        {
            Stopwatch sw = new Stopwatch();

            sw.Start();

            FileInfo viewer = new FileInfo("./viewer.exe");
            int mmf_size = sizeof(COLOR) * WIDTH * HEIGHT;
            int mmf_offset = sizeof(int);

            foreach (Process proc in Process.GetProcesses())
                try
                {
                    if (proc.MainModule.FileName.Equals(viewer.FullName, StringComparison.InvariantCultureIgnoreCase))
                        proc.Kill();
                }
                catch
                {
                }

            using (var mmf = MemoryMappedFile.CreateOrOpen(MMF_NAME, mmf_offset + mmf_size, MemoryMappedFileAccess.ReadWrite))
            using (var fin = mmf.CreateViewAccessor(0, mmf_offset))
            using (var acc = mmf.CreateViewAccessor(mmf_offset, mmf_size))
            using (var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = viewer.FullName,
                    Arguments = $"\"{MMF_NAME}\" \"{PNG_PATH}\" {mmf_size:x8} {mmf_offset:x8} {WIDTH:x8} {HEIGHT:x8} {(int)KEY_EXIT:x8}"
                },
            })
            {
                Thread.Sleep(500);

                proc.Start();

                bool finished = false;
                byte* ptr = null;

                acc.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);

                fin.Write(0, false);
#if PARALLEL
                Console.WriteLine($"Threads:                {Environment.ProcessorCount}");
#else
                Console.WriteLine($"Threads:                1");
#endif
                Console.WriteLine($@"
Resolution (in pixels): {WIDTH}x{HEIGHT}
Subpixels per pixel:    {SUBPIXELS}x{SUBPIXELS}
Samples per subpixel:   {SAMPLES}
Maximum ray depth:      {MAX_DEPTH}
Objects in scene:       {_scene.Length}
Minimum ray count:      {(decimal)WIDTH * HEIGHT * SUBPIXELS * SUBPIXELS * SAMPLES:N0}
Maximum ray count:      {(decimal)WIDTH * HEIGHT * SUBPIXELS * SUBPIXELS * SAMPLES * MAX_DEPTH:N0}

Generating rays ...".Trim());

                using Task t = Task.Factory.StartNew(delegate
                {
                    while (!finished)
                        save_netpbm(ptr);
                });

                Render((COLOR*)ptr);

                finished = true;

                fin.Write(0, true);
                t.Wait();

                save_netpbm(ptr);

                sw.Stop();

                Console.WriteLine($@"
Generated {Ray.TotalGenerationCount:N0} rays.
Calculation time: {TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds)} (hh:mm:ss.f)
----------------------------------------------------------------------------------------------------

Press [{KEY_EXIT}] to close the preview window.".Trim());

                while (true)
                    try
                    {
                        do
                            try
                            {
                                if (Console.KeyAvailable && Console.ReadKey(true).Key == KEY_EXIT)
                                {
                                    proc.Kill();

                                    return;
                                }

                                if (proc.ExitTime < DateTime.Now)
                                    goto end;
                            }
                            catch
                            {
                            }
                            finally
                            {
                                Thread.Sleep(50);
                            }
                        while (!proc.Responding || !proc.HasExited);
                    }
                    catch
                    {
                        goto end;
                    }
end:
                Console.WriteLine("\nexiting...");
            }
        }

        public static Shape[] ReadScene(string raw)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            List<Shape> shapes = new List<Shape>();
            bool inshape = false;
            int linenr = 0;
            Match m;

            foreach ((string line, int nr) in from l in $"{raw}\n[EOF]".Split(new[] { '\n' }, StringSplitOptions.None)
                                              let lt = Regex.Replace(l, @"//.*$", "", RegexOptions.Compiled).Trim()
                                              let ln = ++linenr
                                              where lt.Length > 0
                                              select (lt, ln))
            {
                ArgumentException error(string msg) => new ArgumentException($"Parser error on line no. {nr}: {msg}", nameof(raw));
                Color parse_argb() => uint.Parse(m.Groups["argb"].ToString(), NumberStyles.HexNumber);
                Vector3 parse_v3() => new Vector3(parse_d("x"), parse_d("y"), parse_d("z"));
                double parse_d(string name) => double.Parse(m.Groups[name].ToString());

                if (line.Match(@"^(plane|triangle|sphere|spot|point|\[EOF\])$", out m))
                {
                    if (properties.TryGetValue("shape", out object val))
                        if (!(properties.TryGetValue("c", out object obj) && obj is Material mat))
                            throw error("No material or color has been defined for the previous geometry.");
                        else if (!(properties.TryGetValue("p", out obj) && obj is List<Vector3> vecs && vecs.Count > 0))
                            throw error("The previous geometry must have at least one position vector defined.");
                        else
                            try
                            {
                                shapes.Add(val.ToString() switch
                                {
                                    "plane" => new Plane(vecs[0], vecs[1], vecs[2], vecs[3], mat) as Shape,
                                    "triangle" => new Triangle(vecs[0], vecs[1], vecs[2], mat),
                                    "sphere" => new Sphere(vecs[0], (double)properties["r"], mat),
                                    "spot" => new SpotLight(vecs[0], (Vector3)properties["d"], mat._color, (mat as Glow)!.Intensity, (double)properties["f"], (double)properties["s"]),
                                    "point" => new PointLight(vecs[0], mat._color, (mat as Glow)!.Intensity, (double)properties["f"]),
                                    _ => throw new NotImplementedException()
                                });
                            }
                            catch (Exception ex)
                            {
                                throw error("Some error occured while committing the previous geometry: " + ex);
                            }

                    properties.Clear();
                    inshape = true;

                    if ((properties["shape"] = m.ToString().ToLower()) == "[eof]")
                        break;

                    continue;
                }

                if (!inshape)
                    throw error($"The property '{line}' is not associated with any geomerty.");

                if (line.Match(@"^P\s+(?<x>[^\s,]+),?\s+(?<y>[^\s,]+),?\s+(?<z>[^\s,]+)$", out m))
                {
                    if (!properties.ContainsKey("p"))
                        properties["p"] = new List<Vector3>();

                    (properties["p"] as List<Vector3>)!.Add(parse_v3());
                }
                else if (line.Match(@"^D\s+(?<x>[^\s,]+),?\s+(?<y>[^\s,]+),?\s+(?<z>[^\s,]+)$", out m))
                    properties["d"] = parse_v3();
                else if (line.Match(@"^(?<l>[RFS])\s+(?<v>[^\s]+)$", out m))
                    properties[m.Groups["l"].ToString().ToLower()] = parse_d("v");
                else if (line.Match(@"^CD?\s+#(?<argb>[0-9a-f]+)$", out m))
                    properties["c"] = (Diffuse)parse_argb();
                else if (line.Match(@"^CR\s+#(?<argb>[0-9a-f]+),?\s+(?<a>[^\s]+)$", out m))
                    properties["c"] = new Reflective(parse_d("a"), (Diffuse)parse_argb());
                else if (line.Match(@"^CG\s+#(?<argb>[0-9a-f]+),?\s+(?<a>[^\s]+)$", out m))
                    properties["c"] = new Glow(parse_argb(), parse_d("a"));
                else if (line.Match(@"^CS\s+#(?<argb>[0-9a-f]+),?\s+(?<a>[^\s,]+),?\s+(?<i>[^\s]+)$", out m))
                    properties["c"] = new Specular(parse_argb(), parse_d("a"), parse_d("i"));
                else if (line.Match(@"^CT\s+#(?<argb>[0-9a-f]+),?\s+(?<a>[^\s,]+),?\s+(?<x>[^\s,]+),?\s+(?<y>[^\s,]+),?\s+(?<z>[^\s,]+)$", out m))
                    properties["c"] = new Refractive(parse_d("a"), parse_v3(), (Diffuse)parse_argb());
                else
                    throw error($"Unknown property or geometry identifier '{line}'.");
            }

            return shapes.ToArray();
        }

#pragma warning disable
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PixelLoop_Stripes(Action<int> func)
        {
            const int mod = 3;

            if (PARALLEL)
            {
                const int size = WIDTH * HEIGHT / mod;

                for (int i = 0; i < mod; ++i)
                {
                    int offs = i * size;

                    Parallel.For(offs, offs + (i < mod - 1 ? size : WIDTH * HEIGHT - (mod - 1) * size), func);
                }
            }
            else
            {
                int x, y;

                for (int m = 0, i; m < mod; ++m)
                    for (i = WIDTH * HEIGHT - m - 1; i >= 0; i -= mod)
                        func(i);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PixelLoop_Simple(Action<int> func)
        {
            if (PARALLEL)
                Parallel.For(0, WIDTH * HEIGHT, func);
            else
                for (int i = 0; i < WIDTH * HEIGHT; ++i)
                    func(i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PixelLoop_Random(Action<int> func)
        {
            if (PARALLEL)
                Parallel.For(0, WIDTH * HEIGHT, i => func(_idx[i]));
            else
                for (int i = 0; i < WIDTH * HEIGHT; ++i)
                    func(_idx[i]);
        }
#pragma warning restore

        private static void save_netpbm(byte* ptr)
        {
            StringBuilder sb = new StringBuilder().Append($"P3\n{WIDTH} {HEIGHT}\n255\n");

            for (int y = 0; y < HEIGHT; ++y)
            {
                for (int x = 0; x < WIDTH; ++x)
                    for (int c = 2; c >= 1; --c)
                        sb.Append($"{ptr[(y * WIDTH + x) * 3 + c]} ");

                sb.Append('\n');
            }

            lock (_rng)
                File.WriteAllText(NETPBM_PATH, sb.ToString());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Render(COLOR* colors)
        {
            Vector3 gaze = (LOOK_AT - EYE).Normalized;
            Vector3 cx = (WIDTH * FOV / HEIGHT, 0, 0);
            Vector3 cy = cx.Cross(gaze).Normalized * FOV;
            double γ = 1 / GAMMA;

            (Vector3 color, int count)[] pixels = new (Vector3, int)[WIDTH * HEIGHT];
            void write_back(int i)
            {
                Vector3 clr = pixels[i].color / pixels[i].count;

                colors[i].R = (byte)(0xff * Math.Pow(clr.X, γ).Clamp());
                colors[i].G = (byte)(0xff * Math.Pow(clr.Y, γ).Clamp());
                colors[i].B = (byte)(0xff * Math.Pow(clr.Z, γ).Clamp());
            }

            void func(int i)
            {
                int x = i % WIDTH;
                int y = i / WIDTH;
                double u1, u2, dx, dy;
                Vector3 clr = default;
                Vector3 d;

                for (int si = 0, sx, sy; si < SUBPIXELS * SUBPIXELS; ++si)
                {
                    sx = si % SUBPIXELS;
                    sy = si / SUBPIXELS;

                    u1 = 2 * _rng.NextFloat();
                    u2 = 2 * _rng.NextFloat();
                    dx = u1 < 1 ? Math.Sqrt(u1) - 1 : 1 - Math.Sqrt(2 - u1);
                    dy = u2 < 1 ? Math.Sqrt(u2) - 1 : 1 - Math.Sqrt(2 - u2);
                    dx = (sx + .5 + dx) / 2;
                    dy = (sy + .5 + dy) / 2;

                    d = cx * ((dx + x) / WIDTH - .5);
                    d += cy * ((dy + y) / HEIGHT - .5);
                    d += gaze;

                    clr += Trace(new Ray(EYE + d * CAM_DIST, d.Normalized, (ETA_AIR, ETA_AIR, ETA_AIR), false));
                }

                clr /= SUBPIXELS * SUBPIXELS;
                i = (HEIGHT - 1 - y) * WIDTH + x;

                pixels[i].color += clr.Clamp();
                pixels[i].count++;

                write_back(i);
            }

            for (int s = 0; s < SAMPLES; ++s)
            {
                if (s == 0)
                    PixelLoop_Random(func);
                else
                    PixelLoop_Stripes(func);

                PixelLoop_Simple(write_back);
            }
        }

        public static bool Match(this string s, string p, out Match m, RegexOptions o = RegexOptions.Compiled | RegexOptions.IgnoreCase) => (m = Regex.Match(s, p, o)).Success;


        [NativeCppClass, StructLayout(LayoutKind.Sequential)]
        private struct COLOR
        {
            public byte B;
            public byte G;
            public byte R;
        }
    }
}
