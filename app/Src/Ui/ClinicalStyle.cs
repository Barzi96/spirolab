using System;
using System.Drawing;
using System.Windows.Forms;
namespace SpiroLab.Ui
{
    internal static class ClinicalStyle
    {
        public static readonly Color Accent=Color.FromArgb(19,103,119),Ink=Color.FromArgb(32,49,64),Background=Color.FromArgb(244,247,250),Border=Color.FromArgb(222,229,235);
        public static void Form(Form f,string title,Size size)
        {
            f.Text=title; f.Font=new Font("Tahoma",9.5f); f.BackColor=Background; f.ForeColor=Ink; f.RightToLeft=RightToLeft.Yes;
            f.AutoScaleDimensions=new SizeF(96,96); f.AutoScaleMode=AutoScaleMode.Dpi; f.Size=size; f.StartPosition=FormStartPosition.CenterParent;
            f.Shown+=delegate { if(f.StartPosition!=FormStartPosition.CenterParent) return; Rectangle area=Screen.FromControl(f).WorkingArea; if(f.Height>area.Height-30) f.Height=area.Height-30; if(f.Width>area.Width-30) f.Width=area.Width-30; };
        }
        public static Button Button(string text,EventHandler action,bool primary)
        {
            Button b=new Button { Text=text,AutoSize=true,MinimumSize=new Size(96,36),FlatStyle=FlatStyle.Flat,BackColor=primary?Accent:Color.White,ForeColor=primary?Color.White:Ink,Margin=new Padding(4),Padding=new Padding(12,4,12,4),Cursor=Cursors.Hand };
            b.FlatAppearance.BorderColor=Border; b.Click+=action; return b;
        }
        public static Label Label(string text) { return new Label { Text=text,AutoSize=false,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,RightToLeft=RightToLeft.Yes }; }
        public static TableLayoutPanel FormTable()
        {
            TableLayoutPanel t=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(16),RightToLeft=RightToLeft.Yes };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65)); return t;
        }
        public static void Row(TableLayoutPanel table,string caption,Control control,int height)
        {
            int row=table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            table.Controls.Add(Label(caption),0,row); control.Dock=DockStyle.Fill; control.Margin=new Padding(3,7,3,5); table.Controls.Add(control,1,row);
        }
        public static TextBox TextField(string name,string value) { return new TextBox { Name=name,Text=value??"",MaxLength=500 }; }
    }
}

