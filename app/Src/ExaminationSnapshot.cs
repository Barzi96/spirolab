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
        public readonly string SelectionWarning;
        public readonly DateTime TestedAt;
        public readonly DateTime CaptureStartedAt;
        private ExaminationSnapshot(Exam exam)
        {
            Data=new Exam { ExaminationId=exam.ExaminationId,SessionType=exam.SessionType,CenterName=exam.CenterName,OperatorName=exam.OperatorName,
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
            Data.EnsureManeuverIdentities(); Data.ValidateSelection(false); Data.ValidateReportSelection(Data.SelectionWorkflowVersion>=2);
            // Workflow 4 makes the selected set authoritative. Older finalized v3
            // records keep their historical all-eligible aggregate on read-back.
            AllSummary=ExamSummary.Build(Data.Trials);
            Summary=Data.SelectionWorkflowVersion>=4?ExamSummary.BuildSelected(Data.Trials,Data.ReportManeuverIds):AllSummary;
            SelectionWarning=BuildSelectionWarning();
            if(Summary.Best==null || !Summary.FvcAvailable) throw new InvalidOperationException("حداقل یک مانور کامل با دادهٔ کافی لازم است.");
            List<Trial> display=new List<Trial>();
            foreach(string id in Data.ReportManeuverIds) { Trial t=Data.Trials.Find(delegate(Trial x){return x.ManeuverId==id;}); if(t!=null && Summary.Candidates.Contains(t)) display.Add(t); }
            // Legacy records keep their historical primary visible, but it never
            // changes the session aggregate.
            Trial primary=Data.OperatorSelectedManeuver;
            if(Data.SelectionWorkflowVersion<3 && primary!=null && Summary.Candidates.Contains(primary))
            {
                display.Remove(primary); display.Insert(0,primary);
                foreach(Trial t in Summary.Candidates) { if(display.Count>=3) break; if(!display.Contains(t)) display.Add(t); }
            }
            if(display.Count==0 && Data.SelectionWorkflowVersion<3) foreach(Trial t in Summary.Candidates) { if(display.Count==3) break; display.Add(t); }
            Summary.SetDisplay(display);
            bool simulated=Data.Trials[0].Simulated, imported=Data.Trials[0].Imported;
            foreach(Trial t in Data.Trials) if(t.Simulated!=simulated || t.Imported!=imported) throw new InvalidOperationException("منبع مانورها یکسان نیست.");
            Source=simulated?"simulation":imported?"replay":"usb_live";
            // Honest status: measurements are captured from the device over USB, but
            // full agreement with official MIR output and BTPS is not yet validated
            // independently. Non-device sources stay classified as test data.
            Status=Source=="usb_live"?"DEVICE DATA CAPTURED OVER USB — MIR-EQUIVALENCE AND BTPS NOT INDEPENDENTLY VALIDATED":"NON-CLINICAL TEST DATA";
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
        public string PrimaryLabel { get { return Data.SelectionWorkflowVersion>=4?"BEST (selected)":Data.SelectionWorkflowVersion>=3?"BEST (aggregate)":OperatorSelectedIndex>0?"PRE #"+OperatorSelectedIndex:"unavailable (legacy / not selected)"; } }
        public int ManeuverCount { get { return Data.Trials.Count; } }
        internal int Index(Trial t) { return t.AcquisitionSequence; }
        internal string InterpretationText { get { return string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?Summary.Interpretation:Data.ReviewedInterpretation; } }
        private object Measurement(double? value,string unit,double? predicted,string source,string verification,string missing)
        { return new { value=value,unit=unit,predicted=predicted,percentPredicted=value.HasValue && predicted.HasValue && predicted.Value>0?(double?)(value.Value/predicted.Value*100):null,valueSource=source,verificationStatus=verification,missingDataStatus=missing }; }
        private object Measurement(MeasuredValue m)
        {
            return new { value=m.Value,unit=m.Unit,predicted=m.Predicted,percentPredicted=m.PercentPredicted,valueSource=m.SourceCode,sourceManeuverId=string.IsNullOrEmpty(m.SourceManeuverId)?null:m.SourceManeuverId,sourceAcquisitionSequence=m.AcquisitionSequence>0?(int?)m.AcquisitionSequence:null,verificationStatus=m.ValidationStatus,precision=m.Resolution,method=string.IsNullOrEmpty(m.Method)?null:m.Method,missingDataStatus=m.Available?"available":m.MissingReason };
        }
        private static string MetricNameForIndex(int index)
        {
            switch(index)
            {
                case 0:return Metric.Fvc;
                case 1:return Metric.Fev1;
                case 2:return Metric.Fev1Fvc;
                case 3:return Metric.Pef;
                case 4:return Metric.Fef25;
                case 5:return Metric.Fef50;
                case 6:return Metric.Fef75;
                case 7:return Metric.Fef2575;
                default:return Metric.Bev;
            }
        }
        private static string[] SourceIds(SessionBestValues best)
        {
            if(best==null) return new string[0];
            List<string> ids=new List<string>();
            foreach(string id in best.SourceManeuverIds) ids.Add(id);
            return ids.ToArray();
        }
        private bool IsBestSource(string id)
        {
            if(Summary.BestValues==null || string.IsNullOrEmpty(id)) return false;
            foreach(string source in Summary.BestValues.SourceManeuverIds) if(source==id) return true;
            return false;
        }
        private string BuildSelectionWarning()
        {
            if(Data.SelectionWorkflowVersion<4 || AllSummary==null || Summary==null || AllSummary.BestValues==null || Summary.BestValues==null) return "";
            bool fvc=AllSummary.BestValues.Fvc.Available && Summary.BestValues.Fvc.Available && AllSummary.BestValues.Fvc.Value.Value>Summary.BestValues.Fvc.Value.Value+1e-9;
            bool fev1=AllSummary.BestValues.Fev1.Available && Summary.BestValues.Fev1.Available && AllSummary.BestValues.Fev1.Value.Value>Summary.BestValues.Fev1.Value.Value+1e-9;
            if(!fvc && !fev1) return "";
            return "یک مانور فعال خارج از انتخاب نهایی مقدار "+(fvc?"FVC":"")+(fvc&&fev1?" و ":"")+(fev1?"FEV1":"")+" بالاتری دارد؛ BEST VALUES فقط از مانورهای انتخاب نهایی محاسبه شده است.";
        }
        private Dictionary<string,object> BuildDocument()
        {
            Dictionary<string,object> d=new Dictionary<string,object>();
            d["schemaVersion"]=2;
            d["patient"]=new { nationalId=Data.Patient.NationalId,firstName=Data.Patient.FirstName,lastName=Data.Patient.LastName,age=Data.Patient.Age,sex=Data.Patient.Sex.ToString(),origin=Data.Patient.Origin.ToString(),heightCm=Data.Patient.HeightCm,weightKg=Data.Patient.WeightKg };
            d["patientContext"]=new {fullName=Data.Patient.FullName,serverFullName=Data.Patient.ServerFullName,birthDate=Data.Patient.BirthDate.HasValue?Data.Patient.BirthDate.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):null,referenceAgeYears=Data.Patient.ReferenceAgeYears,demographicSource=Data.Patient.DemographicSource,anthropometrySource=Data.Patient.AnthropometrySource,anthropometryConfirmed=Data.Patient.AnthropometryConfirmed};
            d["hlineDestination"]=new {personId=Data.Patient.HLinePersonId,medicalRecordId=Data.Patient.HLineRecordId,recordDate=Data.Patient.HLineRecordDate,recordStatus=Data.Patient.HLineRecordStatus,writeContract="live_HLine_spirometry_write_enabled;successful_production_clinical_write_not_independently_validated"};
            d["selectionRecommendation"]=new {method="quality_screen_tiers_then_FVC_plus_FEV1_stable_acquisition_order",scientificSource="ATS_ERS_2019",verification="software_rules_tested_clinical_and_MIR_equivalence_unverified",deviceRanking="not_demonstrated_by_observed_USB_protocol"};
            d["examination"]=new { id=Data.ExaminationId,snapshotId=SnapshotId,type=Data.SessionType,testedAt=TestedAt.ToString("o",CultureInfo.InvariantCulture),examinationDate=ExaminationDates.Format(TestedAt),dateSource=Data.ExaminationDateSource,captureStartedAt=CaptureStartedAt.ToString("o",CultureInfo.InvariantCulture),operatorName=Data.OperatorName,operatorStatus=string.IsNullOrWhiteSpace(Data.OperatorName)?"unavailable":"manually_entered",centerName=Data.CenterName,maneuverCount=Data.Trials.Count,activeManeuverCount=Data.Trials.Count,selectedManeuverCount=Data.ReportManeuverIds.Count,includedManeuverCount=Summary.Candidates.Count,bestManeuverId=Summary.BestTrial==null?null:Summary.BestTrial.ManeuverId,bestManeuverIndex=BestIndex };
            d["device"]=new { model="MIR Spirolab (protocol family; model not verified)",serial=Summary.BestTrial==null?null:Summary.BestTrial.DeviceSerial,hardwareValidation="HARDWARE VALIDATION PENDING" };
            d["review"]=new { finalizedAt=Data.FinalizedAt.HasValue?Data.FinalizedAt.Value.ToString("o",CultureInfo.InvariantCulture):null,manualVext=Data.ReviewedVextMl.HasValue?(object)new { reviewer=Data.OperatorName,source=Data.ReviewedVextSource,reviewed=Data.VextManuallyReviewed,selectionMethod="explicit_operator_entry_no_automatic_EVol_selection" }:null };
            d["primaryManeuverSelection"]=new { status=Data.SelectionWorkflowVersion>=4?"not_applicable_workflow4_selected_three":Data.SelectionWorkflowVersion>=3?"not_applicable_workflow3_best_values_aggregate":OperatorSelectedIndex>0?"legacy_recorded":"unavailable_legacy_or_unselected",operatorSelectedManeuverId=string.IsNullOrEmpty(Data.OperatorSelectedManeuverId)?null:Data.OperatorSelectedManeuverId,originalSequence=OperatorSelectedIndex>0?(int?)OperatorSelectedIndex:null,method=Data.SelectionMethod,selectedAt=Data.SelectionTimestamp.HasValue?Data.SelectionTimestamp.Value.ToString("o",CultureInfo.InvariantCulture):null,operatorName=Data.SelectionOperatorName,reason=Data.SelectionReason,automaticRecommendationId=Summary.BestTrial==null?null:Summary.BestTrial.ManeuverId,automaticRecommendationSequence=Summary.BestTrial==null?null:(int?)Index(Summary.BestTrial) };
            SessionBestValues best=Summary.BestValues;
            d["bestValues"]=new { sessionType=Data.SessionType,policy="ATS_ERS_2019_independent_maxima_ratio_from_best_fev1_and_fvc_fef2575_from_largest_fvc_plus_fev1",scope=Data.SelectionWorkflowVersion>=4?"operator_selected_max_three_only_not_all_eligible_maneuvers":"all_eligible_maneuvers_not_report_display_subset",selectionScope=Data.SelectionWorkflowVersion>=4?"selected_three_authoritative":"all_eligible_legacy",selectedManeuverIds=Data.SelectionWorkflowVersion>=4?Data.ReportManeuverIds.ToArray():null,selectionWarning=Data.SelectionWorkflowVersion>=4?SelectionWarning:null,qualityLimited=best==null?true:best.QualityLimited,qualityLimitations=best==null?new string[0]:best.QualityLimitations,fvc=Measurement(Summary.BestValues.Fvc),fev1=Measurement(Summary.BestValues.Fev1),fev1Fvc=Measurement(Summary.BestValues.Fev1Fvc),pef=Measurement(Summary.BestValues.Pef),fef2575=Measurement(Summary.BestValues.Fef2575),fet=Measurement(Summary.BestValues.Fet) };
            Dictionary<string,object> values=new Dictionary<string,object>();
            values["FVC"]=Measurement(Summary.BestValues.Fvc); values["FEV1"]=Measurement(Summary.BestValues.Fev1);
            values["FEV1_FVC"]=Measurement(Summary.BestValues.Fev1Fvc);
            values["FEF25_75"]=Measurement(Summary.BestValues.Fef2575); values["PEF"]=Measurement(Summary.BestValues.Pef);
            values["VEXT"]=Data.ReviewedVextMl.HasValue?Measurement(Data.ReviewedVextMl,"mL",null,"manual_review: "+Data.ReviewedVextSource,"manually_reviewed_not_device_verified","available"):Measurement(null,"mL",null,"not_available","not_validated","unavailable"); d["spirometry"]=values;
            d["bestReportedValues"]=values; // schema2 compatibility: spirometry retains the same aggregate values.
            // Workflow 4: the operator's selected set of at most three maneuvers is the
            // source of BEST VALUES, and changing that selection before finalisation
            // can change the result. Workflow 3 keeps its historical display-only
            // meaning so old records are described exactly as they always were.
            d["spirometryRole"]=Data.SelectionWorkflowVersion>=4
                ?"authoritative_session_best_values_from_operator_selected_max_three_maneuvers;changing_the_selection_before_finalisation_can_change_these_values"
                :"authoritative_session_best_values;report_display_selection_never_changes_these_values";
            VextSelection clinic=ClinicVext.SelectForExam(Data);
            Trial site=Data.SelectionWorkflowVersion<3?Data.OperatorSelectedManeuver:null;
            double? siteEvol=site!=null && site.Result!=null && site.Result.BevAvailable?(double?)(site.Result.Bev*1000.0):null;
            Dictionary<string,object> siteValues=new Dictionary<string,object>();
            if(site!=null)
            {
                ManeuverMeasurementSet sm=site.Measurements;
                siteValues["FVC"]=Measurement(sm.Fvc);siteValues["FEV1"]=Measurement(sm.Fev1);siteValues["FEV1_FVC"]=Measurement(sm.Fev1Fvc);siteValues["PEF"]=Measurement(sm.Pef);siteValues["FEF25_75"]=Measurement(sm.Fef2575);
                if(site.ReviewedVextMl.HasValue && (!site.VextManuallyReviewed || string.IsNullOrWhiteSpace(site.VextReviewer) || string.IsNullOrWhiteSpace(site.ReviewedVextSource) || double.IsNaN(site.ReviewedVextMl.Value) || double.IsInfinity(site.ReviewedVextMl.Value) || site.ReviewedVextMl<0 || site.ReviewedVextMl>12000)) throw new ArgumentException("بازبینی VEXT مانور معتبر نیست.");
                siteValues["VEXT"]=new { value=clinic.Available?(double?)clinic.ValueMl:null,unit="mL",predicted=(double?)null,percentPredicted=(double?)null,valueSource=clinic.Available?"clinic_rule_max_evol_below_200ml:maneuver:"+clinic.ManeuverId:"not_available_no_eligible_real_maneuver",verificationStatus=clinic.Available?"calculated_from_reconstructed_real_samples_not_paired_MIR_verified":"not_validated",missingDataStatus=clinic.Available?"available":"unavailable",sourceManeuverId=clinic.Available?clinic.ManeuverId:null,sourceAcquisitionSequence=clinic.Available?(int?)clinic.AcquisitionSequence:null,selectionRule="clinic_max_0_lt_EVol_lt_200ml_across_real_maneuvers",selectedManeuverId=site.ManeuverId,selectedManeuverEvolMl=siteEvol };
            }
            d["siteExport"]=new { status=Data.SelectionWorkflowVersion>=3?"retired_single_site_maneuver_use_bestValues":"legacy_single_maneuver_export",maneuverId=site==null?null:site.ManeuverId,acquisitionSequence=site==null?(int?)null:site.AcquisitionSequence,selectionMethod=Data.SelectionMethod,selectedAt=Data.SelectionTimestamp.HasValue?Data.SelectionTimestamp.Value.ToString("o",CultureInfo.InvariantCulture):null,selectedBy=Data.SelectionOperatorName,measurements=siteValues,transport="local_only_no_upload",vextReviewer=site==null?null:site.VextReviewer,vextSource=site==null?null:site.ReviewedVextSource };
            d["vextSelection"]=new { rule=Data.SelectionWorkflowVersion>=4?"max(0<EVol<200 mL) across the selected final maneuvers (max three)":"max(0<EVol<200 mL) across real maneuvers of this examination",scope=Data.SelectionWorkflowVersion>=4?"selected_max_three_real_usb_live_maneuvers_only":"active_real_usb_live_maneuvers_only",qualityGate="none_ats_ers_assessment_is_separate",valueMl=clinic.Available?(double?)clinic.ValueMl:null,sourceManeuverId=clinic.Available?clinic.ManeuverId:null,sourceAcquisitionSequence=clinic.Available?(int?)clinic.AcquisitionSequence:null,selectedManeuverId=site==null?null:site.ManeuverId,selectedManeuverEvolMl=siteEvol,evolSource="device_reported_field_not_present_in_observed_protocol;calculated_volume_at_peak_tangent_time_zero",calculationProvenance="reconstructed_expiration_origin_not_device_time_zero",verification="not_paired_MIR_verified",problem=clinic.Available?null:clinic.Problem };
            d["reportSelection"]=new { maneuverIds=Data.ReportManeuverIds,method=Data.ReportSelectionManual?"manual":"automatic",scope=Data.SelectionWorkflowVersion>=4?"authoritative_clinical_set_max_three":"display_only_legacy",selectedAt=Data.ReportSelectionTimestamp.HasValue?Data.ReportSelectionTimestamp.Value.ToString("o",CultureInfo.InvariantCulture):null,selectedBy=Data.ReportSelectionOperator,selectionWarning=Data.SelectionWorkflowVersion>=4?SelectionWarning:null,workflowVersion=Data.SelectionWorkflowVersion };
            List<object> maneuvers=new List<object>();
            foreach(Trial t in Data.Trials)
            {
                Dictionary<string,object> metrics=new Dictionary<string,object>();
                ManeuverAssessment assessment=ManeuverAssessment.Assess(t);
                ManeuverMeasurementSet mm=t.Measurements;
                for(int i=0;i<9;i++)
                {
                    string jsonName=Ui.SpiroReport.MetricName(i),metricName=MetricNameForIndex(i);
                    metrics[jsonName]=Measurement(mm.Get(metricName));
                }
                object device=null;
                if(t.DeviceValues!=null) device=new { present=t.DeviceValues.Present,blockValid=t.DeviceValues.BlockValid,associated=t.DeviceValues.Associated,association=t.DeviceValues.Association,captureSequence=t.DeviceValues.CaptureSequence,readDelaySeconds=double.IsNaN(t.DeviceValues.ReadDelaySeconds)||double.IsInfinity(t.DeviceValues.ReadDelaySeconds)?(double?)null:t.DeviceValues.ReadDelaySeconds,invalidReason=t.DeviceValues.InvalidReason,fvc=t.DeviceValues.FvcValid?(double?)t.DeviceValues.Fvc:null,fev1=t.DeviceValues.Fev1Valid?(double?)t.DeviceValues.Fev1:null,pef=t.DeviceValues.PefValid?(double?)t.DeviceValues.Pef:null,fet=t.DeviceValues.FetValid?(double?)t.DeviceValues.Fet:null,qualityFieldUnverified=t.DeviceValues.Quality };
                Dictionary<string,object> reconstruction=null;
                if(mm.Reconstructed!=null)
                {
                    reconstruction=new Dictionary<string,object>();
                    for(int i=0;i<9;i++) reconstruction[Ui.SpiroReport.MetricName(i)]=Measurement(mm.Reconstructed.Get(MetricNameForIndex(i)));
                }
                maneuvers.Add(new { id=t.ManeuverId,index=Index(t),acquisitionSequence=t.AcquisitionSequence,testedAt=t.When.ToString("o",CultureInfo.InvariantCulture),completed=t.Completed,captureAssessment=assessment,operatorStopped=t.OperatorStopped,captureError=t.CaptureError,signalUsable=t.Result.Valid,status=t.OperatorRejected?"operator_rejected":!t.Completed?"incomplete":!t.Result.Valid?"invalid":"complete_signal_usable",includedInBestReportedValues=IsBestSource(t.ManeuverId),eligibleForSelection=ExamSummary.CanReport(t),source=t.Simulated?"simulation":t.Imported?"replay":"usb_live",endReason=t.EndReason,quality=new { invalidReason=t.Result.InvalidReason,warnings=t.Result.Warnings,assessmentProblem=assessment.Problem,clinicalAcceptance="not_automatically_validated" },rejection=new { rejected=t.OperatorRejected,operatorName=t.RejectionOperator,reason=t.RejectionReason,reviewedAt=t.RejectionTimestamp.HasValue?t.RejectionTimestamp.Value.ToString("o",CultureInfo.InvariantCulture):null },rawSampleCount=t.Flow.Count,rawPacketCount=t.Packets.Count,rawSampleStorage="local_examination_record",verificationStatus=t.DeviceValues!=null&&t.DeviceValues.Present?"device_result_capture_bound_unverified_against_reference_software":"unverified",measurements=metrics,softwareReconstruction=reconstruction,deviceResult=device,vextReview=t.ReviewedVextMl.HasValue?new { valueMl=t.ReviewedVextMl.Value,reviewer=t.VextReviewer,source=t.ReviewedVextSource,reviewed=t.VextManuallyReviewed,role="legacy_local_review_not_the_HLine_vext_field" }:null,estimatedBEV=t.Result.Valid && t.Result.BevAvailable?Measurement(t.Result.Bev*1000,"mL",null,"volume_at_peak_tangent_time_zero","not_equivalent_to_validated_VEXT","available"):null,backExtrapolation=new { timeZeroSeconds=t.Result.BevAvailable?(double?)t.Result.TimeZero:null,method="peak_tangent_time_zero_then_interpolated_volume",sourceAdequacy=t.Result.BevSourceAdequacy,timedVolumeOrigin="reconstructed_segment_origin_not_device_time_zero",evolDeviceParity="not_validated" } });
            }
            d["maneuvers"]=maneuvers;
            // Workflow 4 does not record a diagnosis in this application: the physician
            // states the final interpretation in HLine, so no automatic wording is
            // published as a recorded finding. Older workflow 3 records keep the exact
            // historical shape and meaning they always had.
            if(Data.SelectionWorkflowVersion>=4)
            {
                bool clinicianText=!string.IsNullOrWhiteSpace(Data.ReviewedInterpretation);
                d["interpretation"]=clinicianText
                    ?(object)new { text=Data.ReviewedInterpretation,valueSource="clinician_entered",verificationStatus="manual_entry_not_independently_verified",recordedInThisApplication=true,responsibility="clinician_entered_locally",reportStatement=Data.ReviewedInterpretation }
                    :new { text=(string)null,valueSource="not_recorded_in_this_application",verificationStatus="not_recorded_no_automatic_diagnosis",recordedInThisApplication=false,responsibility="physician_in_HLine",reportStatement=Ui.SpiroReport.PendingInterpretation };
            }
            else
                d["interpretation"]=new { text=string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?Summary.Interpretation:Data.ReviewedInterpretation,valueSource=string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?"unvalidated_algorithm":"clinician_entered",verificationStatus=string.IsNullOrWhiteSpace(Data.ReviewedInterpretation)?"not_validated":"manual_entry_not_independently_verified" };
            d["quality"]=new { grade=string.IsNullOrWhiteSpace(Data.ReviewedQualityGrade)?null:Data.ReviewedQualityGrade,comment=Data.ReviewedQualityComment,valueSource=string.IsNullOrWhiteSpace(Data.ReviewedQualityGrade)?"unavailable":"clinician_entered",verificationStatus="not_automatically_validated",repeatability=Summary.Repeatability };
            List<object> displayed=new List<object>(); foreach(Trial t in Summary.Trials) displayed.Add(new { id=t.ManeuverId,index=Index(t) });
            d["report"]=new { file="Spirometry-Report.jpg",format="image/jpeg",application="Independent SpiroLab 5.4.6",reference="ERS/ECCS 1993",displayedManeuvers=displayed,displayPolicy=Data.SelectionWorkflowVersion>=4?(Data.ReportSelectionManual?"manual_selected_three_authoritative":"automatic_selected_three_authoritative"):Data.SelectionWorkflowVersion>=3?(Data.ReportSelectionManual?"manual_report_ids_do_not_change_best_values":"automatic_top_three_do_not_change_best_values"):"legacy_report_selection",operatorSelectedManeuverId=string.IsNullOrEmpty(Data.OperatorSelectedManeuverId)?null:Data.OperatorSelectedManeuverId,primaryManeuverLabel=PrimaryLabel,bestValues=Measurement(Summary.BestValues.Fvc),bestValueSourceManeuvers=SourceIds(Summary.BestValues),aggregateScope=Data.SelectionWorkflowVersion>=4?"selected_max_three_maneuvers_only":"all_eligible_maneuvers_in_"+Data.SessionType,selectionWarning=Data.SelectionWorkflowVersion>=4?SelectionWarning:null,predictedCurve="not_generated_no_validated_method",vextDisplay=Data.ReviewedVextMl.HasValue?"manual_review":"unavailable",selectionPolicy="eligible_complete_nonrejected;separate_FVC_and_FEV1_quality_tiers;FVC_FEV1_PEF_independent_max;FEF25_75_from_largest_FVC_plus_FEV1;ratio_max_FEV1_div_max_FVC",selectionVerification="reference_supported;device_results_used_when_capture_bound;exact_MIR_firmware_behavior_pending_paired_validation" };
            d["provenance"]=new { source=Source,status=Status,measurementVerification="unverified",clinicalValidation="pending",hardwareValidation="HARDWARE VALIDATION PENDING",legacyRecord=Data.LegacyRecord,btpsCorrection="not_verified",selectionScope=Data.SelectionWorkflowVersion>=4?"selected_three_only":"all_eligible_legacy",vextReason=Data.SelectionWorkflowVersion>=4?"HLine VEXT follows the clinic convention max(0<EVol<200 mL) over the selected final maneuvers (max three); EVol timing is reconstructed and not paired-MIR verified":"HLine VEXT follows the clinic convention max(0<EVol<200 mL) over active real maneuvers; EVol timing is reconstructed and not paired-MIR verified" };
            return d;
        }
    }
}
