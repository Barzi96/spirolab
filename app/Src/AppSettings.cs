using System;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
namespace SpiroLab
{
    public sealed class AppSettings
    {
        public const string DefaultCenter="شرکت سنگ آهن مرکزی ایران - بافق";
        public string CenterName=DefaultCenter;
        public string OperatorName="";
        [XmlIgnore] public string LoadWarning="";
        public static string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"SpiroLab","settings.xml"); } }
        public static AppSettings Load(string path=null)
        {
            path=path??SettingsPath; if(!File.Exists(path)) return new AppSettings();
            try
            {
                if(new FileInfo(path).Length>16384) throw new InvalidDataException();
                using(XmlReader reader=XmlReader.Create(path,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null }))
                { AppSettings settings=(AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(reader); settings.Validate(); return settings; }
            }
            catch { return new AppSettings { LoadWarning="تنظیمات خوانده نشد؛ فایل قبلی حفظ شده است." }; }
        }
        public void Validate()
        { if(string.IsNullOrWhiteSpace(CenterName) || CenterName.Length>200 || OperatorName==null || OperatorName.Length>100) throw new ArgumentException("نام مرکز و کارشناس معتبر وارد کنید."); }
        public void Save(string path=null)
        {
            Validate(); path=path??SettingsPath; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try { using(FileStream stream=File.Create(temp)) new XmlSerializer(typeof(AppSettings)).Serialize(stream,this); if(File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path); }
            finally { if(File.Exists(temp)) File.Delete(temp); }
        }
    }
    public static class ExaminationDates
    {
        public static bool TryParse(string input,out DateTime date)
        {
            date=default(DateTime); string value=Fmt.NormalizeNumber(input).Replace('/','-');
            string[] pieces=value.Split('-'); int year,month,day;
            if(pieces.Length!=3 || !int.TryParse(pieces[0],out year) || !int.TryParse(pieces[1],out month) || !int.TryParse(pieces[2],out day)) return false;
            try
            {
                if(year>=1300 && year<=1499) date=new PersianCalendar().ToDateTime(year,month,day,0,0,0,0);
                else if(year>=1900 && year<=2100) date=new DateTime(year,month,day);
                else return false;
                return true;
            }
            catch(ArgumentOutOfRangeException) { return false; }
        }
        public static string Format(DateTime date) { return date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture); }
        public static string Jalali(DateTime date)
        { PersianCalendar calendar=new PersianCalendar(); return string.Format(CultureInfo.InvariantCulture,"{0:0000}/{1:00}/{2:00}",calendar.GetYear(date),calendar.GetMonth(date),calendar.GetDayOfMonth(date)); }
        public static string DisplayJalali(DateTime date)
        { string value=Jalali(date); for(int i=0;i<10;i++) value=value.Replace((char)('0'+i),(char)('۰'+i)); return value; }
    }
}
