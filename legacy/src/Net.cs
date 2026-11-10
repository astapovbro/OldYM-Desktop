using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Store;
using Org.BouncyCastle.Pkix;
using Org.BouncyCastle.Utilities.Collections;

namespace OldYM {
public interface ITransport { string Text(string url, string token, string form); }
public sealed class Net : ITransport {
    readonly IList roots;
    public Net(string pem) { roots = new X509CertificateParser().ReadCertificates(File.ReadAllBytes(pem)) as IList; if(roots == null || roots.Count == 0) throw new IOException("Нет корневых сертификатов"); }
    public static bool MatchHost(string host, string pattern) {
        host=host.ToLowerInvariant(); pattern=pattern.ToLowerInvariant();
        if(host==pattern)return true;
        if(!pattern.StartsWith("*."))return false;
        string suffix=pattern.Substring(1);
        return host.EndsWith(suffix,StringComparison.Ordinal) && host.Length>suffix.Length && host.Substring(0,host.Length-suffix.Length).IndexOf('.')<0;
    }
    internal void Verify(string host, Org.BouncyCastle.Tls.Certificate chain) {
        if(chain.IsEmpty)throw new IOException("Сервер не прислал сертификат");
        ArrayList certs=new ArrayList();var parser=new X509CertificateParser();
        for(int i=0;i<chain.Length;i++){var c=parser.ReadCertificate(chain.GetCertificateAt(i).GetEncoded());c.CheckValidity();certs.Add(c);}
        X509Certificate leaf=(X509Certificate)certs[0];bool matches=false;IPAddress ip;
        var names=leaf.GetSubjectAlternativeNames();
        if(names!=null)foreach(IList entry in names){int type=Convert.ToInt32(entry[0]);if(IPAddress.TryParse(host,out ip)){IPAddress other;if(type==7 && IPAddress.TryParse(Convert.ToString(entry[1]),out other)&&ip.Equals(other))matches=true;}else if(type==2&&MatchHost(host,Convert.ToString(entry[1])))matches=true;}
        if(!matches)throw new IOException("Имя сервера не совпало с сертификатом");
        var eku=leaf.GetExtendedKeyUsage();if(eku!=null&&!eku.Contains("1.3.6.1.5.5.7.3.1")&&!eku.Contains("2.5.29.37.0"))throw new IOException("Сертификат не предназначен для HTTPS");
        var usage=leaf.GetKeyUsage();if(usage!=null&&!usage[0])throw new IOException("Ключ не предназначен для подписи TLS");
        var anchors=new Org.BouncyCastle.Utilities.Collections.HashSet();foreach(X509Certificate root in roots)anchors.Add(new TrustAnchor(root,null));
        var selector=new X509CertStoreSelector();selector.Certificate=leaf;
        var parameters=new PkixBuilderParameters(anchors,selector);parameters.IsRevocationEnabled=false;
        parameters.AddStore(X509StoreFactory.Create("Certificate/Collection",new X509CollectionStoreParameters(certs)));
        new PkixCertPathBuilder().Build(parameters);
    }
    sealed class Client : DefaultTlsClient {
        readonly Net net;readonly string host;
        public Client(Net n,string h):base(new BcTlsCrypto(new SecureRandom())){net=n;host=h;}
        protected override ProtocolVersion[] GetSupportedVersions(){return new[]{ProtocolVersion.TLSv12};}
        protected override IList GetSniServerNames(){IPAddress ip;if(IPAddress.TryParse(host,out ip))return null;return new ArrayList{new ServerName(NameType.host_name,Encoding.ASCII.GetBytes(host))};}
        public override TlsAuthentication GetAuthentication(){return new Auth(net,host);}
    }
    sealed class Auth : TlsAuthentication {
        readonly Net net;readonly string host;public Auth(Net n,string h){net=n;host=h;}
        public void NotifyServerCertificate(TlsServerCertificate certificate){net.Verify(host,certificate.Certificate);}
        public TlsCredentials GetClientCredentials(CertificateRequest request){return null;}
    }
    public sealed class Response : IDisposable {
        public int Code; public Stream Body; public Dictionary<string,string> Headers;
        internal TcpClient Socket;internal TlsClientProtocol Tls;
        public void Dispose(){try{Body.Dispose();}catch{}try{Socket.Close();}catch{}}
    }
    public Response Open(string address,string token,string form){return Open(address,token,form,0);}
    Response Open(string address,string token,string form,int redirects){
        Uri uri; if(!Uri.TryCreate(address,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.UserInfo!=""||uri.Fragment!="")throw new IOException("Разрешены только HTTPS-адреса");
        if(token!=null&&!System.Text.RegularExpressions.Regex.IsMatch(token,"\\A[A-Za-z0-9_.-]+\\z"))throw new IOException("Неверный формат токена");
        TcpClient socket=new TcpClient();
        try {
            var connect=socket.BeginConnect(uri.DnsSafeHost,uri.Port,null,null);
            using(var wait=connect.AsyncWaitHandle){if(!wait.WaitOne(15000))throw new IOException("Таймаут подключения");socket.EndConnect(connect);}
            socket.ReceiveTimeout=30000;socket.SendTimeout=30000;
            var tls=new TlsClientProtocol(socket.GetStream());tls.Connect(new Client(this,uri.DnsSafeHost));
            Stream stream=tls.Stream;
            byte[] payload=form==null?new byte[0]:Encoding.UTF8.GetBytes(form);
            StringBuilder request=new StringBuilder(form==null?"GET ":"POST ");
            request.Append(uri.PathAndQuery).Append(" HTTP/1.1\r\nHost: ").Append(uri.Authority).Append("\r\nUser-Agent: OldYM-Desktop/0.1\r\nAccept: */*\r\nAccept-Encoding: identity\r\nConnection: close\r\n");
            if(!String.IsNullOrEmpty(token)&&uri.DnsSafeHost.Equals("api.music.yandex.net",StringComparison.OrdinalIgnoreCase))request.Append("Authorization: OAuth ").Append(token).Append("\r\n");
            if(form!=null)request.Append("Content-Type: application/x-www-form-urlencoded\r\nContent-Length: ").Append(payload.Length).Append("\r\n");
            request.Append("\r\n");byte[] header=Encoding.ASCII.GetBytes(request.ToString());stream.Write(header,0,header.Length);stream.Write(payload,0,payload.Length);stream.Flush();
            Stream input=new BufferedStream(stream,16384);string line=Line(input);int code;
            if(line==null||!line.StartsWith("HTTP/1.")||line.Split(' ').Length<2||!Int32.TryParse(line.Split(' ')[1],out code))throw new IOException("Нет ответа HTTP");
            var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            for(int count=0;;count++){if(count>=100)throw new IOException("Слишком много заголовков");line=Line(input);if(line==null)throw new EndOfStreamException();if(line=="")break;int colon=line.IndexOf(':');if(colon<=0)throw new IOException("Неверный заголовок HTTP");string key=line.Substring(0,colon).Trim();if(headers.ContainsKey(key)&&(key.Equals("content-length",StringComparison.OrdinalIgnoreCase)||key.Equals("transfer-encoding",StringComparison.OrdinalIgnoreCase)))throw new IOException("Повторный заголовок длины");headers[key]=line.Substring(colon+1).Trim();}
            if(code==301||code==302||code==303||code==307||code==308){if(redirects>=4||!headers.ContainsKey("Location"))throw new IOException("Ошибка перенаправления");Uri next=new Uri(uri,headers["Location"]);socket.Close();return Open(next.AbsoluteUri,token,code==307||code==308?form:null,redirects+1);}
            Stream body=input;string value;
            if(headers.TryGetValue("Transfer-Encoding",out value)){if(!value.Equals("chunked",StringComparison.OrdinalIgnoreCase))throw new IOException("Неподдерживаемое кодирование HTTP");body=new Chunked(input);}
            else if(headers.TryGetValue("Content-Length",out value)){long size;if(!Int64.TryParse(value,out size)||size<0)throw new IOException("Неверная длина HTTP");body=new Fixed(input,size);}
            if(headers.TryGetValue("Content-Encoding",out value)&&value!="identity"){if(value.Equals("gzip",StringComparison.OrdinalIgnoreCase))body=new GZipStream(body,CompressionMode.Decompress);else throw new IOException("Неподдерживаемое сжатие HTTP");}
            return new Response{Code=code,Body=body,Socket=socket,Tls=tls,Headers=headers};
        }catch{socket.Close();throw;}
    }
    public static string Line(Stream input){StringBuilder b=new StringBuilder();int v;while((v=input.ReadByte())!=-1){if(v==10)break;if(v!=13)b.Append((char)v);if(b.Length>16384)throw new IOException("Слишком длинная строка HTTP");}return v<0&&b.Length==0?null:b.ToString();}
    public static void Copy(Stream source,Stream target,long limit,Func<bool> cancelled){byte[] buffer=new byte[16384];long total=0;int n;while((n=source.Read(buffer,0,buffer.Length))>0){if(cancelled!=null&&cancelled())throw new OperationCanceledException();total+=n;if(total>limit)throw new IOException("Ответ превысил допустимый размер");target.Write(buffer,0,n);}}
    public byte[] Bytes(string url,string token,string form,int limit){using(var r=Open(url,token,form)){using(var b=new MemoryStream()){Copy(r.Body,b,limit,null);if(r.Code<200||r.Code>=300)throw new IOException("Сервис: HTTP "+r.Code);return b.ToArray();}}}
    public string Text(string url,string token,string form){return Encoding.UTF8.GetString(Bytes(url,token,form,4*1024*1024));}
    public void Download(string url,string target,Func<bool> cancelled){string part=target+"."+Guid.NewGuid().ToString("N")+".part";try{using(var r=Open(url,null,null)){if(r.Code!=200)throw new IOException("Аудио: HTTP "+r.Code);using(var f=File.Create(part))Copy(r.Body,f,80L*1024*1024,cancelled);}if(cancelled!=null&&cancelled())throw new OperationCanceledException();if(!File.Exists(target))File.Move(part,target);}finally{if(File.Exists(part))File.Delete(part);}}
    public string OAuth(string path,string form){using(var r=Open("https://oauth.yandex.ru"+path,null,form)){using(var b=new MemoryStream()){Copy(r.Body,b,65536,null);if(r.Code>=500)throw new IOException("Авторизация: HTTP "+r.Code);return Encoding.UTF8.GetString(b.ToArray());}}}
    internal abstract class ReadOnly : Stream {protected Stream input;protected ReadOnly(Stream s){input=s;}public override bool CanRead{get{return true;}}public override bool CanSeek{get{return false;}}public override bool CanWrite{get{return false;}}public override long Length{get{throw new NotSupportedException();}}public override long Position{get{throw new NotSupportedException();}set{throw new NotSupportedException();}}public override void Flush(){}public override long Seek(long a,SeekOrigin b){throw new NotSupportedException();}public override void SetLength(long n){throw new NotSupportedException();}public override void Write(byte[] b,int o,int n){throw new NotSupportedException();}protected override void Dispose(bool d){if(d)input.Dispose();base.Dispose(d);}}
    internal sealed class Fixed : ReadOnly {long remaining;public Fixed(Stream s,long n):base(s){remaining=n;}public override int Read(byte[] b,int o,int n){if(n==0||remaining==0)return 0;int read=input.Read(b,o,(int)Math.Min(n,remaining));if(read==0)throw new EndOfStreamException("Оборван ответ HTTP");remaining-=read;return read;}}
    internal sealed class Chunked : ReadOnly {long remaining;bool first=true,ended;public Chunked(Stream s):base(s){}public override int Read(byte[] b,int o,int n){if(n==0||ended)return 0;if(remaining==0){if(!first&&Line(input)!="")throw new IOException("Ошибка HTTP chunk");first=false;string size=Line(input);if(size==null)throw new EndOfStreamException();int semi=size.IndexOf(';');if(semi>=0)size=size.Substring(0,semi);if(!Int64.TryParse(size,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out remaining)||remaining<0)throw new IOException("Неверный размер chunk");if(remaining==0){ended=true;return 0;}}int read=input.Read(b,o,(int)Math.Min(n,remaining));if(read==0)throw new EndOfStreamException("Оборван HTTP chunk");remaining-=read;return read;}}
}
}
