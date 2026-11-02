using System;
using System.Drawing;
using System.Windows.Forms;
namespace SpiroLab.Ui
{
    public sealed class ExaminationDetailsForm : Form
    {
        readonly Exam exam;
        readonly Trial maneuver;
        readonly TextBox center,op,interpretation,vext,source,grade,comment;
        readonly CheckBox reviewed;
        readonly Label error;
        public ExaminationDetailsForm(Exam value,Trial preview=null)
        {
            exam=value; ExamSummary summary=exam.SelectionWorkflowVersion>=4?ExamSummary.BuildSelected(exam.Trials,exam.ReportManeuverIds):ExamSummary.Build(exam.Trials); Trial auto=summary.BestTrial;
            maneuver=preview??exam.OperatorSelectedManeuver??auto;
            ClinicalStyle.Form(this,"جزئیات معاینه و گزارش",new Size(710,700));
            Panel body=new Panel { Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.White }; Controls.Add(body);
            TableLayoutPanel table=ClinicalStyle.FormTable(); table.RowCount=0; body.Controls.Add(table);
            Button patient=ClinicalStyle.Button("مشاهده مشخصات مراجع",delegate { using(PatientForm f=new PatientForm(exam.Patient,true)) f.ShowDialog(this); },false);
            ClinicalStyle.Row(table,exam.Patient.FullName,patient,48);
            center=ClinicalStyle.TextField("ExamCenter",exam.CenterName); center.MaxLength=200;
            op=ClinicalStyle.TextField("ExamOperator",exam.OperatorName); op.MaxLength=100;
            // Workflow 4: the physician records the final interpretation in HLine, so the
            // normal operator screen no longer offers an expert-interpretation box.
            // Older records keep the field (and its historical value) untouched.
            if(exam.SelectionWorkflowVersion<4) { interpretation=ClinicalStyle.TextField("Interpretation",exam.ReviewedInterpretation); interpretation.Multiline=true; interpretation.MaxLength=250; }
            grade=ClinicalStyle.TextField("ManualGrade",exam.ReviewedQualityGrade); comment=ClinicalStyle.TextField("QualityComment",exam.ReviewedQualityComment);
            ClinicalStyle.Row(table,"مرکز این معاینه",center,44); ClinicalStyle.Row(table,"کارشناس این معاینه",op,44);
            if(interpretation!=null) ClinicalStyle.Row(table,"تفسیر کارشناس",interpretation,68);
            ClinicalStyle.Row(table,"درجهٔ کیفیت (دستی)",grade,44); ClinicalStyle.Row(table,"یادداشت کیفیت",comment,44);
            vext=ClinicalStyle.TextField("ManeuverVext",maneuver!=null && maneuver.ReviewedVextMl.HasValue?Fmt.N(maneuver.ReviewedVextMl.Value,2):"");
            source=ClinicalStyle.TextField("ManeuverVextSource",maneuver==null?"":maneuver.ReviewedVextSource);
            reviewed=new CheckBox { Text="مقدار و منبع را بازبینی کرده‌ام",Checked=maneuver!=null && maneuver.VextManuallyReviewed };
            ClinicalStyle.Row(table,maneuver==null?"ابتدا مانور سامانه را انتخاب کنید":"VEXT مانور #"+maneuver.AcquisitionSequence+" (mL)",vext,48);
            ClinicalStyle.Row(table,"منبع مقدار دستی",source,44); ClinicalStyle.Row(table,"تأیید بازبینی",reviewed,40);
            Trial shown=preview??maneuver;
            TextBox info=new TextBox { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Text="مرجع: ERS / ECCS 1993 — مقادیر بازسازی‌شده اعتبارسنجی نشده‌اند."+Environment.NewLine+(shown==null || shown.Result==null?"":shown.Result.InvalidReason+Environment.NewLine+string.Join(Environment.NewLine,shown.Result.Warnings.ToArray())) };
            ClinicalStyle.Row(table,"مرجع و وضعیت مانور",info,110);
            if(exam.ReviewedVextMl.HasValue) ClinicalStyle.Row(table,"مقدار قدیمی معاینه",new Label { Text=Fmt.N(exam.ReviewedVextMl.Value,2)+" mL؛ بدون انتساب تأییدشده به مانور",AutoSize=true },50);
            foreach(Control c in new Control[] {center,op,interpretation,grade,comment,vext,source,reviewed}) if(c!=null) c.Enabled=!exam.FinalizedAt.HasValue;
            if(maneuver==null) vext.Enabled=source.Enabled=reviewed.Enabled=false;
            error=new Label { Dock=DockStyle.Bottom,Height=32,ForeColor=Color.Firebrick,Padding=new Padding(16,0,16,0) }; Controls.Add(error);
            FlowLayoutPanel actions=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=58,FlowDirection=FlowDirection.RightToLeft,RightToLeft=RightToLeft.No,Padding=new Padding(12,4,12,4) };
            Button save=ClinicalStyle.Button(exam.FinalizedAt.HasValue?"بستن":"ذخیره",delegate { if(exam.FinalizedAt.HasValue) { DialogResult=DialogResult.Cancel; return; } string problem; if(Apply(out problem)) DialogResult=DialogResult.OK; else error.Text=problem; },true); save.Name="SaveDetails";
            Button cancel=ClinicalStyle.Button("انصراف",delegate { DialogResult=DialogResult.Cancel; },false); actions.Controls.Add(save); actions.Controls.Add(cancel); Controls.Add(actions); AcceptButton=save; CancelButton=cancel;
        }
        public bool Apply(out string problem)
        {
            problem=""; if(exam.FinalizedAt.HasValue) { problem="معاینه نهایی شده است."; return false; }
            if(string.IsNullOrWhiteSpace(center.Text)) { problem="نام مرکز را وارد کنید."; return false; }
            double amount=Fmt.Parse(vext.Text,double.NaN); bool has=vext.Text.Trim().Length>0;
            if(has && (maneuver==null || double.IsNaN(amount) || amount<0 || amount>12000 || !reviewed.Checked || string.IsNullOrWhiteSpace(op.Text) || string.IsNullOrWhiteSpace(source.Text))) { problem="VEXT به مقدار معتبر، منبع، نام بازبین و تأیید نیاز دارد."; return false; }
            exam.CenterName=center.Text.Trim(); exam.OperatorName=op.Text.Trim();
            // Workflow 4 has no interpretation control; an existing historical value is
            // left exactly as stored rather than being cleared by saving the form.
            if(interpretation!=null) exam.ReviewedInterpretation=interpretation.Text.Trim();
            exam.ReviewedQualityGrade=grade.Text.Trim(); exam.ReviewedQualityComment=comment.Text.Trim();
            if(maneuver!=null)
            {
                bool changed=maneuver.ReviewedVextMl!=(has?(double?)amount:null) || maneuver.ReviewedVextSource!=source.Text.Trim() || maneuver.VextReviewer!=op.Text.Trim() || maneuver.VextManuallyReviewed!=has;
                maneuver.ReviewedVextMl=has?(double?)amount:null; maneuver.VextManuallyReviewed=has; maneuver.ReviewedVextSource=has?source.Text.Trim():""; maneuver.VextReviewer=has?op.Text.Trim():"";
                if(changed) maneuver.VextReviewedAt=has?(DateTime?)DateTime.Now:null;
            }
            return true;
        }
    }
}
