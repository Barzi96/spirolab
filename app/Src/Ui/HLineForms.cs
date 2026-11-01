using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using SpiroLab.HLine;
namespace SpiroLab.Ui
{
    public sealed class HLineLoginForm : Form
    {
        readonly HLineClient client;
        readonly string preferredProfile;
        readonly TextBox username,password,mfa;
        readonly Label status;
        readonly Button login,firefoxConnect,firefoxOpen;
        readonly Timer poll;
        bool working;
        int pollAttempts;
        string pendingPassword; // kept only until the MFA retry, never written to disk or a log
        public HLineLoginForm(HLineClient client) : this(client,null) { }
        public HLineLoginForm(HLineClient client,string preferredProfile)
        {
            this.client=client; this.preferredProfile=preferredProfile;
            ClinicalStyle.Form(this,"ورود به سامانه",new Size(640,470));
            TableLayoutPanel table=ClinicalStyle.FormTable(); Controls.Add(table);
            username=ClinicalStyle.TextField("HLineUsername",""); password=ClinicalStyle.TextField("HLinePassword",""); password.UseSystemPasswordChar=true;
            mfa=ClinicalStyle.TextField("HLineMfa",""); mfa.MaxLength=6; mfa.TextAlign=HorizontalAlignment.Center;
            ClinicalStyle.Row(table,"نام کاربری",username,48); ClinicalStyle.Row(table,"رمز عبور",password,48); ClinicalStyle.Row(table,"کد تأیید دومرحله‌ای",mfa,48);
            Label session=new Label
            {
                Name="HLineSessionHint",
                Text="اگر در فایرفاکس وارد سامانه شده باشید، اسپیرولب همان نشست را به‌کار می‌برد و نیازی به نام کاربری، رمز یا کد تأیید نیست.",
                AutoSize=true,MaximumSize=new Size(560,0),Padding=new Padding(15,4,15,8)
            };
            ClinicalStyle.Row(table,"نشست فایرفاکس",session,70);
            Panel bottom=new Panel {Dock=DockStyle.Bottom,Height=150,BackColor=Color.White};
            status=new Label {Name="HLineStatus",Dock=DockStyle.Top,Height=44,Text=client.StatusText,Padding=new Padding(15,6,15,6)}; bottom.Controls.Add(status);
            FlowLayoutPanel actions=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=56,FlowDirection=FlowDirection.RightToLeft,RightToLeft=RightToLeft.No,Padding=new Padding(12,4,12,4)};
            firefoxConnect=ClinicalStyle.Button("اتصال مجدد با فایرفاکس",async delegate { await Adopt(); },true); firefoxConnect.Name="HLineFirefoxConnect";
            firefoxOpen=ClinicalStyle.Button("باز کردن سایت در فایرفاکس",delegate { OpenFirefox(); },false); firefoxOpen.Name="HLineFirefoxOpen";
            login=ClinicalStyle.Button("ورود با رمز",async delegate { await Login(); },false); login.Name="HLineLogin";
            actions.Controls.Add(firefoxConnect); actions.Controls.Add(firefoxOpen); actions.Controls.Add(login);
            bottom.Controls.Add(actions); Controls.Add(bottom);
            AcceptButton=login;
            poll=new Timer {Interval=3000}; poll.Tick+=async delegate { if(working) return; if(++pollAttempts>40) { poll.Stop(); return; } await Adopt(); };
            Shown+=delegate { if(username.Text.Length==0) username.Focus(); else if(password.Text.Length==0) password.Focus(); else mfa.Focus(); };
            FormClosing+=delegate(object s,FormClosingEventArgs e) {e.Cancel=working;};
            Disposed+=delegate { poll.Dispose(); };
        }
        void SetActions(bool enabled) { login.Enabled=firefoxConnect.Enabled=firefoxOpen.Enabled=enabled; }
        async Task Login()
        {
            if(working) return;
            working=true; SetActions(false); status.Text="در حال ورود…";
            string u=username.Text,p=password.Text.Length>0?password.Text:(pendingPassword??""),c=Fmt.NormalizeNumber(mfa.Text);
            pendingPassword=null; password.Clear(); mfa.Clear();
            Control focus=null;
            try { await Task.Factory.StartNew(delegate { client.Login(u,p,c); }); status.Text=client.StatusText; DialogResult=DialogResult.OK; }
            catch(HLineException e)
            {
                status.Text=e.Message;
                if(e.Code=="missing_mfa_token" || e.Code=="invalid_mfa_token") { pendingPassword=p; focus=mfa; }
                else if(e.Code=="credentials") focus=username;
            }
            catch {status.Text="عدم دسترسی به سامانه";}
            finally
            {
                p=c=null; working=false; SetActions(true);
                if(client.Status==SessionStatus.Connected) {pendingPassword=null;Close();}
                else if(focus!=null) focus.Focus();
            }
        }
        // Reads the current Firefox session, validates it and closes the dialog
        // without asking for any credential.
        async Task Adopt()
        {
            if(working) return;
            working=true; SetActions(false); status.Text="در حال خواندن نشست فایرفاکس…";
            bool ok=false; string problem=null;
            try
            {
                ok=await Task.Factory.StartNew(delegate
                {
                    string message; bool adopted=client.TryAdoptFirefoxSession(preferredProfile,out message); problem=message; return adopted;
                });
            }
            catch(Exception) { problem="خواندن نشست فایرفاکس ممکن نشد."; }
            working=false; SetActions(true);
            if(ok) { poll.Stop(); status.Text="متصل به سامانه"; DialogResult=DialogResult.OK; Close(); }
            else if(problem!=null) status.Text=problem;
        }
        void OpenFirefox()
        {
            string executable=FirefoxPath();
            if(executable==null) { status.Text="فایرفاکس روی این رایانه پیدا نشد."; return; }
            try
            {
                ProcessStartInfo start=new ProcessStartInfo(executable,"https://hline.ssu.ac.ir/");
                start.UseShellExecute=true;
                Process.Start(start);
            }
            catch { status.Text="اجرای فایرفاکس ممکن نشد."; return; }
            pollAttempts=0; poll.Start();
            status.Text="در فایرفاکس وارد سامانه شوید؛ همین‌جا به‌طور خودکار بررسی می‌شود…";
        }
        static string FirefoxPath()
        {
            string[] candidates=new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Mozilla Firefox","firefox.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Mozilla Firefox","firefox.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Mozilla Firefox","firefox.exe")
            };
            foreach(string candidate in candidates) if(File.Exists(candidate)) return candidate;
            return null;
        }
        // Single entry point used by the toolbar and the patient form: reuse a
        // live Firefox session first, only then show the password dialog.
        public static bool SignIn(HLineClient client,IWin32Window owner) { return SignIn(client,owner,null); }
        public static bool SignIn(HLineClient client,IWin32Window owner,string preferredProfile)
        {
            if(client==null) return false;
            if(client.Status==SessionStatus.Connected) { try { client.VerifySession(); return true; } catch(HLineException) { } }
            string problem;
            if(client.TryAdoptFirefoxSession(preferredProfile,out problem)) return true;
            using(HLineLoginForm form=new HLineLoginForm(client,preferredProfile)) return form.ShowDialog(owner)==DialogResult.OK && client.Status==SessionStatus.Connected;
        }
    }
}
