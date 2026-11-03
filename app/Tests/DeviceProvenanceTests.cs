using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using SpiroLab;
using SpiroLab.Device;
using SpiroLab.HLine;
using SpiroLab.Ui;

class DeviceProvenanceTests
{
    static int checks;
    static string root;
    static void Check(bool ok,string name) {if(!ok) throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static object Field(object o,string name) {return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
    static Exam Data(ExaminationSnapshot s) {return (Exam)typeof(ExaminationSnapshot).GetField("Data",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(s);}
    static ExamSummary Summary(ExaminationSnapshot s) {return (ExamSummary)typeof(ExaminationSnapshot).GetField("Summary",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(s);}
    static DeviceProvenanceResult Validate(ExaminationSnapshot s) {return DeviceProvenanceValidator.Validate(s);}

    static Patient Patient()
    {
        return new Patient {FirstName="مراجع",LastName="ساختگی",NationalId="0000000001",Origin=Origin.Caucasian,HeightCm=170,WeightKg=70,Age=44,HLinePersonId="P-1",HLineRecordId="R-1",HLineRecordDate="2026/11/01",HLineRecordStatus="complete",AnthropometryConfirmed=true};
    }
    static Exam Base()
    {
        Exam e=Exam.CreateNew(new AppSettings {OperatorName="آزمون ساختگی منشأ"});
        e.Patient=Patient(); e.ExaminationDate=new DateTime(2026,10,19); return e;
    }
    static double SafeFvc(Trial t) {return t.Result!=null && t.Result.Fvc>=0.5 && t.Result.Fvc<=10.0?t.Result.Fvc:4.5;}
    static double SafeFev1(Trial t,double fvc) {return t.Result!=null && t.Result.Fev1>=0.5 && t.Result.Fev1<=fvc?t.Result.Fev1:fvc*0.75;}
    static double SafePef(Trial t) {return t.Result!=null && t.Result.Pef>0 && t.Result.Pef<=20?t.Result.Pef:8.0;}
    static double SafeFet(Trial t) {return t.Result!=null && t.Result.Fet>0 && t.Result.Fet<=30?t.Result.Fet:6.0;}
    static Exam ValidExam(int count)
    {
        Exam e=Base();
        for(int i=0;i<count;i++)
        {
            Trial t=Simulator.Build(e.Patient,200+i);
            t.Result=SpiroAnalysis.Analyze(t,e.Patient);
            double fvc=SafeFvc(t),fev1=SafeFev1(t,fvc);
            SyntheticDeviceEvidence.Attach(t,fvc,fev1,SafePef(t),SafeFet(t),SyntheticDeviceEvidence.FixtureSerial,i+1);
            t.Result.BevAvailable=true;t.Result.Bev=0.100+0.005*i;
            e.AddManeuver(t);
        }
        return e;
    }
    static bool EntryAccepted(Trial t)
    {
        MethodInfo method=typeof(MainForm).GetMethod("DeviceCoreAccepted",BindingFlags.Static|BindingFlags.NonPublic);
        if(method==null) throw new MissingMethodException("MainForm.DeviceCoreAccepted");
        return (bool)method.Invoke(null,new object[] {t});
    }
    static Exam NoSerialExam()
    {
        Exam e=ValidExam(3);SelectFirstThree(e);
        foreach(Trial t in e.Trials) {t.DeviceSerial="";t.DeviceValues.SerialNumber="";}
        return e;
    }
    static Exam SelectedThirdWithoutDevice()
    {
        Exam e=Base();
        Trial first=Simulator.Build(e.Patient,201);first.Simulated=false;first.Imported=false;
        first.Result=SpiroAnalysis.Analyze(first,e.Patient);
        first.Result.BevAvailable=first.Result.BevOk=true;first.Result.Bev=0.100;
        SyntheticDeviceEvidence.Attach(first,5.20,4.10,12.00,8.00,SyntheticDeviceEvidence.FixtureSerial,1);
        e.AddManeuver(first);
        Trial second=Simulator.Build(e.Patient,202);second.Simulated=false;second.Imported=false;
        second.Result=SpiroAnalysis.Analyze(second,e.Patient);
        second.Result.BevAvailable=second.Result.BevOk=true;second.Result.Bev=0.105;
        SyntheticDeviceEvidence.Attach(second,4.90,4.20,11.00,8.10,SyntheticDeviceEvidence.FixtureSerial,2);
        e.AddManeuver(second);
        Trial third=Simulator.Build(e.Patient,300);third.Simulated=false;third.Imported=false;
        third.DeviceSerial="";third.DeviceCaptureSequence=0;third.DeviceValues=null;
        third.Result=SpiroAnalysis.Analyze(third,e.Patient);third.Result.BevAvailable=false;third.Result.Cough=false;
        e.AddManeuver(third);
        SelectFirstThree(e);
        return e;
    }
    static void SerialPolicy()
    {
        Exam valid=ValidExam(3);SelectFirstThree(valid);
        Check(EntryAccepted(valid.Trials[0]) && Validate(ExaminationSnapshot.Capture(valid)).Allowed,"S-01 matching device serial is accepted on capture and submission");

        Exam mismatch=ValidExam(3);SelectFirstThree(mismatch);
        ExaminationSnapshot mismatchSnap=ExaminationSnapshot.Capture(mismatch);
        foreach(Trial t in mismatch.Trials) t.DeviceValues.SerialNumber="OTHER";
        MutateAll(mismatchSnap,delegate(DeviceResults d){d.SerialNumber="OTHER";});
        Check(!EntryAccepted(mismatch.Trials[0]),"S-02 capture rejects a mismatched device serial");
        Reject("S-02 submission rejects a mismatched device serial",mismatchSnap,DeviceProvenanceCode.InvalidManeuverAssociation);

        Exam noSerial=NoSerialExam();
        Check(EntryAccepted(noSerial.Trials[0]),"S-03 capture accepts a capture-bound block when SDA reported no serial");
        Check(Validate(ExaminationSnapshot.Capture(noSerial)).Allowed,"S-03 submission accepts the same no-serial session");
    }
    static void SelectedManeuverPolicy()
    {
        Exam e=SelectedThirdWithoutDevice();
        Trial third=e.Trials[2];
        Check(ExamSummary.CanReport(third),"P-01 the fixture third maneuver passes the unchanged reportability screen");
        ExaminationSnapshot s=ExaminationSnapshot.Capture(e);
        bool isBestSource=false;foreach(string id in Summary(s).BestValues.SourceManeuverIds) if(id==third.ManeuverId) isBestSource=true;
        Check(!isBestSource,"P-01 the fixture third maneuver supplies no BEST VALUE");
        Check(ClinicVext.SelectForExam(Data(s)).ManeuverId!=third.ManeuverId,"P-01 the fixture third maneuver is not the VEXT source");
        DeviceProvenanceResult result=Validate(s);
        bool cited=false;foreach(DeviceProvenanceIssue issue in result.Issues) if(issue.ManeuverId==third.ManeuverId) cited=true;
        Check(!result.Allowed && cited,"P-01 a selected maneuver without a valid device result is rejected and identified");

        Exam inconsistent=SelectedThirdWithoutDevice();
        Trial rawThird=inconsistent.Trials[2];
        SyntheticDeviceEvidence.Attach(rawThird,rawThird.Result.Fvc,rawThird.Result.Fev1,rawThird.Result.Pef,rawThird.Result.Fet,SyntheticDeviceEvidence.FixtureSerial,3);
        ExaminationSnapshot rawSnap=ExaminationSnapshot.Capture(inconsistent);
        bool rawPresent=false;foreach(Trial t in Data(rawSnap).Trials) if(t.ManeuverId==rawThird.ManeuverId) {t.DeviceValues.Raw[0x14]^=0xFF;rawPresent=true;}
        DeviceProvenanceResult rawResult=Validate(rawSnap);
        bool rawCited=false;foreach(DeviceProvenanceIssue issue in rawResult.Issues) if(issue.ManeuverId==rawThird.ManeuverId) rawCited=true;
        Check(rawPresent && !rawResult.Allowed && rawCited && rawResult.Code==DeviceProvenanceCode.RawBlockMismatch,"P-01 a selected maneuver with an inconsistent result block is rejected");

        Exam unselected=ValidExam(3);SelectFirstThree(unselected);
        Trial extra=Simulator.Build(unselected.Patient,300);extra.Simulated=false;extra.Imported=false;
        extra.DeviceSerial="";extra.DeviceCaptureSequence=0;extra.DeviceValues=null;
        extra.Result=SpiroAnalysis.Analyze(extra,unselected.Patient);extra.Result.BevAvailable=false;extra.Result.Cough=false;
        unselected.AddManeuver(extra);SelectFirstThree(unselected);
        Check(Validate(ExaminationSnapshot.Capture(unselected)).Allowed,"P-01 an unselected maneuver without device data does not block submission");
    }
    static Exam LegacyExam()
    {
        Exam e=Base(); e.SelectionWorkflowVersion=3;
        for(int i=0;i<3;i++)
        {
            Trial t=Simulator.Build(e.Patient,300+i);t.Simulated=false;t.Imported=false;
            t.Result=SpiroAnalysis.Analyze(t,e.Patient);
            t.Result.BevAvailable=t.Result.BevOk=true;t.Result.Bev=0.110+0.010*i;
            e.AddManeuver(t);
        }
        return e;
    }
    static void SelectFirstThree(Exam e)
    {
        e.ReportManeuverIds.Clear();
        for(int i=0;i<3 && i<e.Trials.Count;i++) e.ReportManeuverIds.Add(e.Trials[i].ManeuverId);
        e.ReportSelectionManual=true;
    }
    static SubmissionDraft Draft(Exam e,string name)
    {
        ExaminationSnapshot s=ExaminationSnapshot.Capture(e);
        return new SubmissionDraft(s,ResultExport.SaveToDirectory(Path.Combine(root,name),s));
    }
    static void Reject(string name,ExaminationSnapshot s,DeviceProvenanceCode? code)
    {
        DeviceProvenanceResult r=Validate(s);
        Check(!r.Allowed,name);
        if(code.HasValue) Check(r.Code==code.Value,name+" (classified)");
        Check(r.OperatorMessage==DeviceProvenanceValidator.OperatorMessage,name+" (operator message)");
    }
    static void MutateAll(ExaminationSnapshot s,Action<DeviceResults> change)
    {
        foreach(Trial t in Data(s).Trials) if(t.DeviceValues!=null) change(t.DeviceValues);
    }

    class CountingGateway : ISubmissionGateway
    {
        public int Uploads,Creates,Reads;
        public RemoteSpirometry Saved;
        public bool ContractVerified {get{return true;}}
        public void VerifyDestination(SubmissionDraft d) {}
        public RemoteSpirometry Read(SubmissionDraft d) {Reads++;return Saved;}
        public string UploadJpeg(SubmissionDraft d,string reference) {Uploads++;return "F-1";}
        public void Create(SubmissionDraft d,string file,string reference) {Creates++;}
        public void VerifySaved(SubmissionDraft d) {}
    }
    sealed class CountingUpdateGateway : CountingGateway, ISubmissionUpdateGateway
    {
        public int Updates;
        public void Update(SubmissionDraft d,string fileId,string reference) {Updates++;}
    }
    static RemoteSpirometry Matching(SubmissionDraft d)
    {
        return new RemoteSpirometry {PersonId=d.PersonId,NationalId=d.NationalId,RecordId=d.RecordId,RecordDate=d.RecordDate,RecordStatus=d.RecordStatus,ExaminationDate=d.ExaminationDate,Fields=d.Fields,FileId="F-1",Jpeg=d.JpegBytes};
    }

    [STAThread] static int Main(string[] args)
    {
        try
        {
            root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);

            // 1. A structurally verified device block is accepted.
            Exam valid=ValidExam(3);ExaminationSnapshot vs=ExaminationSnapshot.Capture(valid);
            DeviceProvenanceResult accepted=Validate(vs);
            if(!accepted.Allowed) Console.WriteLine("DIAGNOSTIC "+accepted.TechnicalSummary);
            Check(accepted.Allowed && accepted.Issues.Count==0,"valid real-device evidence passes the provenance gate");
            Check(accepted.Code==DeviceProvenanceCode.Allowed,"accepted result has explicit allowed code");

            // 2/3/4. Missing evidence, simulator relabelled as real, and a false
            // RequiresDeviceCore flag are all rejected.
            ExaminationSnapshot missing=ExaminationSnapshot.Capture(LegacyExam());
            Reject("missing DeviceValues blocks a new write",missing,DeviceProvenanceCode.MissingDirectDeviceData);
            Exam simFalse=LegacyExam();foreach(Trial t in simFalse.Trials) {t.Simulated=false;t.Imported=false;t.RequiresDeviceCore=false;}
            Reject("Simulated=false alone cannot authenticate a simulator trial",ExaminationSnapshot.Capture(simFalse),DeviceProvenanceCode.MissingDirectDeviceData);
            Exam weakFlag=LegacyExam();foreach(Trial t in weakFlag.Trials) {t.RequiresDeviceCore=false;t.DeviceCaptureSequence=0;}
            Reject("RequiresDeviceCore=false without a block is rejected",ExaminationSnapshot.Capture(weakFlag),DeviceProvenanceCode.MissingDirectDeviceData);

            // 5/6. Association tokens.
            ExaminationSnapshot seq=ExaminationSnapshot.Capture(ValidExam(3));MutateAll(seq,delegate(DeviceResults d){d.CaptureSequence++;});
            Reject("capture sequence mismatch is rejected",seq,DeviceProvenanceCode.InvalidManeuverAssociation);
            ExaminationSnapshot serial=ExaminationSnapshot.Capture(ValidExam(3));MutateAll(serial,delegate(DeviceResults d){d.SerialNumber="OTHER";});
            Reject("serial number mismatch is rejected",serial,DeviceProvenanceCode.InvalidManeuverAssociation);

            // 7/12. Raw block shape and decoded-byte consistency.
            ExaminationSnapshot truncated=ExaminationSnapshot.Capture(ValidExam(3));MutateAll(truncated,delegate(DeviceResults d){d.Raw=new byte[8];});
            Reject("truncated raw device block is rejected",truncated,DeviceProvenanceCode.RawBlockMismatch);
            ExaminationSnapshot rawMismatch=ExaminationSnapshot.Capture(ValidExam(3));MutateAll(rawMismatch,delegate(DeviceResults d){d.Fvc+=0.01;});
            Reject("decoded value inconsistent with raw bytes is rejected",rawMismatch,DeviceProvenanceCode.RawBlockMismatch);

            // 8-11. Invalid direct core values and FET.
            ExaminationSnapshot badFvc=ExaminationSnapshot.Capture(ValidExam(3));MutateAll(badFvc,delegate(DeviceResults d){d.Fvc=99;});
            Reject("invalid direct FVC is rejected",badFvc,null);
            ExaminationSnapshot badFev1=ExaminationSnapshot.Capture(ValidExam(3));MutateAll(badFev1,delegate(DeviceResults d){d.Fev1=99;});
            Reject("invalid direct FEV1 is rejected",badFev1,null);
            ExaminationSnapshot badPef=ExaminationSnapshot.Capture(ValidExam(3));MutateAll(badPef,delegate(DeviceResults d){d.Pef=0;});
            Reject("invalid direct PEF is rejected",badPef,null);
            ExaminationSnapshot badFet=ExaminationSnapshot.Capture(ValidExam(3));MutateAll(badFet,delegate(DeviceResults d){d.Fet=0;});
            Reject("invalid direct FET is rejected",badFet,null);

            // 13. Independent best FVC and best FEV1 from two valid maneuvers.
            Exam two=ValidExam(2);
            Trial a=two.Trials[0],b=two.Trials[1];
            a.Result.Fvc=5.0;a.Result.Fev1=3.0;a.Result.FvcAvailable=a.Result.Fev1Available=true;
            b.Result.Fvc=4.5;b.Result.Fev1=3.5;b.Result.FvcAvailable=b.Result.Fev1Available=true;
            SyntheticDeviceEvidence.Attach(a,5.0,3.0,SafePef(a),SafeFet(a),SyntheticDeviceEvidence.FixtureSerial,1);
            SyntheticDeviceEvidence.Attach(b,4.5,3.5,SafePef(b),SafeFet(b),SyntheticDeviceEvidence.FixtureSerial,2);
            two.ReportManeuverIds.Clear();two.ReportManeuverIds.Add(a.ManeuverId);two.ReportManeuverIds.Add(b.ManeuverId);two.ReportSelectionManual=true;
            ExaminationSnapshot twoSnap=ExaminationSnapshot.Capture(two);
            Check(Validate(twoSnap).Allowed,"best FVC and best FEV1 may come from two different valid maneuvers");
            Check(Summary(twoSnap).BestValues.Fvc.SourceManeuverId!=Summary(twoSnap).BestValues.Fev1.SourceManeuverId,"different valid source maneuvers are recorded independently");

            // 14/15. Software primary rejected; curve-derived FEF25-75 allowed.
            Reject("SoftwareCalculated primary values are rejected",missing,DeviceProvenanceCode.MissingDirectDeviceData);
            Check(Summary(vs).BestValues.Fef2575.Source==MeasurementSource.SoftwareCalculated && Validate(vs).Allowed,"FEF25-75 may remain a software calculation from the valid curve");

            // 16/17. Workflow 4 draws VEXT only from the selected three. A higher
            // fourth EVol, valid or invalid, can neither raise VEXT nor block a
            // submission whose selected three are complete.
            Exam four=ValidExam(4);SelectFirstThree(four);Trial outside=four.Trials[3];outside.Result.Bev=0.180;
            ExaminationSnapshot fourSnap=ExaminationSnapshot.Capture(four);
            VextSelection selectedVext=ClinicVext.SelectForExam(Data(fourSnap));
            Check(Data(fourSnap).ReportManeuverIds.Count==3 && selectedVext.Available && selectedVext.ValueMl==110 && selectedVext.ManeuverId!=outside.ManeuverId,"workflow-4 VEXT ignores a higher fourth EVol outside the selected three");
            Check(Validate(fourSnap).Allowed,"a valid unselected fourth maneuver does not affect the selected-three gate");

            Exam badVext=ValidExam(4);SelectFirstThree(badVext);Trial badOutside=badVext.Trials[3];badOutside.Result.Bev=0.180;badOutside.DeviceValues=null;
            ExaminationSnapshot badVextSnap=ExaminationSnapshot.Capture(badVext);
            Check(ClinicVext.SelectForExam(Data(badVextSnap)).ManeuverId!=badOutside.ManeuverId,"an invalid unselected fourth maneuver is never the VEXT source");
            Check(Validate(badVextSnap).Allowed,"an unselected maneuver without device data cannot block an otherwise valid three-maneuver submission");

            // 18-20. The gate runs before any upload/POST and before the journal
            // is durably marked as write-attempted.
            SubmissionDraft legacyDraft=Draft(LegacyExam(),"legacy-gate");
            CountingGateway counting=new CountingGateway();SubmissionJournal gateJournal=new SubmissionJournal();
            SubmissionCoordinator gateCoordinator=new SubmissionCoordinator(counting,gateJournal,delegate {},null);
            bool blocked=false;try {gateCoordinator.Submit(legacyDraft);} catch(DeviceProvenanceException) {blocked=true;}
            Check(blocked,"invalid provenance raises the dedicated operator-facing exception");
            Check(counting.Uploads==0 && counting.Creates==0 && counting.Reads==0,"invalid provenance performs no upload, POST or read request");
            Check(!gateJournal.WriteAttempted && gateJournal.State==SubmissionState.Draft,"invalid provenance never marks the journal write-attempted");

            // The explicitly authorized PATCH path is gated before its own write.
            SubmissionDraft updateDraft=Draft(LegacyExam(),"legacy-patch-gate");
            CountingUpdateGateway updateGateway=new CountingUpdateGateway();
            MedicalRecord updateRecord=new MedicalRecord {Id=updateDraft.RecordId,PersonId=updateDraft.PersonId,NationalId=updateDraft.NationalId,DateText=updateDraft.RecordDate,Status=updateDraft.RecordStatus};
            SubmissionJournal updateJournal=new SubmissionJournal();
            SubmissionCoordinator updateCoordinator=new SubmissionCoordinator(updateGateway,updateJournal,delegate {},updateRecord);
            bool updateBlocked=false;try {updateCoordinator.SubmitUpdate(updateDraft);} catch(DeviceProvenanceException) {updateBlocked=true;}
            Check(updateBlocked,"invalid provenance blocks the explicit PATCH path");
            Check(updateGateway.Uploads==0 && updateGateway.Updates==0 && !updateJournal.WriteAttempted,"blocked PATCH performs no upload, PATCH or write-attempted journal change");

            // 21/22. A previous successful or uncertain write stays readable and
            // is never resent merely because provenance cannot be re-proven.
            SubmissionDraft previous=Draft(LegacyExam(),"legacy-readback");
            CountingGateway readGateway=new CountingGateway();readGateway.Saved=Matching(previous);
            SubmissionJournal readJournal=new SubmissionJournal {WriteAttempted=true,FileId="F-1"};
            SubmissionCoordinator readCoordinator=new SubmissionCoordinator(readGateway,readJournal,delegate {},null);
            readCoordinator.Submit(previous);
            Check(readCoordinator.State==SubmissionState.Confirmed && readCoordinator.AlreadyPresent,"a previously committed result is read back without a new write");
            Check(readGateway.Uploads==0 && readGateway.Creates==0,"successful read-back performs no upload or POST");
            Check(readCoordinator.Recheck(previous) && readGateway.Uploads==0 && readGateway.Creates==0,"Recheck remains read-only in an uncertain/committed state");

            // 23. Historical v3 records still open and render; the gate does not
            // delete or rewrite them.
            string legacyPath=Path.Combine(root,"legacy-provenance.spiro");Exam legacy=LegacyExam();ExamStore.Save(legacyPath,legacy);
            Exam reopened=ExamStore.Load(legacyPath);ExaminationSnapshot reopenedSnap=ExaminationSnapshot.Capture(reopened);
            Check(reopened.Trials.Count==3 && reopenedSnap.Status.Length>0,"historical v3 examination still opens with all trials");
            using(Bitmap historical=SpiroReport.RenderForJpeg(reopenedSnap)) Check(historical.Width==1800 && historical.Height==2546,"historical report still renders at 1800x2546");
            Check(!Validate(reopenedSnap).Allowed,"historical software-only record is not silently allowed as a new write");

            // 24. The gate is read-only: selection and BEST VALUES are unchanged.
            Exam immutable=ValidExam(3);ExaminationSnapshot before=ExaminationSnapshot.Capture(immutable);
            double fvc=Summary(before).BestValues.Fvc.Value.Value,fev1=Summary(before).BestValues.Fev1.Value.Value,pef=Summary(before).BestValues.Pef.Value.Value;
            string[] ids=Data(before).ReportManeuverIds.ToArray();
            Validate(before);
            Check(Math.Abs(Summary(before).BestValues.Fvc.Value.Value-fvc)<1e-12 && Math.Abs(Summary(before).BestValues.Fev1.Value.Value-fev1)<1e-12 && Math.Abs(Summary(before).BestValues.Pef.Value.Value-pef)<1e-12,"provenance validation never changes BEST VALUES");
            Check(Data(before).ReportManeuverIds.Count==ids.Length && Data(before).ReportManeuverIds[0]==ids[0],"provenance validation never changes the selected three");

            // 25. Snapshot ownership: mutating the source examination after
            // Capture cannot change the frozen device evidence.
            Exam source=ValidExam(3);DeviceResults sourceDevice=source.Trials[0].DeviceValues;ExaminationSnapshot frozen=ExaminationSnapshot.Capture(source);
            string jsonBefore=frozen.ToJson();sourceDevice.Fvc=1.0;sourceDevice.Raw[0x10]=0;sourceDevice.Raw[0x11]=1;
            Check(DeviceProvenanceValidator.Validate(frozen).Allowed,"snapshot keeps device values and raw bytes after the source is mutated");
            Check(frozen.ToJson()==jsonBefore,"snapshot JSON is unchanged after source device mutation");
            Trial clone=source.Trials[0].Clone();clone.DeviceValues.Raw[0x10]=9;clone.DeviceValues.Fvc=2.0;
            Check(Math.Abs(source.Trials[0].DeviceValues.Fvc-1.0)<1e-12 && source.Trials[0].DeviceValues.Raw[0x10]!=9,"Trial.Clone deep-copies the device block and raw bytes");

            // 26. Error text is the prescribed operator-facing text.
            DeviceProvenanceResult rejected=Validate(missing);
            Check(rejected.OperatorMessage=="منشأ مقادیر اصلی این معاینه از دستگاه تأیید نشد. نتیجه به سامانه ارسال نشد. لطفاً دریافت دستگاه یا پرونده معاینه را بررسی کنید.","operator message is the prescribed Persian text");

            SelectedManeuverPolicy();
            SerialPolicy();
            Console.WriteLine("ALL "+checks+" CHECKS PASSED");return 0;
        }
        catch(Exception ex) {Console.WriteLine("FAIL "+ex);return 1;}
    }
}
