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
        internal readonly ExamSummary AllSummary;
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
                OperatorSelectedManeuverId=exam.OperatorSelectedManeuverId,SelectionMethod=exam.SelectionMethod,SelectionTimestamp=exam.SelectionTimestamp,SelectionOperatorName=exam.SelectionOperatorName,SelectionReason=exam.SelectionReason,PrimarySelectionRequired=exam.PrimarySelectionRequired,
                SelectionWorkflowVersion=exam.SelectionWorkflowVersion,ReportManeuverIds=new List<string>(exam.ReportManeuverIds),ReportSelectionManual=exam.ReportSelectionManual,ReportSelectionTimestamp=exam.ReportSelectionTimestamp,ReportSelectionOperator=exam.ReportSelectionOperator };
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
            Data.EnsureManeuverIdentities(); Data.ValidateSelection(!string.IsNullOrEmpty(Data.OperatorSelectedManeuverId)); Data.ValidateReportSelection(Data.SelectionWorkflowVersion>=2);
            AllSummary=ExamSummary.Build(Data.Trials);
            Summary=Data.SelectionWorkflowVersion>=2?ExamSummary.Build(Data.Trials.FindAll(delegate(Trial t) { return Data.ReportManeuverIds.Contains(t.ManeuverId); }),false):ExamSummary.Build(Data.Trials,false);
            if(Summary.Best==null) throw new InvalidOperationException("حداقل یک مانور کامل با دادهٔ کافی لازم است.");
            // Display the primary plus two recommendations without changing aggregate values.
            Trial primary=Data.OperatorSelectedManeuver;
            if(Data.SelectionWorkflowVersion<2 && primary!=null && !Summary.Trials.Contains(primary))
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
            d["patientContext"]=new {fullName=Data.Patient.FullName,serverFullName=Data.Patient.ServerFullName,birthDate=Data.Patient.BirthDate.HasValue?Data.Patient.BirthDate.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):null,referenceAgeYears=Data.Patient.ReferenceAgeYears,demographicSource=Data.Patient.DemographicSource,anthropometrySource=Data.Patient.AnthropometrySource,anthropometryConfirmed=Data.Patient.AnthropometryConfirmed};
            d["hlineDestination"]=new {personId=Data.Patient.HLinePersonId,medicalRecordId=Data.Patient.HLineRecordId,recordDate=Data.Patient.HLineRecordDate,recordStatus=Data.Patient.HLineRecordStatus,writeContract="not_live_verified_no_clinical_write_transport"};
            d["selectionRecommendation"]=new {method="quality_screen_tiers_then_FVC_plus_FEV1_stable_acquisition_order",scientificSource="ATS_ERS_2019",verification="software_rules_tested_clinical_and_MIR_equivalence_unverified",deviceRanking="not_demonstrated_by_observed_USB_protocol"};
            d["examination"]=new { id=Data.ExaminationId,snapshotId=SnapshotId,type="PRE",testedAt=TestedAt.ToString("o",CultureInfo.InvariantCulture),examinationDate=ExaminationDates.Format(TestedAt),dateSource=Data.ExaminationDateSource,captureStartedAt=CaptureStartedAt.ToString("o",CultureInfo.InvariantCulture),operatorName=Data.OperatorName,operatorStatus=string.IsNullOrWhiteSpace(Data.OperatorName)?"unavailable":"manually_entered",centerName=Data.CenterName,maneuverCount=Data.Trials.Count,includedManeuverCount=Summary.Candidates.Count,bestManeuverId=Summary.BestTrial.ManeuverId,bestManeuverIndex=BestIndex };
            d["device"]=new { model="MIR Spirolab (protocol family; model not verified)",serial=Summary.BestTrial.DeviceSerial,hardwareValidation="HARDWARE VALIDATION PENDING" };
            d["review"]=new { finalizedAt=Data.FinalizedAt.HasValue?Data.FinalizedAt.Value.ToString("o",CultureInfo.InvariantCulture):null,manualVext=Data.ReviewedVextMl.HasValue?(object)new { reviewer=Data.OperatorName,source=Data.ReviewedVextSource,reviewed=Data.VextManuallyReviewed,selectionMethod="explicit_operator_entry_no_automatic_EVol_selection" }:null };
            d["primaryManeuverSelection"]=new { operatorSelectedManeuverId=string.IsNullOrEmpty(Data.OperatorSelectedManeuverId)?null:Data.OperatorSelectedManeuverId,originalSequence=OperatorSelectedIndex>0?(int?)OperatorSelectedIndex:null,method=Data.SelectionMethod,selectedAt=Data.SelectionTimestamp.HasValue?Data.SelectionTimestamp.Value.ToString("o",CultureInfo.InvariantCulture):null,operatorName=Data.SelectionOperatorName,reason=Data.SelectionReason,status=OperatorSelectedIndex>0?(Data.SelectionMethod=="automatic"?"automatically_selected":"explicitly_selected"):"unavailable_legacy_or_unselected",automaticRecommendationId=AllSummary.BestTrial.ManeuverId,automaticRecommendationSequence=Index(AllSummary.BestTrial) };
            Dictionary<string,object> values=new Dictionary<string,object>();
            values["FVC"]=Measure(Summary.Best,0,Summary.Fvc,Provider("FVC")); values["FEV1"]=Measure(Summary.Best,1,Summary.Fev1,Provider("FEV1"));
            values["FEV1_FVC"]=Measure(Summary.Best,2,Summary.Ratio,"independent_max_FEV1_divided_by_max_FVC");
            values["FEF25_75"]=Measure(Summary.Best,7,Summary.Best.Fef2575,Summary.BestTrial.ManeuverId); values["PEF"]=Measure(Summary.Best,3,Summary.Pef,Provider("PEF"));
            values["VEXT"]=Data.ReviewedVextMl.HasValue?Measurement(Data.ReviewedVextMl,"mL",null,"manual_review: "+Data.ReviewedVextSource,"manually_reviewed_not_device_verified","available"):Measurement(null,"mL",null,"not_available","not_validated","unavailable"); d["spirometry"]=values;
            d["bestReportedValues"]=values; // schema2 compatibility: spirometry retains the same aggregate values.
            d["spirometryRole"]="legacy_alias_of_report_aggregate_use_siteExport_for_single_maneuver";
            VextSelection clinic=ClinicVext.Select(Data.Trials);
            Trial site=Data.OperatorSelectedManeuver;
            double? siteEvol=site!=null && site.Result!=null && site.Result.BevAvailable?(double?)(site.Result.Bev*1000.0):null;
            Dictionary<string,object> siteValues=new Dictionary<string,object>();
            if(site!=null)
            {
                int[] siteIndices={0,1,2,3,7}; string[] names={"FVC","FEV1","FEV1_FVC","PEF","FEF25_75"};
                for(int i=0;i<siteIndices.Length;i++) siteValues[names[i]]=Measure(site.Result,siteIndices[i],Ui.SpiroReport.Value(site.Result,siteIndices[i]),site.ManeuverId);
                if(site.ReviewedVextMl.HasValue && (!site.VextManuallyReviewed || string.IsNullOrWhiteSpace(site.VextReviewer) || string.IsNullOrWhiteSpace(site.ReviewedVextSource) || double.IsNaN(site.ReviewedVextMl.Value) || double.IsInfinity(site.ReviewedVextMl.Value) || site.ReviewedVextMl<0 || site.ReviewedVextMl>12000)) throw new ArgumentException("بازبینی VEXT مانور معتبر نیست.");
                siteValues["VEXT"]=new { value=clinic.Available?(double?)clinic.ValueMl:null,unit="mL",predicted=(double?)null,percentPredicted=(double?)null,valueSource=clinic.Available?"clinic_rule_max_evol_below_200ml:maneuver:"+clinic.ManeuverId:"not_available_no_eligible_real_maneuver",verificationStatus=clinic.Available?"calculated_from_reconstructed_real_samples_not_paired_MIR_verified":"not_validated",missingDataStatus=clinic.Available?"available":"unavailable",sourceManeuverId=clinic.Available?clinic.ManeuverId:null,sourceAcquisitionSequence=clinic.Available?(int?)clinic.AcquisitionSequence:null,selectionRule="clinic_max_0_lt_EVol_lt_200ml_across_real_maneuvers",selectedManeuverId=site.ManeuverId,selectedManeuverEvolMl=siteEvol };
            }
            d["siteExport"]=new { maneuverId=site==null?null:site.ManeuverId,acquisitionSequence=site==null?(int?)null:site.AcquisitionSequence,selectionMethod=Data.SelectionMethod,selectedAt=Data.SelectionTimestamp.HasValue?Data.SelectionTimestamp.Value.ToString("o",CultureInfo.InvariantCulture):null,selectedBy=Data.SelectionOperatorName,measurements=siteValues,transport="local_only_no_upload",vextReviewer=site==null?null:site.VextReviewer,vextSource=site==null?null:site.ReviewedVextSource };
            d["vextSelection"]=new { rule="max(0<EVol<200 mL) across real maneuvers of this examination",scope="real_usb_live_maneuvers_only",qualityGate="none_ats_ers_assessment_is_separate",valueMl=clinic.Available?(double?)clinic.ValueMl:null,sourceManeuverId=clinic.Available?clinic.ManeuverId:null,sourceAcquisitionSequence=clinic.Available?(int?)clinic.AcquisitionSequence:null,selectedManeuverId=site==null?null:site.ManeuverId,selectedManeuverEvolMl=siteEvol,evolSource="device_reported_field_not_present_in_observed_protocol;calculated_volume_at_peak_tangent_time_zero",calculationProvenance="reconstructed_expiration_origin_not_device_time_zero",verification="not_paired_MIR_verified",problem=clinic.Available?null:clinic.Problem };
            d["reportSelection"]=new { maneuverIds=Data.ReportManeuverIds,method=Data.ReportSelectionManual?"manual":"automatic",selectedAt=Data.ReportSelectionTimestamp.HasValue?Data.ReportSelectionTimestamp.Value.ToString("o",CultureInfo.InvariantCulture):null,selectedBy=Data.ReportSelectionOperator,workflowVersion=Data.SelectionWorkflowVersion };
            List<object> maneuvers=new List<object>();
            foreach(Trial t in Data.Trials)
            {
                Dictionary<string,object> metrics=new Dictionary<string,object>();
                ManeuverAssessment assessment=ManeuverAssessment.Assess(t);
                if(t.Result.Valid) for(int i=0;i<8;i++) metrics[Ui.SpiroReport.MetricName(i)]=Measure(t.Result,i,Ui.SpiroReport.Value(t.Result,i),t.ManeuverId);
                maneuvers.Add(new { id=t.ManeuverId,index=Index(t),acquisitionSequence=t.AcquisitionSequence,testedAt=t.When.ToString("o",CultureInfo.InvariantCulture),completed=t.Completed,captureAssessment=assessment,operatorStopped=t.OperatorStopped,captureError=t.CaptureError,signalUsable=t.Result.Valid,status=t.OperatorRejected?"operator_rejected":!t.Completed?"incomplete":!t.Result.Valid?"invalid":"complete_signal_usable",includedInBestReportedValues=Summary.Candidates.Contains(t),eligibleForSelection=ExamSummary.CanReport(t),source=t.Simulated?"simulation":t.Imported?"replay":"usb_live",endReason=t.EndReason,quality=new { invalidReason=t.Result.InvalidReason,warnings=t.Result.Warnings,clinicalAcceptance="not_automatically_validated" },rejection=new { rejected=t.OperatorRejected,operatorName=t.RejectionOperator,reason=t.RejectionReason,reviewedAt=t.RejectionTimestamp.HasValue?t.RejectionTimestamp.Value.ToString("o",CultureInfo.InvariantCulture):null },rawSampleCount=t.Flow.Count,rawPacketCount=t.Packets.Count,rawSampleStorage="local_examination_record",verificationStatus="unverified",measurements=metrics,vextReview=t.ReviewedVextMl.HasValue?new { valueMl=t.ReviewedVextMl.Value,reviewer=t.VextReviewer,source=t.ReviewedVextSource,reviewed=t.VextManuallyReviewed,role="legacy_local_review_not_the_HLine_vext_field" }:null,estimatedBEV=t.Result.Valid && t.Result.BevAvailable?Measurement(t.Result.Bev*1000,"mL",null,"volume_at_peak_tangent_time_zero","not_equivalent_to_validated_VEXT","available"):null,backExtrapolation=new { timeZeroSeconds=t.Result.BevAvailable?(double?)t.Result.TimeZero:null,method="peak_tangent_time_zero_then_interpolated_volume",sourceAdequacy=t.Result.BevSourceAdequacy,timedVolumeOrigin="reconstructed_segment_origin_not_device_time_zero",evolDeviceParity="not_validated" } });
            }
            d["maneuvers"]=maneuvers;
            d["interpretation"]=new { text=string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?Summary.Interpretation:Data.ReviewedInterpretation,valueSource=string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?"unvalidated_algorithm":"clinician_entered",verificationStatus=string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?"not_validated":"manual_entry_not_independently_verified" };
            d["quality"]=new { grade=string.IsNullOrWhiteSpace(Data.ReviewedQualityGrade)?null:Data.ReviewedQualityGrade,comment=Data.ReviewedQualityComment,valueSource=string.IsNullOrWhiteSpace(Data.ReviewedQualityGrade)?"unavailable":"clinician_entered",verificationStatus="not_automatically_validated",repeatability=Summary.Repeatability };
            List<object> displayed=new List<object>(); foreach(Trial t in Summary.Trials) displayed.Add(new { id=t.ManeuverId,index=Index(t) });
            d["report"]=new { file="Spirometry-Report.jpg",format="image/jpeg",application="Independent SpiroLab 5.3.1",reference="ERS/ECCS 1993",displayedManeuvers=displayed,displayPolicy=Data.SelectionWorkflowVersion>=2?"explicit_report_ids_in_original_acquisition_order":"legacy_primary_and_recommendations",operatorSelectedManeuverId=string.IsNullOrEmpty(Data.OperatorSelectedManeuverId)?null:Data.OperatorSelectedManeuverId,primaryManeuverLabel=PrimaryLabel,predictedCurve="not_generated_no_validated_method",vextDisplay=Data.ReviewedVextMl.HasValue?"manual_review":"unavailable",selectionPolicy="eligible_complete_nonrejected;FVC_FEV1_PEF_independent_max;FEF25_75_from_largest_FVC_plus_FEV1;ratio_max_FEV1_div_max_FVC",selectionVerification="reference_supported;ratio_and_exact_MIR_firmware_behavior_pending_paired_validation" };
            d["provenance"]=new { source=Source,status=Status,measurementVerification="unverified",clinicalValidation="pending",hardwareValidation="HARDWARE VALIDATION PENDING",legacyRecord=Data.LegacyRecord,btpsCorrection="not_verified",vextReason="HLine VEXT follows the clinic convention max(0<EVol<200 mL) over real maneuvers; EVol timing is reconstructed and not paired-MIR verified" };
            return d;
        }
    }
}
