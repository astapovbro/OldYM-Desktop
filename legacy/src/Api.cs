using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Security.Cryptography;
using System.Xml;
namespace OldYM {
public static class Json {
    public static object Parse(string text){return new JavaScriptSerializer{MaxJsonLength=4*1024*1024,RecursionLimit=64}.DeserializeObject(text);}
    public static Dictionary<string,object> Obj(object value){var d=value as Dictionary<string,object>;if(d==null)throw new IOException("Сервис прислал неожиданный JSON");return d;}
    public static object Get(object obj,string key){object v;return Obj(obj).TryGetValue(key,out v)?v:null;}
    public static string Str(object obj,string key,string fallback){object v=Get(obj,key);return v==null?fallback:Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture);}
    public static int Int(object obj,string key,int fallback){int v;return Int32.TryParse(Str(obj,key,""),out v)?v:fallback;}
    public static bool Bool(object obj,string key){return Object.Equals(Get(obj,key),true);}
    public static object[] Arr(object value){if(value==null)return new object[0];var arr=value as object[];if(arr==null)throw new IOException("Ожидался список JSON");return arr;}
}
public sealed class Track {
    public string Id,Title,Artist,Cover,Album,LocalPath;public int Duration;
    public Track(object data){string wrapperAlbumId=Json.Str(data,"albumId","");object full=Json.Get(data,"track");if(full!=null)data=full;Id=Json.Str(data,"id","");if(Id.Length==0)throw new IOException("Нет идентификатора трека");Title=Json.Str(data,"title","Без названия");Duration=Json.Int(data,"durationMs",0);Cover=Json.Str(data,"coverUri","");List<string> names=new List<string>();foreach(object a in Json.Arr(Json.Get(data,"artists")))names.Add(Json.Str(a,"name",""));Artist=names.Count==0?"Неизвестный исполнитель":String.Join(", ",names.ToArray());var albums=Json.Arr(Json.Get(data,"albums"));Album=String.IsNullOrEmpty(wrapperAlbumId)?(albums.Length==0?"":Json.Str(albums[0],"id","")):wrapperAlbumId;if(String.IsNullOrEmpty(Cover)&&albums.Length>0)Cover=Json.Str(albums[0],"coverUri","");LocalPath="";}
    public Track(string id,string title,string artist,int duration,string cover,string album,string localPath){Id=id??"";Title=String.IsNullOrEmpty(title)?"Без названия":title;Artist=String.IsNullOrEmpty(artist)?"Неизвестный исполнитель":artist;Duration=Math.Max(0,duration);Cover=cover??"";Album=album??"";LocalPath=localPath??"";}
    public string Key{get{return System.Text.RegularExpressions.Regex.Replace(Id,"[^A-Za-z0-9_-]","_");}}
    public string CoverUrl(int size){if(String.IsNullOrEmpty(Cover))return null;string s=Cover.Replace("%%",size+"x"+size);if(s.StartsWith("//"))return "https:"+s;if(s.StartsWith("http://"))return "https://"+s.Substring(7);return s.StartsWith("https://")?s:"https://"+s;}
    public string Time{get{return Duration>0?String.Format("{0}:{1:00}",Duration/60000,(Duration/1000)%60):"--:--";}}
    public override string ToString(){return Artist+" — "+Title;}
}
public sealed class Playlist {
    public string Kind,Owner,Title,Revision;
    public bool IsOwned;
    public Playlist(object data,string uid){Kind=Json.Str(data,"kind","");Title=Json.Str(data,"title","Плейлист");Revision=Json.Str(data,"revision","");var owner=Json.Get(data,"owner");Owner=owner==null?uid:Json.Str(owner,"uid",uid);IsOwned=String.Equals(Owner,uid,StringComparison.Ordinal); }
    public override string ToString(){return Title;}
}
public sealed class Api {
    public const string Base="https://api.music.yandex.net";
    readonly ITransport net;readonly string token;
    public string Uid,Login;
    public Api(ITransport n,string t){net=n;token=t;}
    public static string Enc(string value){return Uri.EscapeDataString(value);}
    public object Call(string path,string form){var data=Json.Parse(net.Text(Base+path,token,form));var error=Json.Get(data,"error");if(error!=null)throw new IOException("Яндекс: "+Json.Str(error,"message","Нет доступа"));object result=Json.Get(data,"result");if(result==null)throw new IOException("Пустой результат API");return result;}
    void Mutate(string path,string form){var data=Json.Parse(net.Text(Base+path,token,form));var error=Json.Get(data,"error");if(error!=null)throw new IOException("Яндекс: "+Json.Str(error,"message","Нет доступа"));if(Object.Equals(Json.Get(data,"result"),false))throw new IOException("Яндекс не подтвердил изменение");}
    public void Account(){object s=Call("/account/status",null);object account=Json.Get(s,"account");if(account==null)throw new IOException("Аккаунт не найден");Uid=Json.Str(account,"uid","");Login=Json.Str(account,"displayName",Json.Str(account,"login","Яндекс"));if(Uid=="")throw new IOException("Нет UID аккаунта");}
    public static List<Track> Tracks(object list){List<Track> tracks=new List<Track>();foreach(object o in Json.Arr(list))tracks.Add(new Track(o));return tracks;}
    public List<Track> Search(string text,int page){object result=Call("/search?type=track&page="+page+"&text="+Enc(text),null);var tracks=Json.Get(result,"tracks");return tracks==null?new List<Track>():Tracks(Json.Get(tracks,"results"));}
    public List<string> LikedIds(){object result=Call("/users/"+Enc(Uid)+"/likes/tracks",null);var library=Json.Get(result,"library");var ids=new List<string>();foreach(object o in Json.Arr(Json.Get(library,"tracks")))ids.Add(Json.Str(o,"id",""));return ids;}
    public List<Track> Details(List<string> ids,int start,int count){var selected=ids.GetRange(start,Math.Min(count,ids.Count-start));if(selected.Count==0)return new List<Track>();return Tracks(Call("/tracks","track-ids="+Enc(String.Join(",",selected.ToArray()))));}
    public List<Playlist> Playlists(){var list=new List<Playlist>();var seen=new HashSet<string>();AddPlaylists(list,seen,Call("/users/"+Enc(Uid)+"/playlists/list",null));AddPlaylists(list,seen,Call("/users/"+Enc(Uid)+"/likes/playlists",null));return list;}
    void AddPlaylists(List<Playlist> list,HashSet<string> seen,object data){foreach(object entry in Json.Arr(data)){object raw=Json.Get(entry,"playlist")??entry;var p=new Playlist(raw,Uid);if(p.Kind.Length>0&&seen.Add(p.Owner+":"+p.Kind))list.Add(p);}}
    public List<Track> PlaylistTracks(Playlist playlist){var result=Call("/users/"+Enc(playlist.Owner)+"/playlists/"+Enc(playlist.Kind),null);playlist.Revision=Json.Str(result,"revision",playlist.Revision);if(String.IsNullOrEmpty(playlist.Revision))throw new IOException("Яндекс не прислал revision плейлиста");var a=Json.Arr(Json.Get(result,"tracks"));bool complete=true;var ids=new List<string>();foreach(object o in a){ids.Add(Json.Str(o,"id",""));if(Json.Get(o,"track")==null)complete=false;}if(complete)return Tracks(a);var list=new List<Track>();for(int i=0;i<ids.Count;i+=50)list.AddRange(Details(ids,i,50));return list;}
    public List<Track> Wave(string last){var result=Call("/rotor/station/user:onyourwave/tracks?settings2=true"+(String.IsNullOrEmpty(last)?"":"&queue="+Enc(last)),null);var a=Json.Get(result,"sequence")??Json.Get(result,"tracks");if(a==null)throw new IOException("Сервис не прислал очередь волны");var list=Tracks(a);if(list.Count==0)throw new IOException("Нет новых треков волны");return list;}
    // Fill the first view with a modest number of recommendations. Only Track metadata
    // is retained here; audio remains streamed/downloaded on demand by the player.
    public List<Track> WavePreview(int targetCount)
    {
        if (targetCount < 15) targetCount = 15;
        if (targetCount > 20) targetCount = 20;
        List<Track> collected = new List<Track>();
        HashSet<string> seen = new HashSet<string>();
        string cursor = null;
        for (int request = 0; request < 8 && collected.Count < targetCount; request++)
        {
            List<Track> batch;
            try { batch = Wave(cursor); }
            catch (IOException) { if (collected.Count > 0) break; throw; }
            if (batch == null || batch.Count == 0) break;
            int before = collected.Count;
            foreach (Track track in batch)
            {
                if (track != null && !String.IsNullOrEmpty(track.Id) && seen.Add(track.Id))
                {
                    collected.Add(track);
                    if (collected.Count >= targetCount) break;
                }
            }
            string nextCursor = batch[batch.Count - 1].Id;
            if (collected.Count == before || String.IsNullOrEmpty(nextCursor) ||
                String.Equals(cursor, nextCursor, StringComparison.Ordinal)) break;
            cursor = nextCursor;
        }
        if (collected.Count == 0) throw new IOException("Не удалось загрузить рекомендации для моей волны");
        return collected;
    }
    public void Like(Track t){Mutate("/users/"+Enc(Uid)+"/likes/tracks/add-multiple","track-ids="+Enc(t.Id));}
    public void Unlike(Track t){Mutate("/users/"+Enc(Uid)+"/likes/tracks/remove","track-ids="+Enc(t.Id));}
    public void CreatePlaylist(string title){if(String.IsNullOrWhiteSpace(title))throw new ArgumentException("Укажи название плейлиста");Mutate("/users/"+Enc(Uid)+"/playlists/create","title="+Enc(title.Trim())+"&visibility=private");}
    public void RenamePlaylist(Playlist playlist,string title){EnsureOwned(playlist);if(String.IsNullOrWhiteSpace(title))throw new ArgumentException("Укажи название плейлиста");Mutate("/users/"+Enc(playlist.Owner)+"/playlists/"+Enc(playlist.Kind)+"/name","value="+Enc(title.Trim()));playlist.Title=title.Trim();}
    public void DeletePlaylist(Playlist playlist){EnsureOwned(playlist);Mutate("/users/"+Enc(playlist.Owner)+"/playlists/"+Enc(playlist.Kind)+"/delete","");}
    public void AddTrackToPlaylist(Playlist playlist,Track track){EnsureOwned(playlist);List<Track> current=PlaylistTracks(playlist);Track full=ResolveAlbum(track);string diff="{\"op\":\"insert\",\"at\":"+current.Count+",\"tracks\":[{\"id\":\""+JsonEscape(full.Id)+"\",\"albumId\":\""+JsonEscape(full.Album)+"\"}]}";Mutate("/users/"+Enc(playlist.Owner)+"/playlists/"+Enc(playlist.Kind)+"/change-relative","diff="+Enc(diff)+"&revision="+Enc(playlist.Revision));}
    public void RemoveTrackFromPlaylist(Playlist playlist,int position){EnsureOwned(playlist);List<Track> current=PlaylistTracks(playlist);if(position<0||position>=current.Count)throw new ArgumentOutOfRangeException("position");Track full=ResolveAlbum(current[position]);string diff="{\"op\":\"delete\",\"at\":"+position+",\"tracks\":[{\"id\":\""+JsonEscape(full.Id)+"\",\"albumId\":\""+JsonEscape(full.Album)+"\"}]}";Mutate("/users/"+Enc(playlist.Owner)+"/playlists/"+Enc(playlist.Kind)+"/change-relative","diff="+Enc(diff)+"&revision="+Enc(playlist.Revision));}
    Track ResolveAlbum(Track track){if(!String.IsNullOrEmpty(track.Album))return track;List<Track> found=Details(new List<string>{track.Id},0,1);if(found.Count==0||String.IsNullOrEmpty(found[0].Album))throw new IOException("Не удалось определить альбом трека для плейлиста");return found[0];}
    static string JsonEscape(string value){return (value??"").Replace("\\","\\\\").Replace("\"","\\\"");}
    void EnsureOwned(Playlist playlist){if(playlist==null||!String.Equals(playlist.Owner,Uid,StringComparison.Ordinal))throw new IOException("Можно изменять только собственные плейлисты");}
    public string Audio(Track track){object best=null;int bitrate=0;foreach(object option in Json.Arr(Call("/tracks/"+Enc(track.Id)+"/download-info",null))){if(Json.Str(option,"codec","")!="mp3"||Json.Bool(option,"preview"))continue;int n=Json.Int(option,"bitrateInKbps",128);if(best==null||(n<=128&&(bitrate>128||n>bitrate))){best=option;bitrate=n;}}
        if(best==null)throw new IOException("Полный MP3 недоступен аккаунту или треку");string address=Json.Str(best,"downloadInfoUrl","");if(address.StartsWith("http://"))address="https://"+address.Substring(7);if(Json.Bool(best,"direct"))return address;
        string xml=net.Text(address,null,null);var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536};var doc=new XmlDocument();doc.XmlResolver=null;using(var r=XmlReader.Create(new StringReader(xml),settings))doc.Load(r);
        string host=Tag(doc,"host"),path=Tag(doc,"path"),ts=Tag(doc,"ts"),salt=Tag(doc,"s");if(!System.Text.RegularExpressions.Regex.IsMatch(host,"\\A[A-Za-z0-9.-]+\\z")||!path.StartsWith("/"))throw new IOException("Неверный адрес аудио");
        string sign;using(var md5=MD5.Create())sign=BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes("XGRlBW9FXlekgbPrRHuSiA"+path.Substring(1)+salt))).Replace("-","").ToLowerInvariant();return "https://"+host+"/get-mp3/"+sign+"/"+ts+path;
    }
    static string Tag(XmlDocument doc,string name){var n=doc.GetElementsByTagName(name);if(n.Count==0)throw new IOException("Нет поля "+name);return n[0].InnerText;}
}
public sealed class DeviceAuth {
    const string Client="23cabbbdc6cd418abb4b39c32c41195d", Secret="53bc75238f0c4d08a118e51fe9203300";
    readonly Net net;public DeviceAuth(Net n){net=n;}
    public object Request(){var d=Json.Parse(net.OAuth("/device/code","client_id="+Client+"&device_id="+Guid.NewGuid().ToString("N")+"&device_name="+Api.Enc("OldYM Desktop Legacy")));if(Json.Get(d,"error")!=null)throw new IOException("Не удалось получить код: "+Json.Str(d,"error",""));return d;}
    public object Poll(string code){return Json.Parse(net.OAuth("/token","grant_type=device_code&code="+Api.Enc(code)+"&client_id="+Client+"&client_secret="+Secret));}
    public static string VerificationUrl(object code){string url=Json.Str(code,"verification_url",Json.Str(code,"verification_uri",""));Uri u;if(!Uri.TryCreate(url,UriKind.Absolute,out u)||u.Scheme!="https"||u.UserInfo!=""||!(u.Host=="ya.ru"||u.Host=="yandex.ru"||u.Host.EndsWith(".yandex.ru",StringComparison.Ordinal)))throw new IOException("Неизвестная страница подтверждения");return u.AbsoluteUri;}
}
}
