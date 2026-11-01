using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SpiroLab.Ui
{
    /// <summary>
    /// Draws the two curves a spirometry report needs:
    /// flow/volume and volume/time, stacked or side by side as space allows.
    /// </summary>
    public sealed class ChartPanel : Control
    {
        private Trial _trial;
        private AnalysisResult _result;
        private int _visible = -1;          // -1 = everything
        private IList<Trial> _comparisonTrials=new List<Trial>();
        private readonly List<Expiration> _comparisons=new List<Expiration>();
        public IList<Trial> ComparisonTrials { get { return _comparisonTrials; } set { _comparisonTrials=value??new List<Trial>(); Invalidate(); } }
        public bool SideBySide { get { return Height<340*Font.Size/9f && Width>=600*Font.Size/9f; } }

        private static readonly Color Ink = Color.FromArgb(60, 66, 78);
        private static readonly Color Grid = Color.FromArgb(228, 232, 238);
        private static readonly Color Curve = Color.FromArgb(19,103,119);
        private static readonly Color CurveFill = Color.FromArgb(230,242,245);
        private static readonly Color Marker = Color.FromArgb(94,111,123);

        public ChartPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Font = new Font("Tahoma", 8F);
        }

        public Trial Trial
        {
            get { return _trial; }
            set { _trial = value; _visible = -1; Invalidate(); }
        }

        public AnalysisResult Result
        {
            get { return _result; }
            set { _result = value; Invalidate(); }
        }

        public int VisibleSamples
        {
            get { return _visible; }
            set { _visible = value; Invalidate(); }
        }

        // ------------------------------------------------------------------
        private Expiration _signal;
        private double[] BuildVolume(out int count)
        {
            count = 0;
            if (_trial == null) return new double[0];
            Trial data = _trial;
            if (_visible >= 0 && _visible < data.Flow.Count)
            {
                data = new Trial { Dt = _trial.Dt };
                data.Flow.AddRange(_trial.Flow.GetRange(0, _visible));
                if (_trial.Time.Count == _trial.Flow.Count) data.Time.AddRange(_trial.Time.GetRange(0, _visible));
                if (_trial.Volume.Count == _trial.Flow.Count) data.Volume.AddRange(_trial.Volume.GetRange(0, _visible));
            }
            _signal = Expiration.From(data);
            count = _signal.Count;
            return _signal.Volume;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            Rectangle area = ClientRectangle;
            if (area.Width < 120 || area.Height < 120) return;

            int split = area.Height * 55 / 100;
            Rectangle top = new Rectangle(area.Left + 8, area.Top + 8, area.Width - 16, split - 12);
            Rectangle bottom = new Rectangle(area.Left + 8, area.Top + split + 8, area.Width - 16, area.Height - split - 16);
            if(SideBySide)
            {
                int half=(area.Width-24)/2;
                top=new Rectangle(area.Left+8,area.Top+8,half,area.Height-16);
                bottom=new Rectangle(area.Left+half+16,area.Top+8,half,area.Height-16);
            }

            if (_trial == null || _trial.Flow.Count < 2)
            {
                DrawEmpty(g, area);
                return;
            }

            int count;
            double[] vol = BuildVolume(out count);
            if (vol.Length < 2) { DrawEmpty(g, area); return; }

            double fvc = 0, pef = 0, fif = 0, fet = _signal.Time[count - 1];
            for (int i = 0; i < count; i++)
            {
                if (vol[i] > fvc) fvc = vol[i];
                if (_signal.Flow[i] > pef) pef = _signal.Flow[i];
                if (_signal.Flow[i] < fif) fif = _signal.Flow[i];
            }
            if (fvc < 0.2) fvc = 0.2;
            if (pef < 0.5) pef = 0.5;
            _comparisons.Clear();
            if(_visible<0) foreach(Trial t in _comparisonTrials)
            {
                if(t==_trial) continue; Expiration other=Expiration.From(t); if(other.Count<2) continue; _comparisons.Add(other);
                for(int i=0;i<other.Count;i++) { fvc=Math.Max(fvc,other.Volume[i]); pef=Math.Max(pef,other.Flow[i]); }
                fet=Math.Max(fet,other.Time[other.Count-1]);
            }

            DrawFlowVolume(g, top, vol, count, fvc, pef, fif);
            DrawVolumeTime(g, bottom, vol, count, fvc, fet);
        }

        private void DrawEmpty(Graphics g, Rectangle area)
        {
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                using (Brush b = new SolidBrush(Color.FromArgb(160, 168, 180)))
                    g.DrawString("منحنی بازدم پس از شروع فوت رسم می‌شود", Font, b, area, sf);
            }
        }

        // ------------------------------------------------------------------
        private void DrawFlowVolume(Graphics g, Rectangle r, double[] vol, int count,
                                    double fvc, double pef, double fif)
        {
            double xMax = fvc * 1.08;
            double yMax = pef * 1.15;
            double yMin = Math.Min(fif * 1.2, -1.0);
            if (fif > -0.2) yMin = -0.5;

            Rectangle plot = PlotArea(r, 52, 12, 18, 30);
            DrawFrame(g, plot, "حجم (لیتر)", "جریان (لیتر/ثانیه)", 0, xMax, yMin, yMax, 5, 4);

            PointF[] pts = new PointF[count];
            for (int i = 0; i < count; i++)
                pts[i] = new PointF(
                    MapX(vol[i], plot, 0, xMax),
                    MapY(_signal.Flow[i], plot, yMin, yMax));

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddLines(pts);
                PointF last = pts[count - 1];
                path.AddLine(last.X, pts[0].Y, pts[0].X, pts[0].Y);
                using (Brush fill = new SolidBrush(CurveFill)) g.FillPath(fill, path);
            }

            DrawComparisons(g,plot,0,xMax,yMin,yMax,false);
            using (Pen pen = new Pen(Curve, 2.2f))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, pts);
            }

            if (_result != null && _result.Valid)
            {
                // PEF marker
                double px = MapX(0, plot, 0, xMax);
                double py = MapY(_result.Pef, plot, yMin, yMax);
                using (Pen pen = new Pen(Marker, 1.2f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(pen, plot.Left, (float)py, plot.Right, (float)py);
                }
                using (Brush b = new SolidBrush(Marker))
                    g.DrawString("PEF " + Fmt.N(_result.Pef, 2), Font, b, plot.Left + 4, (float)py - 15);
            }
        }

        // ------------------------------------------------------------------
        private void DrawVolumeTime(Graphics g, Rectangle r, double[] vol, int count,
                                    double fvc, double fet)
        {
            double xMax = Math.Max(6.0, fet * 1.05);
            double yMax = fvc * 1.10;

            Rectangle plot = PlotArea(r, 52, 12, 18, 30);
            DrawFrame(g, plot, "زمان (ثانیه)", "حجم (لیتر)", 0, xMax, 0, yMax, 6, 4);

            PointF[] pts = new PointF[count];
            for (int i = 0; i < count; i++)
                pts[i] = new PointF(
                    MapX(_signal.Time[i], plot, 0, xMax),
                    MapY(vol[i], plot, 0, yMax));

            DrawComparisons(g,plot,0,xMax,0,yMax,true);
            using (Pen pen = new Pen(Curve, 2.2f))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, pts);
            }

            if (_result != null && _result.Valid)
            {
                // FEV1 marker
                float x1 = (float)MapX(1.0, plot, 0, xMax);
                float y1 = (float)MapY(_result.Fev1, plot, 0, yMax);
                using (Pen pen = new Pen(Marker, 1.2f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(pen, x1, y1, x1, plot.Bottom);
                    g.DrawLine(pen, plot.Left, y1, x1, y1);
                }
                using (Brush b = new SolidBrush(Marker))
                    g.DrawString("FEV1 " + Fmt.N(_result.Fev1, 2), Font, b, x1 + 3, y1 - 14);
            }
        }

        // ------------------------------------------------------------------
        private void DrawComparisons(Graphics g,Rectangle plot,double xmin,double xmax,double ymin,double ymax,bool time)
        {
            for(int k=0;k<_comparisons.Count;k++)
            {
                Expiration s=_comparisons[k]; PointF[] points=new PointF[s.Count];
                for(int i=0;i<s.Count;i++) points[i]=new PointF(MapX(time?s.Time[i]:s.Volume[i],plot,xmin,xmax),MapY(time?s.Volume[i]:s.Flow[i],plot,ymin,ymax));
                using(Pen p=new Pen(Color.FromArgb(127,145,154),1.4f)) { p.DashStyle=k%2==0?DashStyle.Dash:DashStyle.DashDot; g.DrawLines(p,points); }
            }
        }
        private Rectangle PlotArea(Rectangle r, int left, int right, int top, int bottom)
        {
            float scale=Font.Size/9f; left=(int)Math.Ceiling(left*scale); right=(int)Math.Ceiling(right*scale); top=(int)Math.Ceiling(top*scale); bottom=(int)Math.Ceiling(bottom*scale);
            int w = r.Width - left - right;
            int h = r.Height - top - bottom;
            if (w < 20) w = 20;
            if (h < 20) h = 20;
            return new Rectangle(r.Left + left, r.Top + top, w, h);
        }

        private static float MapX(double v, Rectangle plot, double min, double max)
        {
            if (max - min < 1e-9) return plot.Left;
            return (float)(plot.Left + (v - min) / (max - min) * plot.Width);
        }

        private static float MapY(double v, Rectangle plot, double min, double max)
        {
            if (max - min < 1e-9) return plot.Bottom;
            return (float)(plot.Bottom - (v - min) / (max - min) * plot.Height);
        }

        private void DrawFrame(Graphics g, Rectangle plot, string xTitle, string yTitle,
                               double xMin, double xMax, double yMin, double yMax,
                               int xTicks, int yTicks)
        {
            using (Pen grid = new Pen(Grid, 1f))
            using (Pen axis = new Pen(Ink, 1.4f))
            using (Brush ink = new SolidBrush(Ink))
            using (Font small = new Font(Font.FontFamily,Font.Size*.8f))
            {
                double xStep = NiceStep((xMax - xMin) / Math.Max(xTicks, 1));
                double yStep = NiceStep((yMax - yMin) / Math.Max(yTicks, 1));

                for (double v = Math.Ceiling(xMin / xStep) * xStep; v <= xMax + 1e-9; v += xStep)
                {
                    float x = MapX(v, plot, xMin, xMax);
                    g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
                    string s = v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    SizeF sz = g.MeasureString(s, small);
                    g.DrawString(s, small, ink, x - sz.Width / 2, plot.Bottom + 2);
                }

                for (double v = Math.Ceiling(yMin / yStep) * yStep; v <= yMax + 1e-9; v += yStep)
                {
                    float y = MapY(v, plot, yMin, yMax);
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    string s = v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    SizeF sz = g.MeasureString(s, small);
                    g.DrawString(s, small, ink, plot.Left - sz.Width - 4, y - sz.Height / 2);
                }

                g.DrawRectangle(axis, plot);
                using(StringFormat align=new StringFormat { Alignment=StringAlignment.Far }) g.DrawString(xTitle,small,ink,new RectangleF(plot.Left,plot.Bottom+small.Height+3,plot.Width,small.Height+3),align);
                g.DrawString(yTitle, small, ink, plot.Left + 2, plot.Top-small.Height-2);
            }
        }

        private static double NiceStep(double raw)
        {
            if (raw <= 0 || double.IsNaN(raw)) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double n = raw / mag;
            double step;
            if (n <= 1) step = 1;
            else if (n <= 2) step = 2;
            else if (n <= 2.5) step = 2.5;
            else if (n <= 5) step = 5;
            else step = 10;
            return step * mag;
        }
    }
}
