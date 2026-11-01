using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using SpiroLab.Ui;

namespace SpiroLab
{
    // Local output only. No network transport or website adapter in this phase.
    public sealed class ResultPackage
    {
        public string ReportPath;
        [Obsolete("There is only one primary report image; retained for source compatibility.")]
        public string CurvesPath;
        public string DataPath;
        internal string SourceSnapshotId;
        internal byte[] ReportBytes;
    }
    public static class ResultExport
    {
        public static int JpegQuality=97;
        internal static Action<string> ExportCheckpoint = null;
        public static void EncodeJpeg(Bitmap image,string path,int quality)
        {
            if(quality<1 || quality>100) throw new ArgumentOutOfRangeException("quality");
            ImageCodecInfo encoder=null;
            foreach(ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders()) if(codec.MimeType=="image/jpeg") encoder=codec;
            if(encoder==null) throw new InvalidOperationException("JPEG encoder unavailable");
            using(EncoderParameters options=new EncoderParameters(1))
            { options.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,(long)quality); image.Save(path,encoder,options); }
        }
        public static ResultPackage SaveToDirectory(string parent,ExaminationSnapshot snapshot)
        {
            snapshot.Data.ValidateSelection(snapshot.Data.PrimarySelectionRequired);
            snapshot.Data.ValidateReportSelection(true);
            string root=Path.GetFullPath(parent); Directory.CreateDirectory(root);
            string id=snapshot.SnapshotId;
            string stage=Path.Combine(root,".spiro-tmp-"+id), final=Path.Combine(root,"Examination-"+id);
            if(Directory.Exists(final) || Directory.Exists(stage)) throw new IOException("مسیر خروجی از قبل وجود دارد؛ پروندهٔ قبلی حفظ شد.");
            Directory.CreateDirectory(stage);
            try
            {
                string jpg=Path.Combine(stage,"Spirometry-Report.jpg"), json=Path.Combine(stage,"Spirometry-Data.json");
                using(Bitmap report=SpiroReport.Render(snapshot)) EncodeJpeg(report,jpg,JpegQuality);
                if(ExportCheckpoint!=null) ExportCheckpoint("after-jpg");
                File.WriteAllText(json,snapshot.ToJson(),new UTF8Encoding(false));
                using(Image check=Image.FromFile(jpg)) if(check.RawFormat.Guid!=ImageFormat.Jpeg.Guid) throw new InvalidDataException("JPEG verification failed");
                byte[] reportBytes=File.ReadAllBytes(jpg);
                var parsed=new JavaScriptSerializer { MaxJsonLength=ExaminationSnapshot.MaxJsonCharacters }.DeserializeObject(File.ReadAllText(json)) as Dictionary<string,object>;
                if(parsed==null || (int)parsed["schemaVersion"]!=2) throw new InvalidDataException("JSON verification failed");
                Directory.Move(stage,final); // atomically publish BOTH files on the same volume
                return new ResultPackage { ReportPath=Path.Combine(final,"Spirometry-Report.jpg"),DataPath=Path.Combine(final,"Spirometry-Data.json"),CurvesPath=Path.Combine(final,"Spirometry-Report.jpg"),SourceSnapshotId=snapshot.SnapshotId,ReportBytes=reportBytes };
            }
            catch
            {
                // Only our exact generated directory, never a caller-supplied path.
                if(Path.GetFullPath(stage).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) && Path.GetFileName(stage)==".spiro-tmp-"+id && Directory.Exists(stage)) Directory.Delete(stage,true);
                throw;
            }
        }
        public static ResultPackage Save(string requestedPath,Patient patient,IList<Trial> trials)
        { return SaveToDirectory(Path.GetDirectoryName(Path.GetFullPath(requestedPath)),ExaminationSnapshot.Capture(patient,trials)); }
        public static string Serialize(Patient p,IList<Trial> trials,string reportFile,string curvesFile)
        { return ExaminationSnapshot.Capture(p,trials).ToJson(); }
    }
}
