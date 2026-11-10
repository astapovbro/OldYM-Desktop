using System;
using System.IO;
using System.Windows.Forms;
using System.Reflection;
[assembly: AssemblyTitle("OldYM Desktop Legacy")]
[assembly: AssemblyVersion("0.2.1.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.0")]
namespace OldYM {
static class Program {
    [STAThread] static void Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);try{string home=AppDomain.CurrentDomain.BaseDirectory;var net=new Net(Path.Combine(home,"assets/roots.pem"));string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OldYMDesktopLegacy");var storage=new Storage(data);Application.Run(new MainForm(net,storage,Array.IndexOf(args,"--demo")>=0));}catch(Exception e){MessageBox.Show(e.Message,"OldYM — ошибка запуска",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
}
}
