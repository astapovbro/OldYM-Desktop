using System;
using System.Drawing;
using System.Windows.Forms;
namespace OldYM {
    internal static class Palette {
        public static readonly Color Background=Color.FromArgb(23,23,25), Sidebar=Color.FromArgb(17,17,19), Panel=Color.FromArgb(30,30,33), Raised=Color.FromArgb(40,40,44), Text=Color.FromArgb(242,242,242), Muted=Color.FromArgb(155,155,164), Accent=Color.FromArgb(255,216,48), Line=Color.FromArgb(54,54,59);
    }
    internal sealed class MusicButton : Button {
        public bool Accent, Navigation, Selected;
        public string Symbol="";
        public MusicButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;TabStop=true;}
        protected override void OnPaint(PaintEventArgs e){
            bool hot=Enabled&&ClientRectangle.Contains(PointToClient(Cursor.Position));
            Color fill=Accent?Palette.Accent:(Selected?Palette.Raised:(hot?Palette.Raised:BackColor));
            e.Graphics.Clear(fill);Color ink=Enabled?(Accent?Color.FromArgb(20,20,20):ForeColor):Palette.Muted;
            if(Navigation&&Selected)using(var b=new SolidBrush(Palette.Accent))e.Graphics.FillRectangle(b,0,7,3,Height-14);
            if(Symbol!="")DrawSymbol(e.Graphics,Symbol,ClientRectangle,ink);
            else TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(Navigation?16:4,0,Width-(Navigation?20:8),Height),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|(Navigation?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter));
        }
        static void DrawSymbol(Graphics g,string s,Rectangle r,Color c){int x=r.Width/2,y=r.Height/2;using(var b=new SolidBrush(c))using(var p=new Pen(c,2)){
            if(s=="play")g.FillPolygon(b,new[]{new Point(x-4,y-7),new Point(x-4,y+7),new Point(x+7,y)});
            else if(s=="pause"){g.FillRectangle(b,x-5,y-7,3,14);g.FillRectangle(b,x+2,y-7,3,14);}
            else if(s=="stop")g.FillRectangle(b,x-5,y-5,10,10);
            else {int d=s=="next"?1:-1;g.FillPolygon(b,new[]{new Point(x-5*d,y-6),new Point(x-5*d,y+6),new Point(x+4*d,y)});g.DrawLine(p,x+6*d,y-6,x+6*d,y+6);}
        }}
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);Invalidate();}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
        protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);if(Symbol=="pause"||Symbol=="play")Symbol=Text=="Продолжить"?"play":"pause";AccessibleName=Text;Invalidate();}
    }
    internal sealed class Timeline : Control {
        int val,max=1000;bool dragging;
        public int Maximum{get{return max;}set{max=Math.Max(1,value);Invalidate();}}
        public int Value{get{return val;}set{int v=Math.Max(0,Math.Min(max,value));if(v!=val){val=v;Invalidate();}}}
        public Timeline(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);Height=18;Cursor=Cursors.Hand;AccessibleName="Позиция воспроизведения";TabStop=true;}
        protected override void OnPaint(PaintEventArgs e){int y=Height/2,x=6+(int)((Width-12)*(val/(double)max));using(var p=new Pen(Palette.Line,3))e.Graphics.DrawLine(p,6,y,Width-6,y);if(Enabled)using(var p=new Pen(Palette.Accent,3))e.Graphics.DrawLine(p,6,y,x,y);if(Enabled&&(dragging||Focused))using(var b=new SolidBrush(Palette.Accent))e.Graphics.FillEllipse(b,x-4,y-4,8,8);}
        void Position(int x){Value=(int)(max*Math.Max(0,Math.Min(1,(x-6)/(double)Math.Max(1,Width-12))));}
        protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left&&Enabled){dragging=true;Capture=true;Focus();Position(e.X);}base.OnMouseDown(e);}
        protected override void OnMouseMove(MouseEventArgs e){if(dragging)Position(e.X);base.OnMouseMove(e);}
        protected override void OnMouseUp(MouseEventArgs e){if(dragging){Position(e.X);dragging=false;Capture=false;}base.OnMouseUp(e);Invalidate();}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Left||e.KeyCode==Keys.Right){Value+=e.KeyCode==Keys.Right?10:-10;e.Handled=true;OnMouseUp(new MouseEventArgs(MouseButtons.None,0,0,0,0));}base.OnKeyDown(e);}
    }
}
