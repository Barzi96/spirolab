using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace SpiroLab
{
    // Owns a deep copy; renderer and JSON writer consume this same finalized data.
    public sealed class ExaminationSnapshot
    {
        internal readonly Exam Data;
        internal readonly ExamSummary Summary;
        internal readonly Dictionary<string,object> Document;
        public readonly string SnapshotId;
        public readonly string Source;
        public readonly string Status;
        public readonly DateTime TestedAt;
        public readonly DateTime CaptureStartedAt;
        private ExaminationSnapshot(Exam exam)
        {
            Data=new Exam { ExaminationId=exam.ExaminationId,CenterName=exam.CenterName,OperatorName=exam.OperatorName,
                ReviewedInterpretation=exam.ReviewedInterpretation,ReviewedQualityGrade=exam.ReviewedQualityGrade,ReviewedQualityComment=exam.ReviewedQualityComment,
                Patient=exam.Patient.Clone(),LegacyRecord=exam.LegacyRecord,ExaminationDate=exam.ExaminationDate,ExaminationDateSource=exam.ExaminationDateSource,FinalizedAt=exam.FinalizedAt,ReviewedVextMl=exam.ReviewedVextMl,ReviewedVextSource=exam.ReviewedVextSource,VextManuallyReviewed=exam.VextManuallyReviewed,
                OperatorSelectedManeuverId=exam.OperatorSelectedManeuverId,SelectionMethod=exam.SelectionMethod,SelectionTimestamp=exam.SelectionTimestamp,SelectionOperatorName=exam.SelectionOperatorName,SelectionReason=exam.SelectionReason,PrimarySelectionRequired=exam.PrimarySelectionRequired };
            string problem;
            if(!Data.Patient.IsUsable(out problem)) throw new ArgumentException(problem);
            HashSet<string> ids=new HashSet<string>();
            foreach(Trial source in exam.Trials)
            {
                Trial t=source.Clone();
                if(string.IsNullOrEmpty(t.ManeuverId) || !ids.Add(t.ManeuverId)) throw new ArgumentException("شناسهٔ مانورها تکراری یا نامعتبر است.");
                if(t.Result==null) t.Result=SpiroAnalysis.Analyze(t,Data.Patient);
                foreach(double flow in t.Flow) if(double.IsNaN(flow) || double.IsInfinity(flow)) throw new ArgumentException("دادهٔ نامعتبر.");
                if(t.Result.Valid) for(int i=0;i<8;i++)
                { double value=Ui.SpiroReport.Value(t.Result,i); if(double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("شاخص نامعتبر."); }
                Data.Trials.Add(t);
            }
            Data.EnsureManeuverIdentities(); Data.ValidateSelection(false);
            Summary=ExamSummary.Build(Data.Trials);
            if(Summary.Best==null) throw new InvalidOperationException("حداقل یک مانور کامل با دادهٔ کافی لازم است.");
            // Display the primary plus two recommendations without changing aggregate values.
            Trial primary=Data.OperatorSelectedManeuver;
            if(primary!=null && !Summary.Trials.Contains(primary))
            {
                Summary.Trials.Clear(); Summary.Trials.Add(primary);
                foreach(Trial t in Summary.Candidates) { if(Summary.Trials.Count==3) break; if(t!=primary) Summary.Trials.Add(t); }
                Summary.Trials.Sort(delegate(Trial a,Trial b) { return a.AcquisitionSequence.CompareTo(b.AcquisitionSequence); });
            }
            bool simulated=Data.Trials[0].Simulated, imported=Data.Trials[0].Imported;
            foreach(Trial t in Data.Trials) if(t.Simulated!=simulated || t.Imported!=imported) throw new InvalidOperationException("منبع مانورها یکسان نیست.");
            Source=simulated?"simulation":imported?"replay":"usb_live";
            Status=Source=="usb_live"?"DEVELOPMENT — UNVERIFIED MEASUREMENTS":"NON-CLINICAL TEST DATA";
            SnapshotId=exam.FinalizedAt.HasValue && !string.IsNullOrEmpty(exam.ReportSnapshotId)?exam.ReportSnapshotId:Guid.NewGuid().ToString("N");
            CaptureStartedAt=Data.Trials[0].When;
            TestedAt=Data.ExaminationDate.HasValue?Data.ExaminationDate.Value.Date:CaptureStartedAt;
            if(Data.ReviewedVextMl.HasValue && (!Data.VextManuallyReviewed || string.IsNullOrWhiteSpace(Data.OperatorName) || string.IsNullOrWhiteSpace(Data.ReviewedVextSource) || double.IsNaN(Data.ReviewedVextMl.Value) || double.IsInfinity(Data.ReviewedVextMl.Value) || Data.ReviewedVextMl<0 || Data.ReviewedVextMl>12000)) throw new ArgumentException("مقدار VEXT باید با نام کارشناس، منبع و تأیید بازبینی ثبت شود.");
            Document=BuildDocument();
        }
        public static ExaminationSnapshot Capture(Exam exam) { return new ExaminationSnapshot(exam); }
        public static ExaminationSnapshot Capture(Patient p,IList<Trial> trials)
        { Exam e=new Exam { Patient=p }; e.Trials.AddRange(trials); return Capture(e); }
        public const int MaxJsonCharacters=64*1024*1024;
        public string ToJson() { return new JavaScriptSerializer { MaxJsonLength=MaxJsonCharacters }.Serialize(Document); }
        public int BestIndex { get { return Index(Summary.BestTrial); } }
        public string OperatorSelectedManeuverId { get { return Data.OperatorSelectedManeuverId; } }
        public int OperatorSelectedIndex { get { return Data.OperatorSelectedManeuver==null?0:Index(Data.OperatorSelectedManeuver); } }
        public string PrimaryLabel { get { return OperatorSelectedIndex>0?"PRE #"+OperatorSelectedIndex:"unavailable (legacy / not selected)"; } }
        public int ManeuverCount { get { return Data.Trials.Count; } }
        internal int Index(Trial t) { return t.AcquisitionSequence; }
        internal string InterpretationText { get { return string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?"Algorithmic / UNVALIDATED: "+Summary.Interpretation:"Clinician-entered: "+Data.ReviewedInterpretation; } }
        private object Measurement(double? value,string unit,double? predicted,string source,string verification,string missing)
        { return new { value=value,unit=unit,predicted=predicted,percentPredicted=value.HasValue && predicted.HasValue && predicted.Value>0?(double?)(value.Value/predicted.Value*100):null,valueSource=source,verificationStatus=verification,missingDataStatus=missing }; }
        private object Measure(AnalysisResult r,int i,double value,string source)
        { double pred=Ui.SpiroReport.Predicted(r.Ref,i); return Measurement(value,Ui.SpiroReport.Unit(i),pred>0?(double?)pred:null,source,"unverified_reconstruction","available"); }
        private string Provider(string metric)
        {
            foreach(Trial t in Summary.Candidates)
                if((metric=="FVC" && t.Result.Fvc==Summary.Fvc) || (metric=="FEV1" && t.Result.Fev1==Summary.Fev1) || (metric=="PEF" && t.Result.Pef==Summary.Pef)) return t.ManeuverId;
            return Summary.BestTrial.ManeuverId;
        }
        private Dictionary<string,object> BuildDocument()
        {
            Dictionary<string,object> d=new Dictionary<string,object>();
            d["schemaVersion"]=2;
            d["patient"]=new { nationalId=Data.Patient.NationalId,firstName=Data.Patient.FirstName,lastName=Data.Patient.LastName,age=Data.Patient.Age,sex=Data.Patient.Sex.ToString(),origin=Data.Patient.Origin.ToString(),heightCm=Data.Patient.HeightCm,weightKg=Data.Patient.WeightKg };
            d["examination"]=new { id=Data.ExaminationId,snapshotId=SnapshotId,type="PRE",testedAt=TestedAt.ToString("o",CultureInfo.InvariantCulture),examinationDate=ExaminationDates.Format(TestedAt),dateSource=Data.ExaminationDateSource,captureStartedAt=CaptureStartedAt.ToString("o",CultureInfo.InvariantCulture),operatorName=Data.OperatorName,operatorStatus=string.IsNullOrWhiteSpace(Data.OperatorName)?"unavailable":"manually_entered",centerName=Data.CenterName,maneuverCount=Data.Trials.Count,includedManeuverCount=Summary.Candidates.Count,bestManeuverId=Summary.BestTrial.ManeuverId,bestManeuverIndex=BestIndex };
            d["device"]=new { model="MIR Spirolab (protocol family; model not verified)",serial=Summary.BestTrial.DeviceSerial,hardwareValidation="HARDWARE VALIDATION PENDING" };
            d["review"]=new { finalizedAt=Data.FinalizedAt.HasValue?Data.FinalizedAt.Value.ToString("o",CultureInfo.InvariantCulture):null,manualVext=Data.ReviewedVextMl.HasValue?(object)new { reviewer=Data.OperatorName,source=Data.ReviewedVextSource,reviewed=Data.VextManuallyReviewed,selectionMethod="explicit_operator_entry_no_automatic_EVol_selection" }:null };
            d["primaryManeuverSelection"]=new { operatorSelectedManeuverId=string.IsNullOrEmpty(Data.OperatorSelectedManeuverId)?null:Data.OperatorSelectedManeuverId,originalSequence=OperatorSelectedIndex>0?(int?)OperatorSelectedIndex:null,method=Data.SelectionMethod,selectedAt=Data.SelectionTimestamp.HasValue?Data.SelectionTimestamp.Value.ToString("o",CultureInfo.InvariantCulture):null,operatorName=Data.SelectionOperatorName,reason=Data.SelectionReason,status=OperatorSelectedIndex>0?"explicitly_selected":"unavailable_legacy_or_unselected",automaticRecommendationId=Summary.BestTrial.ManeuverId,automaticRecommendationSequence=BestIndex };
            Dictionary<string,object> values=new Dictionary<string,object>();
            values["FVC"]=Measure(Summary.Best,0,Summary.Fvc,Provider("FVC")); values["FEV1"]=Measure(Summary.Best,1,Summary.Fev1,Provider("FEV1"));
            values["FEV1_FVC"]=Measure(Summary.Best,2,Summary.Ratio,"independent_max_FEV1_divided_by_max_FVC");
            values["FEF25_75"]=Measure(Summary.Best,7,Summary.Best.Fef2575,Summary.BestTrial.ManeuverId); values["PEF"]=Measure(Summary.Best,3,Summary.Pef,Provider("PEF"));
            values["VEXT"]=Data.ReviewedVextMl.HasValue?Measurement(Data.ReviewedVextMl,"mL",null,"manual_review: "+Data.ReviewedVextSource,"manually_reviewed_not_device_verified","available"):Measurement(null,"mL",null,"not_available","not_validated","unavailable"); d["spirometry"]=values;
            d["bestReportedValues"]=values; // schema2 compatibility: spirometry retains the same aggregate values.
            List<object> maneuvers=new List<object>();
            foreach(Trial t in Data.Trials)
            {
                Dictionary<string,object> metrics=new Dictionary<string,object>();
                if(t.Result.Valid) for(int i=0;i<8;i++) metrics[Ui.SpiroReport.MetricName(i)]=Measure(t.Result,i,Ui.SpiroReport.Value(t.Result,i),t.ManeuverId);
                maneuvers.Add(new { id=t.ManeuverId,index=Index(t),acquisitionSequence=t.AcquisitionSequence,testedAt=t.When.ToString("o",CultureInfo.InvariantCulture),completed=t.Completed,signalUsable=t.Result.Valid,status=t.OperatorRejected?"operator_rejected":!t.Completed?"incomplete":!t.Result.Valid?"invalid":"complete_signal_usable",includedInBestReportedValues=ExamSummary.CanReport(t),source=t.Simulated?"simulation":t.Imported?"replay":"usb_live",endReason=t.EndReason,quality=new { invalidReason=t.Result.InvalidReason,warnings=t.Result.Warnings,clinicalAcceptance="not_automatically_validated" },rejection=new { rejected=t.OperatorRejected,operatorName=t.RejectionOperator,reason=t.RejectionReason,reviewedAt=t.RejectionTimestamp.HasValue?t.RejectionTimestamp.Value.ToString("o",CultureInfo.InvariantCulture):null },rawSampleCount=t.Flow.Count,rawPacketCount=t.Packets.Count,rawSampleStorage="local_examination_record",verificationStatus="unverified",measurements=metrics,estimatedBEV=t.Result.Valid && t.Result.BevAvailable?Measurement(t.Result.Bev*1000,"mL",null,"volume_at_peak_tangent_time_zero","not_equivalent_to_validated_VEXT","available"):null,backExtrapolation=new { timeZeroSeconds=t.Result.BevAvailable?(double?)t.Result.TimeZero:null,method="peak_tangent_time_zero_then_interpolated_volume",sourceAdequacy=t.Result.BevSourceAdequacy,timedVolumeOrigin="reconstructed_segment_origin_not_device_time_zero",evolDeviceParity="not_validated" } });
            }
            d["maneuvers"]=maneuvers;
            d["interpretation"]=new { text=string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?Summary.Interpretation:Data.ReviewedInterpretation,valueSource=string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?"unvalidated_algorithm":"clinician_entered",verificationStatus=string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?"not_validated":"manual_entry_not_independently_verified" };
            d["quality"]=new { grade=string.IsNullOrWhiteSpace(Data.ReviewedQualityGrade)?null:Data.ReviewedQualityGrade,comment=Data.ReviewedQualityComment,valueSource=string.IsNullOrWhiteSpace(Data.ReviewedQualityGrade)?"unavailable":"clinician_entered",verificationStatus="not_automatically_validated",repeatability=Summary.Repeatability };
            List<object> displayed=new List<object>(); foreach(Trial t in Summary.Trials) displayed.Add(new { id=t.ManeuverId,index=Index(t) });
            d["report"]=new { file="Spirometry-Report.jpg",format="image/jpeg",application="Independent SpiroLab 5.1.1",reference="ERS/ECCS 1993",displayedManeuvers=displayed,displayPolicy="operator_primary_plus_highest_sum_eligible_up_to_three_in_original_order",operatorSelectedManeuverId=string.IsNullOrEmpty(Data.OperatorSelectedManeuverId)?null:Data.OperatorSelectedManeuverId,primaryManeuverLabel=PrimaryLabel,predictedCurve="not_generated_no_validated_method",vextDisplay=Data.ReviewedVextMl.HasValue?"manual_review":"unavailable",selectionPolicy="eligible_complete_nonrejected;FVC_FEV1_PEF_independent_max;FEF25_75_from_largest_FVC_plus_FEV1;ratio_max_FEV1_div_max_FVC",selectionVerification="reference_supported;ratio_and_exact_MIR_firmware_behavior_pending_paired_validation" };
            d["provenance"]=new { source=Source,status=Status,measurementVerification="unverified",clinicalValidation="pending",hardwareValidation="HARDWARE VALIDATION PENDING",legacyRecord=Data.LegacyRecord,btpsCorrection="not_verified",vextReason="BEV estimate has not been validated as the occupational-health VEXT field" };
            return d;
        }
    }
}
