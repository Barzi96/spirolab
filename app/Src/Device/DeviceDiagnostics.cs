using System;
using System.IO;
using System.Management;
using System.Text;

namespace SpiroLab.Device
{
    public sealed class DeviceDiagnostics
    {
        public string ProgramVersion="5.0.0";
        public string WindowsVersion=Environment.OSVersion.VersionString;
        public string Architecture=Environment.Is64BitOperatingSystem?"x64":"x86";
        public bool Detected;
        public string VidPid="—",DriverName="—",DriverVersion="—",DriverSigner="unknown",DriverState="missing";
        public string Communication="not_verified",LastSafeError="none";
        public static string Classify(bool detected,string service,int configCode)
        { return !detected?"missing":!string.Equals(service,"MIRUSB",StringComparison.OrdinalIgnoreCase)?"incompatible":configCode!=0?"not_ready":"ready"; }
        public static DeviceDiagnostics Read(bool controlResponded=false,bool liveSamples=false,string error="none")
        {
            DeviceDiagnostics d=new DeviceDiagnostics();
            try
            {
                using(ManagementObjectSearcher search=new ManagementObjectSearcher("SELECT PNPDeviceID,Service,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%VID_152B%'") )
                using(ManagementObjectCollection results=search.Get()) foreach(ManagementObject row in results)
                {
                    int code=Convert.ToInt32(row["ConfigManagerErrorCode"]);
                    if(code==45) continue;
                    string path=Convert.ToString(row["PNPDeviceID"]); d.Detected=true;
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
                    if(Convert.ToString(row["DeviceID"]).IndexOf("VID_152B",StringComparison.OrdinalIgnoreCase)>=0)
                    { d.DriverVersion=Convert.ToString(row["DriverVersion"]); d.DriverSigner=Convert.ToString(row["IsSigned"])+": "+Convert.ToString(row["Signer"]); break; }
            }
            catch { d.LastSafeError="Windows device inventory unavailable"; }
            if(!d.Detected && File.Exists(DriverBinaryPath))
            { d.DriverName="MIRUSB (installed file; binding unverified)"; d.DriverVersion=System.Diagnostics.FileVersionInfo.GetVersionInfo(DriverBinaryPath).FileVersion; d.DriverState="installed_binding_unverified"; }
            if(error!="none") d.LastSafeError=error;
            d.Communication=liveSamples?"samples_received_unverified":controlResponded?"control_response_only":error!="none"?"failed":"not_verified";
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
                if(Communication=="failed") return "ارتباط برقرار شد ولی داده تست دریافت نشد";
                if(Communication=="control_response_only") return "پاسخ کنترل دریافت شد؛ تست زنده هنوز تأیید نشده";
                if(Communication=="samples_received_unverified") return "نمونه دریافت شد؛ اعتبار سخت‌افزاری و بالینی تأیید نشده";
                return "درایور شناسایی شد؛ ارتباط دستگاه هنوز بررسی نشده";
            }
        }
        public string SafeText()
        { return "وضعیت دستگاه\r\n"+PersianSummary+"\r\nProgram: "+ProgramVersion+"\r\nWindows: "+WindowsVersion+" "+Architecture+"\r\nDetected: "+Detected+"\r\nUSB: "+VidPid+"\r\nDriver: "+DriverName+" "+DriverVersion+"\r\nDriver signer (Windows inventory): "+DriverSigner+"\r\nDriver state: "+DriverState+"\r\nCommunication: "+Communication+"\r\nLast safe error: "+LastSafeError+"\r\nHARDWARE VALIDATION PENDING\r\n"; }
    }
}
