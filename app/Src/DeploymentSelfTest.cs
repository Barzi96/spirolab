using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using SpiroLab.Device;

namespace SpiroLab
{
    public static class DeploymentSelfTest
    {
        public static void Run(string directory)
        {
            string root=Path.GetFullPath(directory); Directory.CreateDirectory(root);
            Exam e=Exam.CreateNew(new AppSettings()); e.OperatorName="کارشناس آزمایشی"; e.Patient=new Patient { FirstName="داده",LastName="آزمایشی",NationalId="TEST-0001",Age=35,HeightCm=175,WeightKg=75,Origin=Origin.Caucasian };
            for(int i=0;i<3;i++) { Trial t=Simulator.Build(e.Patient,100+i); t.Result=SpiroAnalysis.Analyze(t,e.Patient); e.Trials.Add(t); }
            e.SelectPrimary(e.Trials[2].ManeuverId,"manual",e.OperatorName,"Explicit development selection; aggregate values remain independent.");
            e.FinalizeExamination();
            string persisted=Path.Combine(root,"selftest.spiro"); ExamStore.Save(persisted,e); e=ExamStore.Load(persisted);
            ExaminationSnapshot snapshot=ExaminationSnapshot.Capture(e);
            ResultPackage output=ResultExport.SaveToDirectory(root,snapshot);
            var json=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(output.DataPath)) as System.Collections.Generic.Dictionary<string,object>;
            if(json==null || (int)json["schemaVersion"]!=2) throw new InvalidDataException("self-test JSON failed");
            File.WriteAllText(Path.Combine(root,"selftest-result.txt"),"PASS application rendering, JPG/JSON, persistence on THIS host.\r\nNON-CLINICAL TEST DATA\r\nHost: "+Environment.OSVersion+"\r\nClean Windows acceptance: NOT PROVEN by this test\r\nHARDWARE VALIDATION PENDING\r\n"+output.ReportPath,new UTF8Encoding(true));
        }
    }
}
