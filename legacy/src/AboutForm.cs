using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
namespace OldYM {
/** Тёмное окно «О программе»: иконка слева, текст справа, кнопка ОК под текстом. */
public sealed class AboutForm : Form {
    public AboutForm(){
        Text="О программе";
        FormBorderStyle=FormBorderStyle.FixedDialog;
        StartPosition=FormStartPosition.CenterParent;
        ShowInTaskbar=false;MinimizeBox=false;MaximizeBox=false;
        ClientSize=new Size(540,300);
        BackColor=Palette.Background;ForeColor=Palette.Text;
        Font=new Font("Tahoma",9);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Padding=new Padding(20,20,20,16),Margin=Padding.Empty,BackColor=Palette.Background};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,96));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
        Controls.Add(root);

        var icon=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(0,2,16,2),BackColor=Palette.Background};
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,Path.Combine("assets","oldym.ico"));
        if(File.Exists(path)){try{using(var source=new Icon(path))icon.Image=source.ToBitmap();}catch(Exception){icon.Image=null;}}
        if(icon.Image==null)icon.Paint+=delegate(object sender,PaintEventArgs e){
            var box=new Rectangle(0,0,72,72);
            using(var fill=new SolidBrush(Palette.Accent))e.Graphics.FillRectangle(fill,box);
            using(var ink=new SolidBrush(Color.FromArgb(20,20,20)))
                e.Graphics.DrawString("YM",new Font("Tahoma",20,FontStyle.Bold),ink,new PointF(14,20));
        };
        root.Controls.Add(icon,0,0);

        var text=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty,BackColor=Palette.Background};
        text.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        text.RowStyles.Add(new RowStyle(SizeType.Absolute,30));
        text.RowStyles.Add(new RowStyle(SizeType.Absolute,22));
        text.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var title=new Label{Text="OldYM Desktop Legacy",Dock=DockStyle.Fill,ForeColor=Palette.Text,Font=new Font("Tahoma",14,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty};
        var version=new Label{Text="1.0 · неофициальный клиент Яндекс Музыки",Dock=DockStyle.Fill,ForeColor=Palette.Accent,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty,AutoEllipsis=true};
        var body=new Label{Dock=DockStyle.Fill,ForeColor=Palette.Muted,TextAlign=ContentAlignment.TopLeft,Margin=new Padding(0,6,0,0),AutoSize=false,
            Text="Поддержка Windows от XP SP3 и выше.\r\n.NET Framework 4 Full, x86.\r\n\r\nHTTPS: Bouncy Castle, TLS 1.2, распространяется по лицензии LGPL-v3.0-or-later.\r\nMP3 сначала скачивается на диск, затем воспроизводится через winmm.\r\n\r\nАвтор клиента: Astapov_Bro"};
        text.Controls.Add(title,0,0);text.Controls.Add(version,0,1);text.Controls.Add(body,0,2);
        root.Controls.Add(text,1,0);

        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty,Padding=new Padding(0,10,0,0),BackColor=Palette.Background};
        var ok=new MusicButton{Text="ОК",Accent=true,Size=new Size(110,34),Margin=Padding.Empty,DialogResult=DialogResult.OK,BackColor=Palette.Background,ForeColor=Palette.Text};
        footer.Controls.Add(ok);
        root.Controls.Add(footer,1,1);
        AcceptButton=ok;CancelButton=ok;
    }
}
}
