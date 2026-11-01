using System;
using System.IO;
using System.Management;
using System.Text;

namespace SpiroLab.Device
{
    public sealed class DeviceDiagnostics
    {
        public string ProgramVersion="5.1.0";
        public string WindowsVersion=Environment.OSVersion.VersionString;
        public string Architecture=Environment.Is64BitOperatingSystem?"x64":"x86";
        public bool Detected;
        public string VidPid="—",DriverName="—",DriverVersion="—",DriverSigner="unknown",DriverState="missing";
        public string Communication="not_verified",LastSafeError="none";
        public bool HandleOpened,ControlResponded,StartCommandSucceeded;
        public int LivePackets,DecodedSamples;
        public static string CommunicationState(bool handle,bool control,bool command,int packets,int samples,string error)
        { return samples>0?"samples_received_unverified":packets>0?"packets_received_no_decoded_samples":command?"start_command_succeeded_no_live_packets":control?"control_response_only":handle?"handle_opened_no_control_response":error!="none"?"failed":"not_verified"; }
        public static string Classify(bool detected,string service,int configCode)
        { return !detected?"missing":!string.Equals(service,"MIRUSB",StringComparison.OrdinalIgnoreCase)?"incompatible":configCode!=0?"not_ready":"ready"; }
        public static DeviceDiagnostics Read(bool controlResponded=false,bool liveSamples=false,string error="none",bool handleOpened=false,bool commandSucceeded=false,int packets=0,int decodedSamples=0)
        {
            DeviceDiagnostics d=new DeviceDiagnostics();
            d.HandleOpened=handleOpened; d.ControlResponded=controlResponded; d.StartCommandSucceeded=commandSucceeded;
            d.LivePackets=packets; d.DecodedSamples=decodedSamples;
            try
            {
                using(ManagementObjectSearcher search=new ManagementObjectSearcher("SELECT PNPDeviceID,Service,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%VID_152B%'") )
                using(ManagementObjectCollection results=search.Get()) foreach(ManagementObject row in results)
                {
                    int code=Convert.ToInt32(row["ConfigManagerErrorCode"]);
                    if(code==45) continue;
                    string path=Convert.ToString(row["PNPDeviceID"]);
                    if(path.IndexOf("VID_152B&PID_0002",StringComparison.OrdinalIgnoreCase)<0) continue;
                    d.Detected=true;
                    var match=System.Text.RegularExpressions.Regex.Match(path,@"VID_[0-9A-F]{4}&PID_[0-9A-F]{4}",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    d.VidPid=match.Success?match.Value:"VID_152B";
                    string service=Convert.ToString(row["Service"]); d.DriverName=service; d.DriverState=Classify(true,service,code);
                    if(service=="MIRUSB")
                    {
                        string binary=DriverBinaryPath;
                        if(File.Exists(binary)) d.DriverVersion=System.Diagnostics.FileVersionInfo.GetVersionInfo(binary).FileVersion;
                        else d.DriverState="missing";
                        if(File.Exists(binary)) using(BinaryReader pe=new BinaryReader(File.OpenRead(binary)))
                        { pe.BaseStream.Position=0x3c; int offset=pe.ReadInt32(); pe.BaseStream.Position=offset+4; ushort machine=pe.ReadUInt16(); if((Environment.Is64BitOperatingSystem && machine!=0x8664) || (!Environment.Is64BitOperatingSystem && machine!=0x14c)) d.DriverState="incompatible"; }
                    }
                    break;
                }
                using(ManagementObjectSearcher signed=new ManagementObjectSearcher("SELECT DeviceID,DriverVersion,Signer,IsSigned FROM Win32_PnPSignedDriver"))
                using(ManagementObjectCollection results=signed.Get()) foreach(ManagementObject row in results)
                    if(Convert.ToString(row["DeviceID"]).IndexOf("VID_152B&PID_0002",StringComparison.OrdinalIgnoreCase)>=0)
                    { d.DriverVersion=Convert.ToString(row["DriverVersion"]); d.DriverSigner=Convert.ToString(row["IsSigned"])+": "+Convert.ToString(row["Signer"]); break; }
            }
            catch { d.LastSafeError="Windows device inventory unavailable"; }
            if(!d.Detected && File.Exists(DriverBinaryPath))
            { d.DriverName="MIRUSB (installed file; binding unverified)"; d.DriverVersion=System.Diagnostics.FileVersionInfo.GetVersionInfo(DriverBinaryPath).FileVersion; d.DriverState="installed_binding_unverified"; }
            if(error!="none") d.LastSafeError=error;
            d.Communication=CommunicationState(handleOpened,controlResponded,commandSucceeded,packets,liveSamples?Math.Max(1,decodedSamples):decodedSamples,error);
            return d;
        }
        public static string DriverBinaryPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess?"Sysnative":"System32","drivers","mirusb.sys"); } }
        public string PersianSummary
        {
            get
            {
                if(LastSafeError=="Windows device inventory unavailable") return "خواندن وضعیت دستگاه از ویندوز ممکن نشد";
                if(!Detected) return "دستگاه در ویندوز شناسایی نشد";
                if(DriverState!="ready") return "دستگاه شناسایی شد ولی درایور ارتباطی آماده نیست";
                if(Communication=="failed") return HandleOpened?"رابط باز شد ولی ارتباط فرمان تأیید نشد":"درایور شناسایی شد ولی رابط USB باز نشد";
                if(Communication=="control_response_only") return "پاسخ کنترل دریافت شد؛ تست زنده هنوز تأیید نشده";
                if(Communication=="samples_received_unverified") return "نمونه دریافت شد؛ اعتبار سخت‌افزاری و بالینی تأیید نشده";
                if(Communication=="packets_received_no_decoded_samples") return "بسته دریافت شد؛ هنوز نمونهٔ جریان قابل پردازش وجود ندارد";
                if(Communication=="start_command_succeeded_no_live_packets") return "فرمان شروع پذیرفته شد؛ هنوز بستهٔ زنده دریافت نشده";
                if(Communication=="handle_opened_no_control_response") return "رابط USB باز شد؛ پاسخ فرمان کنترل دریافت نشده";
                return "درایور شناسایی شد؛ ارتباط دستگاه هنوز بررسی نشده";
            }
        }
        public string SafeText()
        { return "وضعیت دستگاه\r\n"+PersianSummary+"\r\nProgram: "+ProgramVersion+"\r\nWindows: "+WindowsVersion+" "+Architecture+"\r\nDetected: "+Detected+"\r\nUSB: "+VidPid+"\r\nDriver: "+DriverName+" "+DriverVersion+"\r\nDriver signer (Windows inventory): "+DriverSigner+"\r\nDriver state: "+DriverState+"\r\nHandle opened: "+HandleOpened+"\r\nControl response: "+ControlResponded+"\r\nStart command succeeded: "+StartCommandSucceeded+"\r\nLive packets: "+LivePackets+"\r\nDecoded samples: "+DecodedSamples+"\r\nCommunication: "+Communication+"\r\nLast safe error: "+LastSafeError+"\r\nHARDWARE VALIDATION PENDING\r\n"; }
    }
}
