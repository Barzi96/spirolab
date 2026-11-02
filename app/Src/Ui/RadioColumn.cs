using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
namespace SpiroLab.Ui
{
    public sealed class RadioColumn : DataGridViewColumn
    {
        public Action<int> SelectRow;
        public RadioColumn() : base(new RadioCell()) { ReadOnly=true; }
        public sealed class RadioCell : DataGridViewCheckBoxCell
        {
            public RadioCell() { }
            protected override void Paint(Graphics g,Rectangle clip,Rectangle bounds,int row,DataGridViewElementStates state,object value,object formatted,string error,DataGridViewCellStyle style,DataGridViewAdvancedBorderStyle border,DataGridViewPaintParts parts)
            {
                base.Paint(g,clip,bounds,row,state,value,formatted,error,style,border,parts & ~DataGridViewPaintParts.ContentForeground);
                bool chosen=value is bool && (bool)value,enabled=!(Tag is bool) || (bool)Tag;
                RadioButtonState button=chosen?(enabled?RadioButtonState.CheckedNormal:RadioButtonState.CheckedDisabled):(enabled?RadioButtonState.UncheckedNormal:RadioButtonState.UncheckedDisabled);
                Size size=RadioButtonRenderer.GetGlyphSize(g,button);
                RadioButtonRenderer.DrawRadioButton(g,new Point(bounds.Left+(bounds.Width-size.Width)/2,bounds.Top+(bounds.Height-size.Height)/2),button);
            }
            protected override AccessibleObject CreateAccessibilityInstance() { return new RadioAccess(this); }
            sealed class RadioAccess : DataGridViewCell.DataGridViewCellAccessibleObject
            {
                readonly RadioCell cell;
                public RadioAccess(RadioCell owner) : base(owner) { cell=owner; }
                public override AccessibleRole Role { get { return AccessibleRole.RadioButton; } }
                public override AccessibleStates State { get { return base.State | (cell.Value is bool && (bool)cell.Value?AccessibleStates.Checked:AccessibleStates.None) | (cell.Tag is bool && !(bool)cell.Tag?AccessibleStates.Unavailable:AccessibleStates.None); } }
                public override string DefaultAction { get { return "انتخاب برای سامانه"; } }
                public override void DoDefaultAction() { RadioColumn column=cell.OwningColumn as RadioColumn; if(column!=null && column.SelectRow!=null) column.SelectRow(cell.RowIndex); }
            }
        }
    }
    public sealed class ReportColumn : DataGridViewCheckBoxColumn
    {
        public Action<int> ToggleRow;
        public ReportColumn() {CellTemplate=new ReportCell();ReadOnly=true;}
        sealed class ReportCell : DataGridViewCheckBoxCell
        {
            protected override AccessibleObject CreateAccessibilityInstance() {return new ReportAccess(this);}
            sealed class ReportAccess : DataGridViewCell.DataGridViewCellAccessibleObject
            {
                readonly ReportCell cell;
                public ReportAccess(ReportCell owner):base(owner) {cell=owner;}
                public override AccessibleRole Role {get {return AccessibleRole.CheckButton;}}
                public override AccessibleStates State {get {return (base.State & ~AccessibleStates.ReadOnly) | (cell.Value is bool && (bool)cell.Value?AccessibleStates.Checked:AccessibleStates.None) | (cell.Tag is bool && !(bool)cell.Tag?AccessibleStates.Unavailable:AccessibleStates.None);}}
                public override string DefaultAction {get {return "تغییر انتخاب نهایی";}}
                public override void DoDefaultAction() {var column=cell.OwningColumn as ReportColumn;if(column!=null && column.ToggleRow!=null) column.ToggleRow(cell.RowIndex);}
            }
        }
    }
    public sealed class ManeuverColumn : DataGridViewTextBoxColumn
    {
        public const int DeleteWidth=34;
        public Action<int> DeleteRow;
        public ManeuverColumn() {CellTemplate=new ManeuverCell();ReadOnly=true;SortMode=DataGridViewColumnSortMode.NotSortable;}
        public bool HitDelete(int width,int x) {return width>0 && x>=width-DeleteWidth;}
        sealed class ManeuverCell : DataGridViewTextBoxCell
        {
            protected override void Paint(Graphics g,Rectangle clip,Rectangle bounds,int row,DataGridViewElementStates state,object value,object formatted,string error,DataGridViewCellStyle style,DataGridViewAdvancedBorderStyle border,DataGridViewPaintParts parts)
            {
                base.Paint(g,clip,bounds,row,state,value,formatted,error,style,border,parts & ~DataGridViewPaintParts.ContentForeground);
                string text=Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)??"";
                Rectangle number=new Rectangle(bounds.Left,bounds.Top,Math.Max(0,bounds.Width-DeleteWidth),bounds.Height);
                TextRenderer.DrawText(g,text,style.Font,number,style.ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                bool enabled=!(Tag is bool) || (bool)Tag;
                Rectangle delete=new Rectangle(bounds.Right-DeleteWidth+2,bounds.Top+2,DeleteWidth-4,bounds.Height-4);
                Color color=enabled?Color.FromArgb(168,55,55):Color.FromArgb(150,150,150);
                TextRenderer.DrawText(g,"حذف",style.Font,delete,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            }
            protected override AccessibleObject CreateAccessibilityInstance() {return new ManeuverAccess(this);}
            sealed class ManeuverAccess : DataGridViewCell.DataGridViewCellAccessibleObject
            {
                readonly ManeuverCell cell;
                public ManeuverAccess(ManeuverCell owner):base(owner) {cell=owner;}
                public override AccessibleRole Role {get {return AccessibleRole.PushButton;}}
                public override string DefaultAction {get {return "حذف مانور";}}
                public override void DoDefaultAction() {var column=cell.OwningColumn as ManeuverColumn;if(column!=null && column.DeleteRow!=null) column.DeleteRow(cell.RowIndex);}
            }
        }
    }
}

