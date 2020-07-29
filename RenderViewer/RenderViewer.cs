using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.IO.MemoryMappedFiles;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using System.Drawing;
using System.Linq;
using System;
using System.IO;
using System.Linq.Expressions;


namespace PathTracer.Viewer
{
    public static unsafe class RenderViewer
    {
        [DllImport("User32.dll")]
        private static extern int SetForegroundWindow(IntPtr hwnd);

        public static void Main(string[] args)
        {
            if (args.Length > 6)
                try
                {
                    string mmf_path = args[0];
                    string png_path = args[1];
                    int mmf_size = int.Parse(args[2], NumberStyles.HexNumber);
                    int mmf_offset = int.Parse(args[3], NumberStyles.HexNumber);
                    int width = int.Parse(args[4], NumberStyles.HexNumber);
                    int height = int.Parse(args[5], NumberStyles.HexNumber);
                    int exit_key = int.Parse(args[6], NumberStyles.HexNumber);
                    bool finished = false;

                    using (var mmf = MemoryMappedFile.OpenExisting(mmf_path, MemoryMappedFileRights.Read))
                    using (var fin = mmf.CreateViewAccessor(0, mmf_offset, MemoryMappedFileAccess.Read))
                    using (var acc = mmf.CreateViewAccessor(mmf_offset, mmf_size, MemoryMappedFileAccess.Read))
                    using (var frm = new F
                    {
                        Width = width + 16,
                        Height = height + 48,
                        Text = "Path Tracer",
                        BackColor = Color.Black,
                        Location = Screen.AllScreens.Last().Bounds.Location,
                        StartPosition = FormStartPosition.Manual,
                    })
                    using (var task = Task.Factory.StartNew(delegate
                    {
                        byte* file = null;

                        acc.SafeMemoryMappedViewHandle.AcquirePointer(ref file);

                        using Bitmap bmp = new Bitmap(width, height, width * sizeof(COLOR), PixelFormat.Format24bppRgb, (IntPtr)file);

                        void update_img() => frm.Invoke(new MethodInvoker(() =>
                        {
                            frm.BackgroundImage = bmp;
                            frm.BackgroundImage.Save(png_path);
                        }));

                        do
                        {
                            update_img();

                            Application.DoEvents();

                            fin.Read(0, out finished);
                        }
                        while (!finished);

                        Thread.Sleep(500);
                        Application.DoEvents();

                        update_img();

                        frm.Invoke(new MethodInvoker(delegate
                        {
                            frm.BackgroundImage = frm.BackgroundImage.Clone() as Image;
                            frm.Text += "   [[ FINISHED ]]";
                        }));

                        acc.SafeMemoryMappedViewHandle.ReleasePointer();
                    }))
                    {
                        frm.Show();
                        frm.Focus();
                        frm.WindowState = FormWindowState.Maximized;
                        frm.KeyDown += (_, e) =>
                        {
                            if (e.KeyCode == (Keys)exit_key)
                            {
                                finished = true;
                                frm.Close();
                            }
                        };

                        while (!finished)
                            Application.DoEvents();

                        frm.Visible = false;
                        frm.ShowDialog();
                    }
                }
                catch (ObjectDisposedException)
                {
                }
        }

        private class F : Form
        {
            private readonly PB _pic;


            protected override bool DoubleBuffered
            {
                get => base.DoubleBuffered;
                set => base.DoubleBuffered = value;
            }

            public override Image BackgroundImage
            {
                get => _pic.Image;
                set => _pic.Image = value;
            }

            public F()
            {
                DoubleBuffered = true;

                Controls.Add(_pic = new PB
                {
                    Dock = DockStyle.Fill,
                    SizeMode = PictureBoxSizeMode.Zoom,
                });
            }

            private class PB
                : PictureBox
            {
                protected override bool DoubleBuffered
                {
                    get => base.DoubleBuffered;
                    set => base.DoubleBuffered = value;
                }

                public PB() => DoubleBuffered = true;

                protected override void OnPaint(PaintEventArgs e)
                {
                    e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;

                    base.OnPaint(e);
                }
            }
        }
    }

    [NativeCppClass, StructLayout(LayoutKind.Sequential)]
    internal struct COLOR
    {
        public byte B;
        public byte G;
        public byte R;
    }
}
