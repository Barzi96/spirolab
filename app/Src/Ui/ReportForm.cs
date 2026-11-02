using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SpiroLab.Ui
{
    public sealed class ReportForm : Form
    {
        public event Action<ResultPackage> Exported;
        private readonly ExaminationSnapshot _snapshot;
        private readonly Patient _patient;
        private readonly IList<Trial> _trials;
        private readonly Bitmap _report;
        private readonly PrintDocument _document;
        private readonly PictureBox _preview;
        public ReportForm(Patient patient,Trial trial,int number,string summary) : this(patient,new List<Trial> { trial }) { }
        public ReportForm(Patient patient,IList<Trial> trials) : this(ExaminationSnapshot.Capture(patient,trials)) { }
        public ReportForm(ExaminationSnapshot snapshot)
        {
            int workflow=snapshot.Data.SelectionWorkflowVersion;
            snapshot.Data.ValidateSelection(workflow>0 && workflow<3);
            snapshot.Data.ValidateReportSelection(true);
            _snapshot=snapshot; _patient=snapshot.Data.Patient; _trials=snapshot.Data.Trials; _report=SpiroReport.Render(snapshot);
            Text="پیش‌نمایش گزارش اسپیرومتری — "+_patient.FullName; Size=new Size(1000,900); StartPosition=FormStartPosition.CenterParent;
            Font=new Font("Tahoma",10); RightToLeft=RightToLeft.Yes;
            _document=new PrintDocument { DocumentName="SpiroLab report" };
            _document.DefaultPageSettings.PaperSize=new PaperSize("A4",827,1169);
            _document.DefaultPageSettings.Margins=new Margins(25,25,25,25);
            _document.PrintPage+=PrintPage;
            _preview=new PictureBox { Dock=DockStyle.Fill,Image=_report,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(237,241,245) };
            FlowLayoutPanel toolbar=new FlowLayoutPanel { Dock=DockStyle.Top,Height=49,FlowDirection=FlowDirection.RightToLeft };
            AddButton(toolbar,"ذخیره JPG + JSON",delegate { SaveImage(false); });
            AddButton(toolbar,"چاپ / PDF",delegate
            {
                using(PrintDialog dialog=new PrintDialog { Document=_document,UseEXDialog=true })
                    if(dialog.ShowDialog(this)==DialogResult.OK)
                        try { _document.Print(); } catch(Exception ex) { Error(ex); }
            });
            Controls.Add(_preview); Controls.Add(toolbar);
        }
        private void AddButton(FlowLayoutPanel panel,string text,EventHandler action)
        { Button b=new Button { Text=text,AutoSize=true,Height=36,Margin=new Padding(4) }; b.Click+=action; panel.Controls.Add(b); }
        private void Error(Exception ex) { MessageBox.Show(this,ex.Message,"گزارش",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
        private void SaveImage(bool curvesOnly)
        {
            using(FolderBrowserDialog dialog=new FolderBrowserDialog { Description="پوشهٔ نگهداری معاینه را انتخاب کنید" })
                if(dialog.ShowDialog(this)==DialogResult.OK)
                    try
                    {
                        ResultPackage package=ResultExport.SaveToDirectory(dialog.SelectedPath,_snapshot);
                        if(Exported!=null) Exported(package);
                        MessageBox.Show(this,"دو فایل ذخیره شد:\n"+Path.GetDirectoryName(package.ReportPath),"خروجی");
                    }
                    catch(Exception ex) { Error(ex); }
        }
        public static Bitmap RenderCurves(Trial t) { return SpiroReport.RenderCurves(new List<Trial> { t }); }
        public static string HtmlReport(Patient p,Trial t,int number,string summary,Bitmap curves)
        { using(Bitmap report=SpiroReport.Render(p,new List<Trial> { t })) return SpiroReport.Html(p,new List<Trial> { t },report); }
        private void PrintPage(object sender,PrintPageEventArgs args)
        {
            Rectangle b=args.MarginBounds; float scale=Math.Min((float)b.Width/_report.Width,(float)b.Height/_report.Height);
            args.Graphics.DrawImage(_report,new RectangleF(b.Left+(b.Width-_report.Width*scale)/2,b.Top,_report.Width*scale,_report.Height*scale));
            args.HasMorePages=false;
        }
        protected override void Dispose(bool disposing)
        { if(disposing) { _preview.Image=null; _document.Dispose(); _report.Dispose(); } base.Dispose(disposing); }
    }
}
