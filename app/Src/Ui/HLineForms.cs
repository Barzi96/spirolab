using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using SpiroLab.HLine;
namespace SpiroLab.Ui
{
    public sealed class HLineLoginForm : Form
    {
        readonly HLineClient client;
        readonly TextBox username,password,mfa;
        readonly Label status;
        readonly Button login;
        bool working;
        string pendingPassword; // kept only until the MFA retry, never written to disk or a log
        public HLineLoginForm(HLineClient client)
        {
            this.client=client; ClinicalStyle.Form(this,"ورود به سامانه",new Size(590,360));
            TableLayoutPanel table=ClinicalStyle.FormTable(); Controls.Add(table);
            username=ClinicalStyle.TextField("HLineUsername",""); password=ClinicalStyle.TextField("HLinePassword",""); password.UseSystemPasswordChar=true;
            mfa=ClinicalStyle.TextField("HLineMfa",""); mfa.MaxLength=6; mfa.TextAlign=HorizontalAlignment.Center;
            ClinicalStyle.Row(table,"نام کاربری",username,48); ClinicalStyle.Row(table,"رمز عبور",password,48); ClinicalStyle.Row(table,"کد تأیید دومرحله‌ای",mfa,48);
            status=new Label {Name="HLineStatus",Dock=DockStyle.Bottom,Height=44,Text=client.StatusText,Padding=new Padding(15,6,15,6)}; Controls.Add(status);
            login=ClinicalStyle.Button("ورود",async delegate { await Login(); },true); login.Name="HLineLogin"; login.Dock=DockStyle.Bottom; Controls.Add(login); AcceptButton=login;
            Shown+=delegate { if(username.Text.Length==0) username.Focus(); else if(password.Text.Length==0) password.Focus(); else mfa.Focus(); };
            FormClosing+=delegate(object s,FormClosingEventArgs e) {e.Cancel=working;};
        }
        async Task Login()
        {
            working=true; login.Enabled=false; status.Text="در حال ورود…";
            string u=username.Text,p=password.Text.Length>0?password.Text:(pendingPassword??""),c=Fmt.NormalizeNumber(mfa.Text);
            pendingPassword=null; password.Clear(); mfa.Clear();
            try { await Task.Factory.StartNew(delegate { client.Login(u,p,c); }); status.Text=client.StatusText; DialogResult=DialogResult.OK; }
            catch(HLineException e)
            {
                status.Text=e.Message;
                if(e.Code=="missing_mfa_token" || e.Code=="invalid_mfa_token") { pendingPassword=p; mfa.Focus(); }
                else if(e.Code=="credentials") username.Focus();
            }
            catch {status.Text="عدم دسترسی به سامانه";}
            finally {p=c=null; working=false;login.Enabled=true;if(client.Status==SessionStatus.Connected) {pendingPassword=null;Close();}}
        }
        // Single entry point used by both the toolbar and the patient form.
        public static bool SignIn(HLineClient client,IWin32Window owner)
        {
            if(client==null) return false;
            if(client.Status==SessionStatus.Connected) { try { client.VerifySession(); return true; } catch(HLineException) { } }
            using(HLineLoginForm form=new HLineLoginForm(client)) return form.ShowDialog(owner)==DialogResult.OK && client.Status==SessionStatus.Connected;
        }
    }
}
