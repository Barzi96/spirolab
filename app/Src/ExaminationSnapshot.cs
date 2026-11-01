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
        private ExaminationSnapshot(Exam exam)
        {
            Data=new Exam { ExaminationId=exam.ExaminationId,CenterName=exam.CenterName,OperatorName=exam.OperatorName,
                ReviewedInterpretation=exam.ReviewedInterpretation,ReviewedQualityGrade=exam.ReviewedQualityGrade,ReviewedQualityComment=exam.ReviewedQualityComment,
                Patient=exam.Patient.Clone(),LegacyRecord=exam.LegacyRecord };
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
            Summary=ExamSummary.Build(Data.Trials);
            if(Summary.Best==null) throw new InvalidOperationException("حداقل یک مانور کامل با دادهٔ کافی لازم است.");
            bool simulated=Data.Trials[0].Simulated, imported=Data.Trials[0].Imported;
            foreach(Trial t in Data.Trials) if(t.Simulated!=simulated || t.Imported!=imported) throw new InvalidOperationException("منبع مانورها یکسان نیست.");
            Source=simulated?"simulation":imported?"replay":"usb_live";
            Status=Source=="usb_live"?"DEVELOPMENT — UNVERIFIED MEASUREMENTS":"NON-CLINICAL TEST DATA";
            SnapshotId=Guid.NewGuid().ToString("N");
            TestedAt=Data.Trials[0].When;
            Document=BuildDocument();
        }
        public static ExaminationSnapshot Capture(Exam exam) { return new ExaminationSnapshot(exam); }
        public static ExaminationSnapshot Capture(Patient p,IList<Trial> trials)
        { Exam e=new Exam { Patient=p }; e.Trials.AddRange(trials); return Capture(e); }
        public string ToJson() { return new JavaScriptSerializer { MaxJsonLength=16000000 }.Serialize(Document); }
        public int BestIndex { get { return Data.Trials.IndexOf(Summary.BestTrial)+1; } }
        public int ManeuverCount { get { return Data.Trials.Count; } }
        internal int Index(Trial t) { return Data.Trials.IndexOf(t)+1; }
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
            d["examination"]=new { id=Data.ExaminationId,snapshotId=SnapshotId,type="PRE",testedAt=TestedAt.ToString("o",CultureInfo.InvariantCulture),operatorName=Data.OperatorName,operatorStatus=string.IsNullOrWhiteSpace(Data.OperatorName)?"unavailable":"manually_entered",centerName=Data.CenterName,maneuverCount=Data.Trials.Count,includedManeuverCount=Summary.Candidates.Count,bestManeuverId=Summary.BestTrial.ManeuverId,bestManeuverIndex=BestIndex };
            d["device"]=new { model="MIR Spirolab (protocol family; model not verified)",serial=Summary.BestTrial.DeviceSerial,hardwareValidation="HARDWARE VALIDATION PENDING" };
            Dictionary<string,object> values=new Dictionary<string,object>();
            values["FVC"]=Measure(Summary.Best,0,Summary.Fvc,Provider("FVC")); values["FEV1"]=Measure(Summary.Best,1,Summary.Fev1,Provider("FEV1"));
            values["FEV1_FVC"]=Measure(Summary.Best,2,Summary.Ratio,"independent_max_FEV1_divided_by_max_FVC");
            values["FEF25_75"]=Measure(Summary.Best,7,Summary.Best.Fef2575,Summary.BestTrial.ManeuverId); values["PEF"]=Measure(Summary.Best,3,Summary.Pef,Provider("PEF"));
            values["VEXT"]=Measurement(null,"mL",null,"not_available","not_validated","unavailable"); d["spirometry"]=values;
            List<object> maneuvers=new List<object>();
            foreach(Trial t in Data.Trials)
            {
                Dictionary<string,object> metrics=new Dictionary<string,object>();
                if(t.Result.Valid) for(int i=0;i<8;i++) metrics[Ui.SpiroReport.MetricName(i)]=Measure(t.Result,i,Ui.SpiroReport.Value(t.Result,i),t.ManeuverId);
                maneuvers.Add(new { id=t.ManeuverId,index=Index(t),testedAt=t.When.ToString("o",CultureInfo.InvariantCulture),completed=t.Completed,signalUsable=t.Result.Valid,verificationStatus="unverified",measurements=metrics,estimatedBEV=t.Result.Valid?Measurement(t.Result.Bev*1000,"mL",null,"peak_tangent_estimate","not_equivalent_to_validated_VEXT","available"):null });
            }
            d["maneuvers"]=maneuvers;
            d["interpretation"]=new { text=string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?Summary.Interpretation:Data.ReviewedInterpretation,valueSource=string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?"unvalidated_algorithm":"clinician_entered",verificationStatus=string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?"not_validated":"manual_entry_not_independently_verified" };
            d["quality"]=new { grade=string.IsNullOrWhiteSpace(Data.ReviewedQualityGrade)?null:Data.ReviewedQualityGrade,comment=Data.ReviewedQualityComment,valueSource=string.IsNullOrWhiteSpace(Data.ReviewedQualityGrade)?"unavailable":"clinician_entered",verificationStatus="not_automatically_validated",repeatability=Summary.Repeatability };
            List<object> displayed=new List<object>(); foreach(Trial t in Summary.Trials) displayed.Add(new { id=t.ManeuverId,index=Index(t) });
            d["report"]=new { file="Spirometry-Report.jpg",format="image/jpeg",application="Independent SpiroLab 5",reference="ERS/ECCS 1993",displayedManeuvers=displayed,predictedCurve="not_generated_no_validated_method",vextDisplay="unavailable" };
            d["provenance"]=new { source=Source,status=Status,measurementVerification="unverified",clinicalValidation="pending",hardwareValidation="HARDWARE VALIDATION PENDING",legacyRecord=Data.LegacyRecord,btpsCorrection="not_verified",vextReason="BEV estimate has not been validated as the occupational-health VEXT field" };
            return d;
        }
    }
}
