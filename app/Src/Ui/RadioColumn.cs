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
}

