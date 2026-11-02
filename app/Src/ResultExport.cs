using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        public int ReportWidth;
        public int ReportHeight;
        public int JpegQuality;
        internal string SourceSnapshotId;
        public byte[] ReportBytes;
    }
    public sealed class JpegExportInfo
    {
        public int OriginalWidth,OriginalHeight,Width,Height,Quality,Bytes,TargetBytes;
        public bool TargetMet,Downscaled;
    }
    public static class ResultExport
    {
        // The printed report remains the readability authority. 250 KiB stays the
        // preferred sharing target; a dense sheet may sit above it rather than
        // losing small labels or curve detail.
        public const int JpegTargetBytes=250*1024;
        // Hard readability ceiling actually guaranteed by the export path. Measured
        // on the real 1800x2546 sheet: q80 = 277 KiB, q70 = 248 KiB, so the ceiling
        // leaves room for the densest clinical sheet without any quality search.
        public const int JpegCeilingBytes=320*1024;
        // Single quality used for the shared JPG. Measured on real report samples at
        // 1800x2546: quality 80 keeps 99.3% of text ink at 44.2 dB PSNR; lower
        // qualities only save ~27 KiB and start adding ringing on the small table.
        public const int JpegQuality=80;
        internal static Action<string> ExportCheckpoint = null;
        static ImageCodecInfo JpegEncoder()
        {
            foreach(ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders()) if(codec.MimeType=="image/jpeg") return codec;
            throw new InvalidOperationException("JPEG encoder unavailable");
        }
        public static byte[] EncodeJpegBytes(Bitmap image,int quality)
        {
            if(image==null) throw new ArgumentNullException("image");
            if(quality<1 || quality>100) throw new ArgumentOutOfRangeException("quality");
            using(MemoryStream output=new MemoryStream()) using(EncoderParameters options=new EncoderParameters(1))
            {
                options.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,(long)quality);
                image.Save(output,JpegEncoder(),options);
                byte[] bytes=output.ToArray();
                ValidateJpegBytes(bytes);
                return bytes;
            }
        }
        // Retained as a deliberate low-level encoder for diagnostics and evidence;
        // the production export path always calls EncodeOptimizedJpeg instead.
        public static void EncodeJpeg(Bitmap image,string path,int quality)
        {
            if(string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
            File.WriteAllBytes(path,EncodeJpegBytes(image,quality));
        }
        static void ValidateJpegBytes(byte[] bytes)
        {
            if(bytes==null || bytes.Length<4 || bytes[0]!=255 || bytes[1]!=216 || bytes[bytes.Length-2]!=255 || bytes[bytes.Length-1]!=217) throw new InvalidDataException("JPEG header/trailer verification failed");
            using(MemoryStream stream=new MemoryStream(bytes,false)) using(Image image=Image.FromStream(stream))
                if(image.RawFormat.Guid!=ImageFormat.Jpeg.Guid) throw new InvalidDataException("JPEG verification failed");
        }
        // One render, one encode, one write. There is no candidate loop over sizes
        // or qualities: the JPG is rasterised directly at its final size, so the
        // output is deterministic, cheap and byte-identical to what is uploaded.
        public static JpegExportInfo EncodeOptimizedJpeg(Bitmap source,string path)
        {
            if(source==null) throw new ArgumentNullException("source");
            if(string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
            byte[] bytes=EncodeJpegBytes(source,JpegQuality);
            File.WriteAllBytes(path,bytes);
            return new JpegExportInfo {OriginalWidth=source.Width,OriginalHeight=source.Height,Width=source.Width,Height=source.Height,Quality=JpegQuality,Bytes=bytes.Length,TargetBytes=JpegTargetBytes,TargetMet=bytes.Length<=JpegTargetBytes,Downscaled=false};
        }
        public static ResultPackage SaveToDirectory(string parent,ExaminationSnapshot snapshot)
        {
            int workflow=snapshot.Data.SelectionWorkflowVersion;
            snapshot.Data.ValidateSelection(workflow>0 && workflow<3);
            snapshot.Data.ValidateReportSelection(true);
            string root=Path.GetFullPath(parent); Directory.CreateDirectory(root);
            string id=snapshot.SnapshotId;
            string stage=Path.Combine(root,".spiro-tmp-"+id), final=Path.Combine(root,"Examination-"+id);
            if(Directory.Exists(final) || Directory.Exists(stage)) throw new IOException("مسیر خروجی از قبل وجود دارد؛ پروندهٔ قبلی حفظ شد.");
            Directory.CreateDirectory(stage);
            try
            {
                string jpg=Path.Combine(stage,"Spirometry-Report.jpg"), json=Path.Combine(stage,"Spirometry-Data.json");
                JpegExportInfo quality;
                using(Bitmap report=SpiroReport.RenderForJpeg(snapshot)) quality=EncodeOptimizedJpeg(report,jpg);
                if(ExportCheckpoint!=null) ExportCheckpoint("after-jpg");
                File.WriteAllText(json,snapshot.ToJson(),new UTF8Encoding(false));
                using(Image check=Image.FromFile(jpg)) if(check.RawFormat.Guid!=ImageFormat.Jpeg.Guid) throw new InvalidDataException("JPEG verification failed");
                byte[] reportBytes=File.ReadAllBytes(jpg);
                ValidateJpegBytes(reportBytes);
                if(reportBytes.Length!=quality.Bytes) throw new InvalidDataException("JPEG write verification failed");
                var parsed=new JavaScriptSerializer { MaxJsonLength=ExaminationSnapshot.MaxJsonCharacters }.DeserializeObject(File.ReadAllText(json)) as Dictionary<string,object>;
                if(parsed==null || (int)parsed["schemaVersion"]!=2) throw new InvalidDataException("JSON verification failed");
                Directory.Move(stage,final); // atomically publish BOTH files on the same volume
                return new ResultPackage { ReportPath=Path.Combine(final,"Spirometry-Report.jpg"),DataPath=Path.Combine(final,"Spirometry-Data.json"),CurvesPath=Path.Combine(final,"Spirometry-Report.jpg"),ReportWidth=quality.Width,ReportHeight=quality.Height,JpegQuality=quality.Quality,SourceSnapshotId=snapshot.SnapshotId,ReportBytes=reportBytes };
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
