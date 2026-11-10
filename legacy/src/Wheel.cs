using System;
using System.Drawing;
using System.Windows.Forms;

namespace OldYM {
    /// Прокрутка колёсиком, даже когда список не в фокусе:
    /// сообщение перехватывается до доставки и уходит списку под курсором.
    public sealed class WheelFilter : IMessageFilter {
        const int WM_MOUSEWHEEL = 0x020A;
        readonly Form form;
        public WheelFilter(Form owner){form=owner;}
        public bool PreFilterMessage(ref Message m){
            if(m.Msg!=WM_MOUSEWHEEL)return false;
            if(form==null||form.IsDisposed||!form.IsHandleCreated)return false;
            long packed=m.LParam.ToInt64();
            Point screen=new Point((short)(packed&0xFFFF),(short)((packed>>16)&0xFFFF));
            ListView target=Find(form,form.PointToClient(screen));
            if(target==null)return false;
            int delta=(short)((m.WParam.ToInt64()>>16)&0xFFFF);
            return Wheel.Scroll(target,delta);
        }
        static ListView Find(Control root,Point point){
            Control hit=root.GetChildAtPoint(point,GetChildAtPointSkip.Invisible);
            for(Control c=hit;c!=null;c=c.Parent)if(c is ListView)return (ListView)c;
            return null;
        }
    }

    public static class Wheel {
        public static bool Scroll(ListView list,int delta){
            if(list==null||delta==0||!list.IsHandleCreated)return false;
            if(list.Items.Count==0)return false;
            int top=list.TopItem!=null?list.TopItem.Index:0;
            int step=delta>0?-3:3;
            int target=top+step;
            if(target<0)target=0;
            if(target>list.Items.Count-1)target=list.Items.Count-1;
            try{
                list.TopItem=list.Items[target];
            }catch(Exception){
                try{list.EnsureVisible(target);}catch(Exception){}
            }
            return true;
        }
    }
}
