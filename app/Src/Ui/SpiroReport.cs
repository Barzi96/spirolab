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
    public static class SpiroReport
    {
        public const int Width=1200, Height=1697;
        private static readonly Color[] Colors={Color.Black,Color.Black,Color.Black};
        public static string OriginText(Origin origin)
        { return origin==Origin.Caucasian?"Caucasian":origin==Origin.Unspecified?"No group selected":"Other (no correction)"; }
        public static Bitmap Render(Patient p,IList<Trial> trials) { return Render(ExaminationSnapshot.Capture(p,trials)); }
        public static Bitmap Render(ExaminationSnapshot snapshot)
        {
            Patient p=snapshot.Data.Patient; IList<Trial> trials=snapshot.Data.Trials;
            ExamSummary s=snapshot.Summary;
            if(s.Best==null) throw new InvalidOperationException("برای گزارش، حداقل یک مانور کامل و معتبر ثبت کنید.");
            Bitmap image=new Bitmap(Width*2,Height*2); image.SetResolution(300,300);
            using(Graphics g=Graphics.FromImage(image))
            {
                g.Clear(Color.White); g.ScaleTransform(2,2); g.SmoothingMode=SmoothingMode.AntiAlias;
                g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                Text(g,string.IsNullOrWhiteSpace(snapshot.Data.CenterName)?"SPIROMETRY REPORT":snapshot.Data.CenterName,new RectangleF(60,42,1080,50),27,true,false,true);
                Text(g,"ERS / ECCS 1993",new RectangleF(60,95,350,30),14,false,false,false);
                Text(g,"PRE | Primary: "+(snapshot.OperatorSelectedIndex>0?"#"+snapshot.OperatorSelectedIndex:"—"),new RectangleF(450,95,300,30),14,false,false,true);
                Text(g,"کارشناس: "+(string.IsNullOrWhiteSpace(snapshot.Data.OperatorName)?"—":snapshot.Data.OperatorName),new RectangleF(790,95,350,30),14,false,true,false);
                Text(g,snapshot.Status+" | PRE | "+snapshot.ManeuverCount+" maneuvers",new RectangleF(60,137,1080,32),13,true,false,true);
                g.DrawLine(Pens.LightGray,60,177,1140,177);
                Text(g,p.FullName+"    |    کد ملی: "+p.NationalId,new RectangleF(60,192,1080,33),17,true,true,false);
                Text(g,"سن: "+p.Age+"    جنسیت: "+(p.Sex==Sex.Male?"مرد":"زن")+"    قد: "+Fmt.N(p.HeightCm,1)+" cm    وزن: "+Fmt.N(p.WeightKg,1)+" kg",new RectangleF(60,232,1080,33),14,false,true,false);
                Text(g,"Exam date: "+ExaminationDates.Format(snapshot.TestedAt)+" ("+PersianDate.Format(snapshot.TestedAt)+")    |    Origin: "+OriginText(p.Origin)+"    |    Predicted: ERS",new RectangleF(60,276,1080,29),13,false,false,false);
                Text(g,"FLOW–VOLUME & VOLUME–TIME CURVES",new RectangleF(60,321,1080,30),16,true,false,true);
                DrawCurves(g,new Rectangle(60,362,1080,483),s,delegate(Trial t) { return snapshot.Index(t); },snapshot.OperatorSelectedManeuverId);
                Text(g,"BEST VALUES",new RectangleF(60,880,1080,32),18,true,false,true);
                string[][] best=BestRows(s);
                Table(g,60,920,1080,32,new string[] {"PARAMETER","UNIT","Pred.","Measured","%Pred."},best);
                Text(g,"MANEUVER DETAILS — UP TO THREE, PRIMARY INCLUDED",new RectangleF(60,1133,1080,30),14,true,false,true);
                List<string[]> detail=new List<string[]>();
                int[] starred={0,1,3}; double[] maxima={s.Fvc,s.Fev1,s.Pef};
                for(int k=0;k<starred.Length;k++)
                {
                    int i=starred[k]; List<string> row=new List<string> {"*"+MetricName(i),Unit(i),Predicted(s.Best.Ref,i)>0?Fmt.N(Predicted(s.Best.Ref,i),2):"—"};
                    for(int j=0;j<3;j++) { row.Add(j<s.Trials.Count?Fmt.N(maxima[k],2):"—"); if(j==0) row.Add(Predicted(s.Best.Ref,i)>0?Fmt.N(maxima[k]/Predicted(s.Best.Ref,i)*100,0):"—"); }
                    detail.Add(row.ToArray());
                }
                for(int i=0;i<8;i++)
                {
                    List<string> row=new List<string> {MetricName(i),Unit(i),Predicted(s.Best.Ref,i)>0?Fmt.N(Predicted(s.Best.Ref,i),i==2?1:2):"—"};
                    for(int j=0;j<3;j++) { row.Add(j<s.Trials.Count?Fmt.N(Value(s.Trials[j].Result,i),i==2?1:2):"—"); if(j==0) row.Add(s.Trials.Count>0 && Predicted(s.Best.Ref,i)>0?Fmt.N(Value(s.Trials[0].Result,i)/Predicted(s.Best.Ref,i)*100,0):"—"); }
                    detail.Add(row.ToArray());
                }
                List<string> evol=new List<string> {"EVol (calc.)","mL","—"};
                for(int j=0;j<3;j++) { evol.Add(j<s.Trials.Count && s.Trials[j].Result.BevAvailable?Fmt.N(s.Trials[j].Result.Bev*1000,0):"—"); if(j==0) evol.Add("—"); }
                detail.Add(evol.ToArray());
                Table(g,60,1170,1080,22,new string[] {"PARAMETER","UNIT","Pred.",s.Trials.Count>0?"PRE #"+snapshot.Index(s.Trials[0]):"—","%Pred.",s.Trials.Count>1?"PRE #"+snapshot.Index(s.Trials[1]):"—",s.Trials.Count>2?"PRE #"+snapshot.Index(s.Trials[2]):"—"},detail.ToArray());
                Text(g,"INTERPRETATION: "+snapshot.InterpretationText,new RectangleF(60,1462,1080,40),12,false,true,false);
                Text(g,"QUALITY: "+(string.IsNullOrWhiteSpace(snapshot.Data.ReviewedQualityGrade)?"UNVALIDATED":snapshot.Data.ReviewedQualityGrade+" (manual)")+" | EVol: timing unverified | VEXT: "+(snapshot.Data.ReviewedVextMl.HasValue?Fmt.N(snapshot.Data.ReviewedVextMl.Value,2)+" mL (manual review)":"unavailable"),new RectangleF(60,1505,1080,25),11,false,false,false);
                Text(g,"مانور اصلی: "+snapshot.PrimaryLabel+" | انتخاب‌کننده: "+(snapshot.OperatorSelectedIndex>0?snapshot.Data.SelectionOperatorName:"—")+" | روش: "+(snapshot.OperatorSelectedIndex>0?(snapshot.Data.SelectionMethod=="manual"?"دستی":"پیشنهاد پذیرفته‌شده"):"ثبت نشده"),new RectangleF(60,1532,1080,25),11,false,true,false);
                Text(g,snapshot.Data.ReviewedQualityComment,new RectangleF(60,1558,1080,23),11,false,true,false);
                Text(g,"REPEATABILITY (descriptive, unvalidated): "+s.Repeatability,new RectangleF(60,1582,1080,28),11,false,false,false);
                Text(g,"Exam: "+snapshot.Data.ExaminationId+" | Recommendation: PRE #"+snapshot.BestIndex+" | Primary: "+snapshot.PrimaryLabel,new RectangleF(60,1655,1080,26),10,false,false,false);
                Text(g,"Independent SpiroLab 5.1.1 | SN: "+(string.IsNullOrEmpty(s.BestTrial.DeviceSerial)?"not verified":s.BestTrial.DeviceSerial)+" | Not a MIR-generated report",new RectangleF(60,1620,1080,28),11,false,false,false);
            }
            return image;
        }
        public static Bitmap RenderCurves(IList<Trial> trials)
        {
            ExamSummary s=ExamSummary.Build(trials);
            if(s.Best==null) throw new InvalidOperationException("مانور کامل و معتبر وجود ندارد.");
            Bitmap image=new Bitmap(1200,700); image.SetResolution(150,150);
            using(Graphics g=Graphics.FromImage(image))
            {
                g.Clear(Color.White); g.SmoothingMode=SmoothingMode.AntiAlias;
                Text(g,ExamStore.Source(s.BestTrial)+" | FLOW–VOLUME & VOLUME–TIME",new RectangleF(60,20,1080,35),15,true,true,true);
                DrawCurves(g,new Rectangle(60,80,1080,560),s);
            }
            return image;
        }
        private static void DrawCurves(Graphics g,Rectangle area,ExamSummary s,Func<Trial,int> identify=null,string primaryId=null)
        {
            Rectangle plot=new Rectangle(area.Left+58,area.Top+35,area.Width-118,area.Height-96);
            double volumeMax=Math.Max(7,Math.Ceiling(Math.Max(s.Fvc,s.Best.Ref.Fvc)+.5));
            double flowMax=Math.Max(14,Math.Ceiling(Math.Max(s.Pef,s.Best.Ref.Pef)/2+1)*2),timeMax=14;
            List<Expiration> signals=new List<Expiration>();
            foreach(Trial t in s.Trials) { Expiration e=Expiration.From(t); signals.Add(e); if(e.Count>0) timeMax=Math.Max(timeMax,Math.Ceiling(e.Time[e.Count-1]+1)); }
            using(Pen grid=new Pen(Color.FromArgb(217,222,226),1))
            {
                grid.DashStyle=DashStyle.Dot;
                for(int v=0;v<=volumeMax;v++)
                {
                    float x=plot.Left+(float)(v/volumeMax*plot.Width); g.DrawLine(grid,x,plot.Top,x,plot.Bottom);
                    Text(g,v.ToString(CultureInfo.InvariantCulture),new RectangleF(x-17,plot.Bottom+5,34,22),10,false,false,true);
                }
                for(int f=0;f<=flowMax;f+=2)
                {
                    float y=plot.Bottom-(float)(f/flowMax*plot.Height); g.DrawLine(grid,plot.Left,y,plot.Right,y);
                    Text(g,f.ToString(CultureInfo.InvariantCulture),new RectangleF(plot.Left-40,y-11,30,23),10,false,false,true);
                }
                for(int t=0;t<=timeMax;t+=2)
                {
                    float y=plot.Top+(float)(t/timeMax*plot.Height);
                    Text(g,t.ToString(CultureInfo.InvariantCulture),new RectangleF(plot.Right+8,y-11,37,23),10,false,false,true);
                }
            }
            g.DrawRectangle(Pens.Gray,plot);
            Text(g,"FLOW (L/s)",new RectangleF(plot.Left-30,area.Top,200,27),12,true,false,false);
            Text(g,"TIME (s)",new RectangleF(plot.Right-100,area.Top,150,27),12,true,false,false);
            Text(g,"VOLUME (L)",new RectangleF(plot.Left+plot.Width/2-90,plot.Bottom+26,180,25),11,false,false,true);
            GraphicsState clip=g.Save(); g.SetClip(plot);
            for(int k=0;k<signals.Count;k++)
            {
                Expiration e=signals[k]; if(e.Count<2) continue;
                PointF[] fv=new PointF[e.Count],vt=new PointF[e.Count];
                for(int i=0;i<e.Count;i++)
                {
                    float x=plot.Left+(float)(e.Volume[i]/volumeMax*plot.Width);
                    fv[i]=new PointF(x,plot.Bottom-(float)(e.Flow[i]/flowMax*plot.Height));
                    vt[i]=new PointF(x,plot.Top+(float)(e.Time[i]/timeMax*plot.Height));
                }
                using(Pen pen=new Pen(Colors[k],k==0?2.4f:1.8f))
                { if(k==1) pen.DashStyle=DashStyle.Dash; if(k==2) pen.DashStyle=DashStyle.DashDot; g.DrawLines(pen,fv); g.DrawLines(pen,vt); }
            }
            g.Restore(clip);
            int legendY=area.Bottom+2;
            for(int k=0;k<s.Trials.Count;k++)
            {
                using(Pen pen=new Pen(Colors[k],2)) { if(k==1) pen.DashStyle=DashStyle.Dash; if(k==2) pen.DashStyle=DashStyle.DashDot; g.DrawLine(pen,area.Left+90+k*290,legendY+10,area.Left+132+k*290,legendY+10); }
                Text(g,"PRE #"+(identify==null?k+1:identify(s.Trials[k]))+(s.Trials[k].ManeuverId==primaryId?" [PRIMARY]":""),new RectangleF(area.Left+142+k*290,legendY-3,195,28),11,s.Trials[k].ManeuverId==primaryId,false,false);
            }
        }
        public static double Value(AnalysisResult r,int i)
        { switch(i) { case 0:return r.Fvc; case 1:return r.Fev1; case 2:return r.Fev1Fvc; case 3:return r.Pef; case 4:return r.Fef25; case 5:return r.Fef50; case 6:return r.Fef75; case 7:return r.Fef2575; default:return r.Bev*1000; } }
        public static double Predicted(Reference r,int i)
        { switch(i) { case 0:return r.Fvc; case 1:return r.Fev1; case 2:return r.Fev1Fvc; case 3:return r.Pef; case 4:return r.Fef25; case 5:return r.Fef50; case 6:return r.Fef75; case 7:return r.Fef2575; default:return 0; } }
        public static string MetricName(int i) { return new string[] {"FVC","FEV1","FEV1/FVC","PEF","FEF25","FEF50","FEF75","FEF25–75","BEV (estimate)"}[i]; }
        public static string Unit(int i) { return i<2?"L":i==2?"%":i==8?"mL":"L/s"; }
        private static string[][] BestRows(ExamSummary s)
        {
            int[] index={0,1,2,3,7}; double[] measured={s.Fvc,s.Fev1,s.Ratio,s.Pef,s.Best.Fef2575};
            string[][] rows=new string[5][];
            for(int k=0;k<5;k++) { int i=index[k]; double predicted=Predicted(s.Best.Ref,i); rows[k]=new string[] {MetricName(i),Unit(i),predicted>0?Fmt.N(predicted,i==2?1:2):"—",Fmt.N(measured[k],i==2?1:2),predicted>0?Fmt.N(measured[k]/predicted*100,0):"—"}; }
            return rows;
        }
        private static void Table(Graphics g,int x,int y,int width,int rowHeight,string[] headers,string[][] rows)
        {
            int columns=headers.Length; float cw=(float)width/columns;
            for(int row=0;row<=rows.Length;row++)
                for(int col=0;col<columns;col++)
                {
                    RectangleF cell=new RectangleF(x+col*cw,y+row*rowHeight,cw,rowHeight);
                    if(row==0) g.DrawLine(Pens.Gray,cell.Left,cell.Bottom,cell.Right,cell.Bottom);
                    Text(g,row==0?headers[col]:rows[row-1][col],cell,14,row==0,false,true);
                }
        }
        private static void Text(Graphics g,string text,RectangleF rect,float size,bool bold,bool rtl,bool center)
        {
            using(Font font=new Font("Tahoma",size*1.35f,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
            using(StringFormat format=new StringFormat { Alignment=center?StringAlignment.Center:StringAlignment.Near,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter })
            { if(rtl) format.FormatFlags|=StringFormatFlags.DirectionRightToLeft; g.DrawString(text,font,Brushes.Black,rect,format); }
        }
        private static string E(string s) { return SecurityElement.Escape(s??"")??""; }
        public static string Html(Patient p,IList<Trial> trials,Bitmap report)
        {
            ExamSummary s=ExamSummary.Build(trials); StringBuilder b=new StringBuilder();
            b.Append("<!doctype html><html lang='fa' dir='rtl'><meta charset='utf-8'><title>SpiroLab</title><style>body{font:14px Tahoma;max-width:1000px;margin:auto}img{width:100%}table{width:100%;border-collapse:collapse;direction:ltr}td,th{border:1px solid #ccc;padding:8px;text-align:center}li{margin:10px}h2{font-size:18px}@media print{.details{break-before:page}}</style>");
            using(MemoryStream ms=new MemoryStream()) { report.Save(ms,ImageFormat.Png); b.Append("<img alt='"+E(p.FullName)+" — "+E(ExamStore.Source(s.BestTrial))+"' src='data:image/png;base64,"+Convert.ToBase64String(ms.ToArray())+"'>"); }
            b.Append("<div class='details'><h2>اعداد بهترین مقادیر</h2><table><tr><th>Parameter</th><th>Unit</th><th>Pred.</th><th>Measured</th><th>%Pred.</th></tr>");
            foreach(string[] row in BestRows(s)) { b.Append("<tr>"); foreach(string value in row) b.Append("<td>"+E(value)+"</td>"); b.Append("</tr>"); }
            b.Append("</table><p>"+E(p.FullName)+" | "+E(p.NationalId)+"</p></div></html>");
            return b.ToString();
        }
    }
}
