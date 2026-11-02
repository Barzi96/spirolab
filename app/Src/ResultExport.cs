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
        // The printed report remains the readability authority. 250 KiB is the
        // practical sharing target; a complex sheet may remain larger rather than
        // sacrificing small labels or curve detail.
        public const int JpegTargetBytes=250*1024;
        const int MinimumOptimizedWidth=1600;
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
        static Bitmap ResizeForJpeg(Bitmap source,int width)
        {
            int height=Math.Max(1,(int)Math.Round(source.Height*(double)width/source.Width));
            Bitmap scaled=new Bitmap(width,height,PixelFormat.Format24bppRgb);
            scaled.SetResolution(300,300);
            using(Graphics g=Graphics.FromImage(scaled))
            {
                g.Clear(Color.White); g.CompositingMode=CompositingMode.SourceCopy;
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                g.SmoothingMode=SmoothingMode.HighQuality;
                using(ImageAttributes attributes=new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(source,new Rectangle(0,0,width,height),0,0,source.Width,source.Height,GraphicsUnit.Pixel,attributes);
                }
            }
            return scaled;
        }
        // Encode all candidates in memory, then write the final bytes exactly once.
        // Candidate order is deliberate: full resolution first, highest quality
        // first, with conservative width reduction only when the target requires it.
        public static JpegExportInfo EncodeOptimizedJpeg(Bitmap source,string path)
        {
            if(source==null) throw new ArgumentNullException("source");
            if(string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
            int[] widths=source.Width>MinimumOptimizedWidth?new int[] {source.Width,2000,1800,MinimumOptimizedWidth}:new int[] {source.Width};
            int[] qualities=new int[] {90,86,82,78};
            JpegExportInfo selected=null;
            byte[] selectedBytes=null;
            foreach(int width in widths)
            {
                if(width>source.Width || width<Math.Min(source.Width,MinimumOptimizedWidth)) continue;
                Bitmap candidate=width==source.Width?null:ResizeForJpeg(source,width);
                try
                {
                    Bitmap image=candidate??source;
                    foreach(int quality in qualities)
                    {
                        byte[] bytes=EncodeJpegBytes(image,quality);
                        if(selectedBytes==null || bytes.Length<selectedBytes.Length)
                        {
                            selectedBytes=bytes;
                            selected=new JpegExportInfo {OriginalWidth=source.Width,OriginalHeight=source.Height,Width=image.Width,Height=image.Height,Quality=quality,Bytes=bytes.Length,TargetBytes=JpegTargetBytes,TargetMet=bytes.Length<=JpegTargetBytes,Downscaled=image.Width!=source.Width};
                        }
                        if(bytes.Length<=JpegTargetBytes)
                        {
                            selectedBytes=bytes; selected=new JpegExportInfo {OriginalWidth=source.Width,OriginalHeight=source.Height,Width=image.Width,Height=image.Height,Quality=quality,Bytes=bytes.Length,TargetBytes=JpegTargetBytes,TargetMet=true,Downscaled=image.Width!=source.Width};
                            File.WriteAllBytes(path,bytes);
                            return selected;
                        }
                    }
                }
                finally { if(candidate!=null) candidate.Dispose(); }
            }
            // A complex sheet may remain above the preferred target. The smallest
            // readable candidate is used, never a thumbnail or a recompressed copy.
            File.WriteAllBytes(path,selectedBytes);
            return selected;
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
                using(Bitmap report=SpiroReport.Render(snapshot)) quality=EncodeOptimizedJpeg(report,jpg);
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
