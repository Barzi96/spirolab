using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;

namespace SpiroLab.Ui
{
    // Shared by PNG, print preview and HTML: one report, one set of numbers.
    // Layout mirrors the clinic's printed MIR/Spirolab sheet: a small three-part
    // header, the compact patient block, flow-volume and volume-time curves,
    // BEST VALUES, the per-maneuver table and a short interpretation/footer.
    public static class SpiroReport
    {
        public const string ProductVersion="5.4.0";
        public const int Width=1200, Height=1697;
        private static readonly Color[] Colors={Color.Black,Color.Black,Color.Black};
        private const string StampFileName="stamp.png";
        public static string OriginText(Origin origin)
        { return origin==Origin.Caucasian?"Caucasian":origin==Origin.Unspecified?"No group selected":"Other (no correction)"; }
        public static Bitmap Render(Patient p,IList<Trial> trials) { return Render(ExaminationSnapshot.Capture(p,trials)); }
        public static Bitmap Render(ExaminationSnapshot snapshot)
        {
            Patient p=snapshot.Data.Patient; ExamSummary s=snapshot.Summary;
            if(s.Best==null) throw new InvalidOperationException("برای گزارش، حداقل یک مانور کامل و معتبر ثبت کنید.");
            Bitmap image=new Bitmap(Width*2,Height*2); image.SetResolution(300,300);
            using(Graphics g=Graphics.FromImage(image))
            {
                g.Clear(Color.White); g.ScaleTransform(2,2); g.SmoothingMode=SmoothingMode.AntiAlias;
                g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                int left=55, right=1145, content=right-left;
                string session=string.IsNullOrWhiteSpace(snapshot.Data.SessionType)?"PRE":snapshot.Data.SessionType;
                Text(g,string.IsNullOrWhiteSpace(snapshot.Data.CenterName)?"SPIROMETRY REPORT":snapshot.Data.CenterName,new RectangleF(left,28,content,34),25,false,true,true);
                Text(g,"ERS / ECCS 1993",new RectangleF(left,72,260,22),15,false,false,false);
                Text(g,session+" | "+(snapshot.Data.SelectionWorkflowVersion>=3?"BEST VALUES":"Site: "+(snapshot.OperatorSelectedIndex>0?"#"+snapshot.OperatorSelectedIndex:"—")),new RectangleF(left+content/2-160,72,320,22),15,false,false,true);
                Text(g,"کارشناس: "+(string.IsNullOrWhiteSpace(snapshot.Data.OperatorName)?"—":snapshot.Data.OperatorName),new RectangleF(right-320,72,320,22),15,false,true,false);
                g.DrawLine(new Pen(Color.FromArgb(120,120,120),.7f),left,100,right,100);

                // Patient block: the same order and labels as the printed sheet.
                Text(g,"Test Date   "+ExaminationDates.Format(snapshot.TestedAt)+"        BTPS: not independently verified",new RectangleF(left,112,content,22),15,false,false,false);
                Text(g,"Name   "+p.FullName,new RectangleF(left,138,content,24),17,false,false,false);
                Text(g,"Birth Date   "+(p.BirthDate.HasValue?ExaminationDates.Format(p.BirthDate.Value):"—")+"          #ID   "+p.NationalId,new RectangleF(left,164,content,22),15,false,false,false);
                Text(g,"Age   "+p.Age+"        Height cm   "+Fmt.N(p.HeightCm,1)+"        Weight kg   "+Fmt.N(p.WeightKg,1)+"        Gender   "+(p.Sex==Sex.Male?"Male":"Female"),new RectangleF(left,190,content,22),15,false,false,false);
                Text(g,"Origin   "+OriginText(p.Origin)+"        Predicted ERS",new RectangleF(left,216,content,22),15,false,false,false);
                Text(g,session+" File N°   "+(string.IsNullOrWhiteSpace(p.EmployeeCode)?"—":p.EmployeeCode),new RectangleF(left,242,content,22),15,false,false,false);

                DrawCurves(g,new Rectangle(left,284,content,630),s,delegate(Trial t) { return snapshot.Index(t); },session);

                Text(g,"BEST VALUES",new RectangleF(left,930,content,24),17,true,false,false);
                string[][] best=BestRows(s);
                Table(g,left,960,600,22,new string[] {"","Pred.","Measured","%Pred."},best,new int[] {34,22,22,22},14);

                List<string[]> detail=DetailRows(s,snapshot);
                string[] headers=new string[] {"PARAMETER","Pred.",s.Trials.Count>0?session+" #"+snapshot.Index(s.Trials[0]):session+" #1","%Pred.",s.Trials.Count>1?session+" #"+snapshot.Index(s.Trials[1]):session+" #2",s.Trials.Count>2?session+" #"+snapshot.Index(s.Trials[2]):session+" #3"};
                Table(g,left,1110,content,22,headers,detail.ToArray(),new int[] {30,13,13,12,14,14},14);

                Text(g,"INTERPRETATION:",new RectangleF(left,1432,content,22),15,false,false,false);
                string interpretation=Interpretation(snapshot);
                string interpretationLabel=string.IsNullOrWhiteSpace(snapshot.Data.ReviewedInterpretation)?"PRELIMINARY AUTOMATED: ":"CLINICIAN-REVIEWED: ";
                string interpretationText=interpretationLabel+interpretation;
                Text(g,interpretationText,new RectangleF(left,1456,content,22),15,false,Persian(interpretation),false);
                Text(g,"QUALITY CONTROL GRADE: "+(string.IsNullOrWhiteSpace(snapshot.Data.ReviewedQualityGrade)?"—":snapshot.Data.ReviewedQualityGrade),new RectangleF(left,1482,content,22),15,false,false,false);
                Text(g,"EXHALE ALL air in the lungs",new RectangleF(left,1504,content,22),15,false,false,false);
                Text(g,"REPEATABILITY: FVC, FEV1",new RectangleF(left,1526,content,22),15,false,false,false);

                DrawStamp(g,right-230,1638);
                Text(g,"SpiroLab "+ProductVersion,new RectangleF(left,1652,420,20),12,false,false,false);
                string serial=s.BestTrial==null?null:s.BestTrial.DeviceSerial;
                Text(g,"SN "+(string.IsNullOrWhiteSpace(serial)?"—":serial.Trim()),new RectangleF(right-260,1652,260,20),12,false,false,true);
            }
            return image;
        }
        // Optional clinic stamp: %APPDATA%\SpiroLab\stamp.png. When absent the
        // area stays blank without moving any other block.
        // Short print-style wording on the sheet; a clinician's reviewed text is
        // printed exactly as entered.
        internal static string Interpretation(ExaminationSnapshot snapshot)
        {
            if(!string.IsNullOrWhiteSpace(snapshot.Data.ReviewedInterpretation)) return snapshot.Data.ReviewedInterpretation;
            ExamSummary s=snapshot.Summary;
            if(s.Best==null || !s.Best.Ref.Applicable) return "Outside ERS reference range";
            bool ratioLow=s.Ratio<s.Best.Ref.RatioLln, fvcLow=s.Fvc<s.Best.Ref.FvcLln;
            if(ratioLow && fvcLow) return "Obstructive pattern with reduced FVC";
            if(ratioLow) return "Obstructive pattern";
            if(fvcLow) return "Restrictive pattern";
            return "Normal Spirometry";
        }
        private static void DrawStamp(Graphics g,int right,int bottom)
        {
            try
            {
                string path=Path.Combine(Path.GetDirectoryName(AppSettings.SettingsPath),StampFileName);
                if(!File.Exists(path)) return;
                using(Image stamp=Image.FromFile(path))
                {
                    const int maxWidth=220, maxHeight=96;
                    double scale=Math.Min((double)maxWidth/stamp.Width,(double)maxHeight/stamp.Height);
                    int width=Math.Max(1,(int)Math.Round(stamp.Width*scale)), height=Math.Max(1,(int)Math.Round(stamp.Height*scale));
                    g.DrawImage(stamp,new Rectangle(right-width,bottom-height,width,height));
                }
            }
            catch { }
        }
        public static Bitmap RenderCurves(IList<Trial> trials)
        {
            ExamSummary s=ExamSummary.Build(trials);
            if(s.Best==null) throw new InvalidOperationException("مانور کامل و معتبر وجود ندارد.");
            Bitmap image=new Bitmap(1200,700); image.SetResolution(150,150);
            using(Graphics g=Graphics.FromImage(image))
            {
                g.Clear(Color.White); g.SmoothingMode=SmoothingMode.AntiAlias;
                Text(g,ExamStore.Source(s.BestTrial)+" | FLOW–VOLUME & VOLUME–TIME",new RectangleF(60,20,1080,30),16,false,true,true);
                DrawCurves(g,new Rectangle(60,80,1080,560),s);
            }
            return image;
        }
        private static void DrawCurves(Graphics g,Rectangle area,ExamSummary s,Func<Trial,int> identify=null,string session="PRE")
        {
            int gap=24;
            int plotHeight=area.Height-58;
            Rectangle flow=new Rectangle(area.Left,area.Top,area.Width*47/100,plotHeight);
            Rectangle time=new Rectangle(flow.Right+gap,area.Top,area.Width-flow.Width-gap,plotHeight);
            double volumeMax=Math.Max(2,Math.Ceiling(Math.Max(s.Fvc,s.Best.Ref.Fvc)+.25));
            double flowMax=Math.Max(4,Math.Ceiling(Math.Max(s.Pef,s.Best.Ref.Pef)/2+1)*2);
            double timeMax=6;
            foreach(Trial t in s.Trials) { Expiration e=Expiration.From(t); if(e.Count>0) timeMax=Math.Max(timeMax,Math.Ceiling(e.Time[e.Count-1]+1)); }
            PlotFlowVolume(g,flow,s,volumeMax,flowMax);
            PlotVolumeTime(g,time,s,volumeMax,timeMax);
            Legend(g,new Rectangle(area.Left,area.Bottom-26,area.Width,26),s,identify,session);
        }
        private static Rectangle Inner(Rectangle panel)
        { return new Rectangle(panel.Left+44,panel.Top+10,panel.Width-56,panel.Height-38); }
        private static void PlotFlowVolume(Graphics g,Rectangle panel,ExamSummary s,double volumeMax,double flowMax)
        {
            Rectangle plot=Inner(panel);
            using(Pen grid=new Pen(Color.FromArgb(205,205,205),.7f))
            {
                grid.DashStyle=DashStyle.Dot;
                for(int v=0;v<=volumeMax;v++)
                {
                    float x=plot.Left+(float)(v/volumeMax*plot.Width);
                    g.DrawLine(grid,x,plot.Top,x,plot.Bottom);
                    Text(g,v.ToString(CultureInfo.InvariantCulture),new RectangleF(x-14,plot.Bottom+3,28,18),12,false,false,true);
                }
                for(int f=0;f<=flowMax;f+=2)
                {
                    float y=plot.Bottom-(float)(f/flowMax*plot.Height);
                    g.DrawLine(grid,plot.Left,y,plot.Right,y);
                    Text(g,f.ToString(CultureInfo.InvariantCulture),new RectangleF(plot.Left-38,y-9,32,18),12,false,false,true);
                }
            }
            g.DrawRectangle(new Pen(Color.FromArgb(90,90,90),.8f),plot);
            Text(g,"FLOW (L/s)",new RectangleF(panel.Left,panel.Top-14,220,20),13,false,true,false);
            Text(g,"VOLUME (L)",new RectangleF(plot.Left,plot.Bottom+20,plot.Width,20),13,false,false,true);
            GraphicsState clip=g.Save(); g.SetClip(plot);
            DrawPredictedFlow(g,plot,s,volumeMax,flowMax);
            for(int k=0;k<s.Trials.Count;k++)
            {
                Expiration e=Expiration.From(s.Trials[k]); if(e.Count<2) continue;
                PointF[] points=new PointF[e.Count];
                for(int i=0;i<e.Count;i++) points[i]=new PointF(plot.Left+(float)(e.Volume[i]/volumeMax*plot.Width),plot.Bottom-(float)(e.Flow[i]/flowMax*plot.Height));
                using(Pen pen=SeriesPen(k)) g.DrawLines(pen,points);
            }
            g.Restore(clip);
        }
        private static void PlotVolumeTime(Graphics g,Rectangle panel,ExamSummary s,double volumeMax,double timeMax)
        {
            Rectangle plot=Inner(panel);
            using(Pen grid=new Pen(Color.FromArgb(205,205,205),.7f))
            {
                grid.DashStyle=DashStyle.Dot;
                for(int t=0;t<=timeMax;t++)
                {
                    float x=plot.Left+(float)(t/timeMax*plot.Width);
                    g.DrawLine(grid,x,plot.Top,x,plot.Bottom);
                    Text(g,t.ToString(CultureInfo.InvariantCulture),new RectangleF(x-14,plot.Bottom+3,28,18),12,false,false,true);
                }
                for(int v=0;v<=volumeMax;v++)
                {
                    float y=plot.Bottom-(float)(v/volumeMax*plot.Height);
                    g.DrawLine(grid,plot.Left,y,plot.Right,y);
                    Text(g,v.ToString(CultureInfo.InvariantCulture),new RectangleF(plot.Left-38,y-9,32,18),12,false,false,true);
                }
            }
            g.DrawRectangle(new Pen(Color.FromArgb(90,90,90),.8f),plot);
            Text(g,"VOLUME (L)",new RectangleF(panel.Left,panel.Top-14,220,20),13,false,true,false);
            Text(g,"TIME (s)",new RectangleF(plot.Left,plot.Bottom+20,plot.Width,20),13,false,false,true);
            GraphicsState clip=g.Save(); g.SetClip(plot);
            DrawPredictedVolume(g,plot,s,volumeMax,timeMax);
            for(int k=0;k<s.Trials.Count;k++)
            {
                Expiration e=Expiration.From(s.Trials[k]); if(e.Count<2) continue;
                PointF[] points=new PointF[e.Count];
                for(int i=0;i<e.Count;i++) points[i]=new PointF(plot.Left+(float)(e.Time[i]/timeMax*plot.Width),plot.Bottom-(float)(e.Volume[i]/volumeMax*plot.Height));
                using(Pen pen=SeriesPen(k)) g.DrawLines(pen,points);
            }
            g.Restore(clip);
        }
        // Schematic predicted envelope derived from the ERS predicted FVC, FEV1
        // and PEF; it is a reference shape, not a recorded measurement.
        private static void DrawPredictedFlow(Graphics g,Rectangle plot,ExamSummary s,double volumeMax,double flowMax)
        {
            double fvc=s.Best.Ref.Fvc, pef=s.Best.Ref.Pef, fev1=s.Best.Ref.Fev1;
            if(fvc<=0 || pef<=0) return;
            List<PointF> points=new List<PointF>();
            for(int i=0;i<=40;i++)
            {
                double fraction=i/40.0;
                double shape=fraction<0.18?fraction/0.18:Math.Pow(1-(fraction-0.18)/0.82,1.35);
                double volume=fraction*fvc;
                double flow=pef*Math.Max(0,Math.Min(1,shape));
                points.Add(new PointF(plot.Left+(float)(volume/volumeMax*plot.Width),plot.Bottom-(float)(flow/flowMax*plot.Height)));
            }
            using(Pen pen=new Pen(Color.FromArgb(110,110,110),1.2f)) { pen.DashStyle=DashStyle.Dash; g.DrawLines(pen,points.ToArray()); }
        }
        private static void DrawPredictedVolume(Graphics g,Rectangle plot,ExamSummary s,double volumeMax,double timeMax)
        {
            double fvc=s.Best.Ref.Fvc, fev1=s.Best.Ref.Fev1;
            if(fvc<=0 || fev1<=0 || fev1>=fvc) return;
            double tau=-1/Math.Log(1-fev1/fvc);
            if(double.IsNaN(tau) || double.IsInfinity(tau) || tau<=0) return;
            List<PointF> points=new List<PointF>();
            for(int i=0;i<=60;i++)
            {
                double t=i/60.0*timeMax;
                double volume=fvc*(1-Math.Exp(-t/tau));
                points.Add(new PointF(plot.Left+(float)(t/timeMax*plot.Width),plot.Bottom-(float)(Math.Min(volume,volumeMax)/volumeMax*plot.Height)));
            }
            using(Pen pen=new Pen(Color.FromArgb(110,110,110),1.2f)) { pen.DashStyle=DashStyle.Dash; g.DrawLines(pen,points.ToArray()); }
        }
        private static Pen SeriesPen(int index)
        {
            Pen pen=new Pen(Colors[Math.Min(index,Colors.Length-1)],index==0?1.8f:1.3f);
            if(index==1) pen.DashStyle=DashStyle.Dash;
            if(index==2) pen.DashStyle=DashStyle.Dot;
            return pen;
        }
        private static void Legend(Graphics g,Rectangle area,ExamSummary s,Func<Trial,int> identify,string session)
        {
            int x=area.Left+10, y=area.Top+4;
            for(int k=0;k<s.Trials.Count;k++)
            {
                using(Pen pen=SeriesPen(k)) g.DrawLine(pen,x,y+8,x+34,y+8);
                Text(g,session+" #"+(identify==null?k+1:identify(s.Trials[k])),new RectangleF(x+40,y,90,20),13,false,false,false);
                x+=150;
            }
            using(Pen pen=new Pen(Color.FromArgb(110,110,110),1.2f)) { pen.DashStyle=DashStyle.Dash; g.DrawLine(pen,x,y+8,x+34,y+8); }
            Text(g,"Pred.",new RectangleF(x+40,y,90,20),13,false,false,false);
        }
        public static double Value(AnalysisResult r,int i)
        { switch(i) { case 0:return r.Fvc; case 1:return r.Fev1; case 2:return r.Fev1Fvc; case 3:return r.Pef; case 4:return r.Fef25; case 5:return r.Fef50; case 6:return r.Fef75; case 7:return r.Fef2575; default:return r.Bev*1000; } }
        public static double Predicted(Reference r,int i)
        { switch(i) { case 0:return r.Fvc; case 1:return r.Fev1; case 2:return r.Fev1Fvc; case 3:return r.Pef; case 4:return r.Fef25; case 5:return r.Fef50; case 6:return r.Fef75; case 7:return r.Fef2575; default:return 0; } }
        public static string MetricName(int i) { return new string[] {"FVC","FEV1","FEV1/FVC","PEF","FEF25","FEF50","FEF75","FEF25–75","BEV (estimate)"}[i]; }
        public static string Unit(int i) { return i<2?"L":i==2?"%":i==8?"mL":"L/s"; }
        private static string Label(int i) { return MetricName(i)+" ("+Unit(i)+")"; }
        private static string Label(string metric) { return Metric.DisplayName(metric)+" ("+Metric.Unit(metric)+")"; }
        private static string Number(double value,int i) { return Fmt.N(value,i==2?1:2); }
        private static string Percent(double measured,double predicted) { return predicted>0?Fmt.N(measured/predicted*100,0):"—"; }
        private static string Number(MeasuredValue value)
        {
            return value!=null && value.Available?Fmt.N(value.Value.Value,value.Name==Metric.Fev1Fvc?1:2):"—";
        }
        private static string Predicted(MeasuredValue value)
        {
            return value!=null && value.Predicted.HasValue && value.Predicted.Value>0?Fmt.N(value.Predicted.Value,value.Name==Metric.Fev1Fvc?1:2):"—";
        }
        private static string Percent(MeasuredValue value)
        {
            return value!=null && value.PercentPredicted.HasValue && value.PercentPredicted.Value>0?Fmt.N(value.PercentPredicted.Value,0):"—";
        }
        private static MeasuredValue ValueOf(Trial trial,int i)
        {
            if(trial==null) return null;
            return trial.Measurements.Get(MetricNameForIndex(i));
        }
        private static string MetricNameForIndex(int i)
        {
            switch(i)
            {
                case 0:return Metric.Fvc;case 1:return Metric.Fev1;case 2:return Metric.Fev1Fvc;case 3:return Metric.Pef;
                case 4:return Metric.Fef25;case 5:return Metric.Fef50;case 6:return Metric.Fef75;case 7:return Metric.Fef2575;default:return Metric.Bev;
            }
        }
        private static string[][] BestRows(ExamSummary s)
        {
            if(s==null || s.BestValues==null) return new string[0][];
            MeasuredValue[] values={s.BestValues.Fvc,s.BestValues.Fev1,s.BestValues.Fev1Fvc,s.BestValues.Pef,s.BestValues.Fef2575};
            string[][] rows=new string[values.Length][];
            for(int k=0;k<values.Length;k++)
                rows[k]=new string[] {Label(values[k].Name),Predicted(values[k]),Number(values[k]),Percent(values[k])};
            return rows;
        }
        private static List<string[]> DetailRows(ExamSummary s,ExaminationSnapshot snapshot)
        {
            List<string[]> rows=new List<string[]>();
            // Session maxima are shown once in BEST VALUES. The detailed table is
            // exclusively actual measurements from the displayed real maneuvers.
            for(int i=0;i<8;i++)
            {
                string metric=MetricNameForIndex(i);
                MeasuredValue reference=s.BestValues==null?null:s.BestValues.Get(metric);
                List<string> row=new List<string> {Label(metric),reference!=null && reference.Predicted.HasValue && reference.Predicted.Value>0?Fmt.N(reference.Predicted.Value,metric==Metric.Fev1Fvc?1:2):"—"};
                for(int j=0;j<3;j++)
                {
                    MeasuredValue value=j<s.Trials.Count?ValueOf(s.Trials[j],i):null;
                    row.Add(Number(value));
                    if(j==0) row.Add(Percent(value));
                }
                rows.Add(row.ToArray());
            }
            List<string> evol=new List<string> {"EVol (mL)","—"};
            for(int j=0;j<3;j++)
            {
                evol.Add(j<s.Trials.Count && s.Trials[j].Result.BevAvailable?Fmt.N(s.Trials[j].Result.Bev*1000,0):"—");
                if(j==0) evol.Add("—");
            }
            rows.Add(evol.ToArray());
            return rows;
        }
        private static void Table(Graphics g,int x,int y,int width,int rowHeight,string[] headers,string[][] rows,int[] weights,float size)
        {
            int total=0; foreach(int weight in weights) total+=weight;
            float[] widths=new float[weights.Length]; for(int i=0;i<weights.Length;i++) widths[i]=(float)width*weights[i]/total;
            for(int row=-1;row<rows.Length;row++)
            {
                float cellX=x;
                for(int col=0;col<widths.Length;col++)
                {
                    string value=row<0?(col<headers.Length?headers[col]:""):rows[row][col];
                    Text(g,value,new RectangleF(cellX,y+(row+1)*rowHeight,widths[col],rowHeight),size,row<0,false,col>0);
                    cellX+=widths[col];
                }
                float bottom=y+(row+2)*rowHeight;
                g.DrawLine(new Pen(row<0?Color.FromArgb(110,110,110):Color.FromArgb(205,205,205),row<0?.9f:.6f),x,bottom,x+width,bottom);
            }
        }
        private static bool Persian(string text)
        {
            if(string.IsNullOrEmpty(text)) return false;
            foreach(char c in text) if(c>=0x0600 && c<=0x06FF) return true;
            return false;
        }
        private static void Text(Graphics g,string text,RectangleF rect,float size,bool bold,bool rtl,bool center)
        {
            string family=Persian(text)?"Tahoma":"Consolas";
            using(Font font=new Font(family,size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
            using(StringFormat format=new StringFormat { Alignment=center?StringAlignment.Center:StringAlignment.Near,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap })
            { if(rtl) format.FormatFlags|=StringFormatFlags.DirectionRightToLeft; g.DrawString(text??"",font,Brushes.Black,rect,format); }
        }
        private static string E(string s) { return SecurityElement.Escape(s??"")??""; }
        public static string Html(Patient p,IList<Trial> trials,Bitmap report)
        {
            ExamSummary s=ExamSummary.Build(trials); StringBuilder b=new StringBuilder();
            b.Append("<!doctype html><html lang='fa' dir='rtl'><meta charset='utf-8'><title>SpiroLab</title><style>body{font:13px Tahoma;max-width:1000px;margin:auto}img{width:100%}table{width:100%;border-collapse:collapse;direction:ltr}td,th{border-bottom:1px solid #ccc;padding:6px;text-align:center}li{margin:10px}h2{font-size:16px}@media print{.details{break-before:page}}</style>");
            using(MemoryStream ms=new MemoryStream()) { report.Save(ms,ImageFormat.Png); b.Append("<img alt='"+E(p.FullName)+" — "+E(ExamStore.Source(s.BestTrial))+"' src='data:image/png;base64,"+Convert.ToBase64String(ms.ToArray())+"'>"); }
            b.Append("<div class='details'><h2>BEST VALUES</h2><table><tr><th>Parameter</th><th>Pred.</th><th>Measured</th><th>%Pred.</th></tr>");
            foreach(string[] row in BestRows(s)) { b.Append("<tr>"); foreach(string value in row) b.Append("<td>"+E(value)+"</td>"); b.Append("</tr>"); }
            b.Append("</table><p>"+E(p.FullName)+" | "+E(p.NationalId)+"</p></div></html>");
            return b.ToString();
        }
    }
}
