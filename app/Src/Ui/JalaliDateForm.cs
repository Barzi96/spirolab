using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
namespace SpiroLab.Ui
{
    public sealed class JalaliDateForm : Form
    {
        readonly PersianCalendar calendar=new PersianCalendar();
        readonly TableLayoutPanel days;
        readonly Label heading;
        DateTime month;
        public DateTime Value { get; private set; }
        public JalaliDateForm(DateTime selected)
        {
            Value=selected.Date; month=selected.Date;
            ClinicalStyle.Form(this,"انتخاب تاریخ معاینه",new Size(430,390));
            FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=MinimizeBox=false;
            FlowLayoutPanel navigation=new FlowLayoutPanel { Dock=DockStyle.Top,Height=51,FlowDirection=FlowDirection.RightToLeft,RightToLeft=RightToLeft.No };
            navigation.Controls.Add(ClinicalStyle.Button("ماه قبل",delegate { MoveMonth(-1); },false));
            heading=new Label { AutoSize=false,Width=165,Height=42,TextAlign=ContentAlignment.MiddleCenter }; navigation.Controls.Add(heading);
            navigation.Controls.Add(ClinicalStyle.Button("ماه بعد",delegate { MoveMonth(1); },false));
            days=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=7,RowCount=7,RightToLeft=RightToLeft.Yes,Padding=new Padding(12) };
            for(int i=0;i<7;i++) { days.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/7)); days.RowStyles.Add(new RowStyle(SizeType.Percent,100f/7)); }
            Controls.Add(days); Controls.Add(navigation); DrawMonth();
        }
        void MoveMonth(int amount) { try { month=calendar.AddMonths(month,amount); DrawMonth(); } catch(ArgumentOutOfRangeException) { } }
        void DrawMonth()
        {
            while(days.Controls.Count>0) days.Controls[0].Dispose();
            int year=calendar.GetYear(month),m=calendar.GetMonth(month);
            string[] names={"فروردین","اردیبهشت","خرداد","تیر","مرداد","شهریور","مهر","آبان","آذر","دی","بهمن","اسفند"};
            heading.Text=names[m-1]+" "+year;
            string[] week={"ش","ی","د","س","چ","پ","ج"};
            for(int col=0;col<7;col++) days.Controls.Add(new Label { Text=week[col],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter },col,0);
            DateTime first=calendar.ToDateTime(year,m,1,0,0,0,0); int offset=((int)first.DayOfWeek+1)%7;
            for(int day=1;day<=calendar.GetDaysInMonth(year,m);day++)
            {
                DateTime date=calendar.ToDateTime(year,m,day,0,0,0,0); int slot=offset+day-1;
                Button b=new Button { Text=day.ToString(CultureInfo.InvariantCulture),Tag=date,Dock=DockStyle.Fill,Margin=new Padding(2),FlatStyle=FlatStyle.Flat,BackColor=date==Value?ClinicalStyle.Accent:Color.White,ForeColor=date==Value?Color.White:ClinicalStyle.Ink };
                b.FlatAppearance.BorderColor=ClinicalStyle.Border;
                b.Click+=delegate(object sender,EventArgs e) { Value=(DateTime)((Button)sender).Tag; DialogResult=DialogResult.OK; };
                days.Controls.Add(b,slot%7,slot/7+1);
            }
        }
    }
}
