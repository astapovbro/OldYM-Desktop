using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace OldYM
{
    public sealed class Storage
    {
        public readonly string Root;
        public readonly string Cache;
        public readonly string Downloads;
        readonly object cacheLock = new object();

        public Storage(string root)
        {
            Root = root;
            Cache = Path.Combine(root, "cache");
            Downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "OldYM");
            Directory.CreateDirectory(Cache);
            Directory.CreateDirectory(Downloads);
        }

        public string ReadToken()
        {
            string path = Path.Combine(Root, "account.dat");
            if (!File.Exists(path)) return "";
            byte[] protectedBytes = File.ReadAllBytes(path);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
        }

        public void SaveToken(string token)
        {
            if (String.IsNullOrEmpty(token)) throw new ArgumentException("Пустой токен", "token");
            byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
            string path = Path.Combine(Root, "account.dat");
            string temporary = path + ".new";
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public void Forget()
        {
            string path = Path.Combine(Root, "account.dat");
            if (File.Exists(path)) File.Delete(path);
        }

        public string AudioPath(Track track)
        {
            return Path.Combine(Cache, track.Key + ".mp3");
        }

        public string DownloadPath(Track track)
        {
            if (!String.IsNullOrEmpty(track.LocalPath)) return track.LocalPath;
            string name = SafeFileName(track.Artist + " - " + track.Title);
            if (name.Length > 110) name = name.Substring(0, 110).Trim();
            return Path.Combine(Downloads, name + " [" + track.Key + "].mp3");
        }

        public bool IsDownloaded(Track track)
        {
            return track != null && File.Exists(DownloadPath(track));
        }

        public void SaveDownloadInfo(Track track, string audioPath)
        {
            if (track == null || String.IsNullOrEmpty(audioPath)) return;
            // Metadata is embedded in the MP3 as ID3v2 frames. Remove legacy sidecar only
            // after the MP3 has been rewritten successfully.
            if (!Mp3Id3.TryWrite(audioPath, track)) return;
            string legacyInfo = audioPath + ".oldym";
            try
            {
                if (File.Exists(legacyInfo))
                {
                    File.SetAttributes(legacyInfo, FileAttributes.Normal);
                    File.Delete(legacyInfo);
                }
            }
            catch { }
        }

        public List<Track> DownloadedTracks()
        {
            List<Track> result = new List<Track>();
            if (!Directory.Exists(Downloads)) return result;
            string[] files = Directory.GetFiles(Downloads, "*.mp3");
            Array.Sort(files, delegate(string a, string b)
            {
                return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
            });
            foreach (string path in files)
            {
                try
                {
                    string id, title, artist, cover = "", album = "";
                    int duration = 0;
                    if (TryReadDownloadInfo(path, out id, out title, out artist, out duration, out cover, out album))
                    {
                        // One-time migration: old .oldym sidecars are moved into ID3 tags.
                        SaveDownloadInfo(new Track(id, title, artist, duration, cover, album, path), path);
                    }
                    else
                    {
                        string embeddedId, embeddedTitle, embeddedArtist, embeddedCover, embeddedAlbum;
                        int embeddedDuration;
                        ParseDownloadName(path, out id, out title, out artist);
                        if (Mp3Id3.TryRead(path, out embeddedId, out embeddedTitle, out embeddedArtist,
                                          out embeddedDuration, out embeddedCover, out embeddedAlbum))
                        {
                            if (!String.IsNullOrEmpty(embeddedId)) id = embeddedId;
                            if (!String.IsNullOrEmpty(embeddedTitle)) title = embeddedTitle;
                            if (!String.IsNullOrEmpty(embeddedArtist)) artist = embeddedArtist;
                            if (embeddedDuration > 0) duration = embeddedDuration;
                            if (!String.IsNullOrEmpty(embeddedCover)) cover = embeddedCover;
                            if (!String.IsNullOrEmpty(embeddedAlbum)) album = embeddedAlbum;
                        }
                    }
                    result.Add(new Track(id, title, artist, duration, cover, album, path));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return result;
        }

        static string EncodeMetadata(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        static string DecodeMetadata(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
        }

        static bool TryReadDownloadInfo(string path, out string id, out string title, out string artist,
                                        out int duration, out string cover, out string album)
        {
            id = title = artist = cover = album = "";
            duration = 0;
            string infoPath = path + ".oldym";
            if (!File.Exists(infoPath)) return false;
            try
            {
                string[] lines = File.ReadAllLines(infoPath, Encoding.UTF8);
                if (lines.Length < 6) return false;
                id = DecodeMetadata(lines[0]);
                title = DecodeMetadata(lines[1]);
                artist = DecodeMetadata(lines[2]);
                Int32.TryParse(lines[3], System.Globalization.NumberStyles.Integer,
                               System.Globalization.CultureInfo.InvariantCulture, out duration);
                cover = DecodeMetadata(lines[4]);
                album = DecodeMetadata(lines[5]);
                return !String.IsNullOrEmpty(id) && !String.IsNullOrEmpty(title);
            }
            catch { return false; }
        }

        static void ParseDownloadName(string path, out string id, out string title, out string artist)
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            int suffix = stem.LastIndexOf(" [", StringComparison.Ordinal);
            id = suffix >= 0 && stem.EndsWith("]", StringComparison.Ordinal)
                ? stem.Substring(suffix + 2, stem.Length - suffix - 3) : "";
            string display = suffix >= 0 ? stem.Substring(0, suffix) : stem;
            int separator = display.IndexOf(" - ", StringComparison.Ordinal);
            if (separator >= 0)
            {
                artist = display.Substring(0, separator);
                title = display.Substring(separator + 3);
            }
            else
            {
                artist = "Неизвестный исполнитель";
                title = display;
            }
            if (String.IsNullOrEmpty(id)) id = stem;
            if (String.IsNullOrEmpty(title)) title = "Без названия";
        }

        static string SafeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid) value = value.Replace(c, '_');
            value = value.Trim().TrimEnd('.');
            return String.IsNullOrEmpty(value) ? "OldYM track" : value;
        }

        public void Trim(string keep)
        {
            lock (cacheLock)
            {
                DirectoryInfo directory = new DirectoryInfo(Cache);
                if (!directory.Exists) return;
                List<FileInfo> files = new List<FileInfo>(directory.GetFiles("*.mp3"));
                files.Sort(delegate(FileInfo a, FileInfo b) { return a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc); });
                long size = 0;
                foreach (FileInfo file in files) size += file.Length;
                foreach (FileInfo file in files)
                {
                    if (size <= 128L * 1024 * 1024) break;
                    if (String.Equals(file.FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        long length = file.Length;
                        file.Delete();
                        size -= length;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }

    internal static class Mp3Id3
    {
        public static bool TryWrite(string path, Track track)
        {
            if (String.IsNullOrEmpty(path) || track == null || !File.Exists(path)) return false;
            string temporary = path + ".id3tmp";
            try
            {
                // Replace any existing leading ID3v2 tag instead of parsing all of its
                // optional flags/frames. This also handles unsynchronised v2.3/v2.4 tags
                // which otherwise prevented metadata migration. The MPEG audio stream
                // itself is copied byte-for-byte and never loaded fully into memory.
                long audioOffset = 0;
                using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (input.Length >= 10)
                    {
                        byte[] header = new byte[10];
                        ReadFully(input, header, 0, header.Length);
                        if (header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3')
                        {
                            int majorVersion = header[3];
                            int tagSize = ReadSynchsafe(header, 6);
                            if (majorVersion < 2 || majorVersion > 4 || tagSize < 0 ||
                                input.Length < 10L + tagSize)
                                return false;
                            audioOffset = 10L + tagSize;
                        }
                    }

                    const int version = 3; // ID3v2.3 is broadly supported by desktop players.
                    List<byte[]> frames = new List<byte[]>();
                    AddTextFrame(frames, "TIT2", track.Title, version);
                    AddTextFrame(frames, "TPE1", track.Artist, version);
                    if (!String.IsNullOrEmpty(track.Album)) AddTextFrame(frames, "TALB", track.Album, version);
                    if (track.Duration > 0)
                        AddTextFrame(frames, "TLEN", track.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture), version);
                    if (!String.IsNullOrEmpty(track.Id)) AddUserTextFrame(frames, "OLDYM_ID", track.Id, version);
                    if (!String.IsNullOrEmpty(track.Cover)) AddUserTextFrame(frames, "OLDYM_COVER", track.Cover, version);

                    using (FileStream output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        int totalTagSize = 0;
                        foreach (byte[] frame in frames) totalTagSize = checked(totalTagSize + frame.Length);
                        byte[] newHeader = new byte[10];
                        newHeader[0] = (byte)'I'; newHeader[1] = (byte)'D'; newHeader[2] = (byte)'3';
                        newHeader[3] = (byte)version; newHeader[4] = 0; newHeader[5] = 0;
                        WriteSynchsafe(newHeader, 6, totalTagSize);
                        output.Write(newHeader, 0, newHeader.Length);
                        foreach (byte[] frame in frames) output.Write(frame, 0, frame.Length);
                        input.Position = audioOffset;
                        CopyStream(input, output);
                        output.Flush();
                    }
                }
                File.Replace(temporary, path, null);
                return true;
            }
            catch
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                return false;
            }
        }

        public static bool TryRead(string path, out string id, out string title, out string artist,
                                   out int duration, out string cover, out string album)
        {
            id = title = artist = cover = album = "";
            duration = 0;
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (stream.Length < 10) return false;
                    byte[] header = new byte[10];
                    ReadFully(stream, header, 0, 10);
                    if (header[0] != (byte)'I' || header[1] != (byte)'D' || header[2] != (byte)'3') return false;
                    int version = header[3];
                    if ((version != 3 && version != 4) || (header[5] & 0x80) != 0) return false;
                    int tagSize = ReadSynchsafe(header, 6);
                    if (tagSize < 0 || tagSize > 8 * 1024 * 1024 || stream.Length < 10L + tagSize) return false;
                    byte[] tag = new byte[tagSize];
                    ReadFully(stream, tag, 0, tag.Length);
                    int frameEnd = tag.Length;
                    if (version == 4 && (header[5] & 0x10) != 0) frameEnd = Math.Max(0, frameEnd - 10);
                    int position = 0;
                    if ((header[5] & 0x40) != 0)
                    {
                        if (tag.Length < 4) return false;
                        int extended = version == 3 ? ReadBigEndian(tag, 0) + 4 : ReadSynchsafe(tag, 0);
                        if (extended < 4 || extended > frameEnd) return false;
                        position = extended;
                    }
                    while (position + 10 <= frameEnd)
                    {
                        if (IsPadding(tag, position, frameEnd)) break;
                        string frameId = Encoding.ASCII.GetString(tag, position, 4);
                        if (!IsFrameId(frameId)) break;
                        int size = version == 3 ? ReadBigEndian(tag, position + 4) : ReadSynchsafe(tag, position + 4);
                        if (size < 0 || position + 10L + size > frameEnd) break;
                        if (frameId == "TIT2") title = DecodeText(tag, position + 10, size);
                        else if (frameId == "TPE1") artist = DecodeText(tag, position + 10, size);
                        else if (frameId == "TALB") album = DecodeText(tag, position + 10, size);
                        else if (frameId == "TLEN") Int32.TryParse(DecodeText(tag, position + 10, size), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out duration);
                        else if (frameId == "TXXX")
                        {
                            string description, value;
                            if (TryDecodeUserText(tag, position + 10, size, out description, out value))
                            {
                                if (String.Equals(description, "OLDYM_ID", StringComparison.OrdinalIgnoreCase)) id = value;
                                else if (String.Equals(description, "OLDYM_COVER", StringComparison.OrdinalIgnoreCase)) cover = value;
                            }
                        }
                        position += 10 + size;
                    }
                }
                return !String.IsNullOrEmpty(id) || !String.IsNullOrEmpty(title) || !String.IsNullOrEmpty(artist);
            }
            catch { return false; }
        }

        static void AddTextFrame(List<byte[]> frames, string id, string value, int version)
        {
            if (String.IsNullOrEmpty(value)) return;
            byte[] text;
            if (version == 4)
            {
                byte[] encoded = Encoding.UTF8.GetBytes(value);
                text = new byte[encoded.Length + 1]; text[0] = 3;
                Buffer.BlockCopy(encoded, 0, text, 1, encoded.Length);
            }
            else
            {
                byte[] encoded = Encoding.Unicode.GetBytes(value);
                text = new byte[encoded.Length + 3]; text[0] = 1; text[1] = 0xFF; text[2] = 0xFE;
                Buffer.BlockCopy(encoded, 0, text, 3, encoded.Length);
            }
            frames.Add(BuildFrame(id, text, version));
        }

        static void AddUserTextFrame(List<byte[]> frames, string description, string value, int version)
        {
            byte[] desc, val, payload;
            if (version == 4)
            {
                desc = Encoding.UTF8.GetBytes(description); val = Encoding.UTF8.GetBytes(value);
                payload = new byte[1 + desc.Length + 1 + val.Length]; payload[0] = 3;
                Buffer.BlockCopy(desc, 0, payload, 1, desc.Length);
                Buffer.BlockCopy(val, 0, payload, 2 + desc.Length, val.Length);
            }
            else
            {
                desc = Encoding.Unicode.GetBytes(description); val = Encoding.Unicode.GetBytes(value);
                payload = new byte[3 + desc.Length + 2 + val.Length]; payload[0] = 1; payload[1] = 0xFF; payload[2] = 0xFE;
                Buffer.BlockCopy(desc, 0, payload, 3, desc.Length);
                Buffer.BlockCopy(val, 0, payload, 5 + desc.Length, val.Length);
            }
            frames.Add(BuildFrame("TXXX", payload, version));
        }

        static byte[] BuildFrame(string id, byte[] payload, int version)
        {
            byte[] frame = new byte[10 + payload.Length];
            byte[] name = Encoding.ASCII.GetBytes(id); Buffer.BlockCopy(name, 0, frame, 0, 4);
            if (version == 4) WriteSynchsafe(frame, 4, payload.Length); else WriteBigEndian(frame, 4, payload.Length);
            Buffer.BlockCopy(payload, 0, frame, 10, payload.Length);
            return frame;
        }

        static string DecodeText(byte[] data, int offset, int count)
        {
            if (count <= 1) return "";
            int encoding = data[offset]; int start = offset + 1; int length = count - 1;
            Encoding decoder;
            if (encoding == 0) decoder = Encoding.GetEncoding(28591);
            else if (encoding == 1)
            {
                decoder = Encoding.Unicode;
                if (length >= 2 && data[start] == 0xFE && data[start + 1] == 0xFF) { decoder = Encoding.BigEndianUnicode; start += 2; length -= 2; }
                else if (length >= 2 && data[start] == 0xFF && data[start + 1] == 0xFE) { start += 2; length -= 2; }
            }
            else if (encoding == 2) decoder = Encoding.BigEndianUnicode;
            else if (encoding == 3) decoder = Encoding.UTF8;
            else return "";
            string value = decoder.GetString(data, start, length);
            return value.Trim('\0', '\uFEFF');
        }

        static bool TryDecodeUserText(byte[] data, int offset, int count, out string description, out string value)
        {
            description = value = "";
            if (count <= 2) return false;
            string combined = DecodeText(data, offset, count);
            int separator = combined.IndexOf('\0');
            if (separator < 0) return false;
            description = combined.Substring(0, separator).Trim('\uFEFF');
            value = combined.Substring(separator + 1).Trim('\0', '\uFEFF');
            return description.Length > 0;
        }

        static bool IsFrameId(string id)
        {
            if (id == null || id.Length != 4) return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i]; if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false;
            }
            return true;
        }

        static bool IsPadding(byte[] data, int offset, int end)
        {
            for (int i = offset; i < end; i++) if (data[i] != 0) return false;
            return true;
        }

        static int ReadBigEndian(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return -1;
            long value = ((long)data[offset] << 24) | ((long)data[offset + 1] << 16) | ((long)data[offset + 2] << 8) | data[offset + 3];
            return value > Int32.MaxValue ? -1 : (int)value;
        }

        static int ReadSynchsafe(byte[] data, int offset)
        {
            if (offset + 4 > data.Length || (data[offset] | data[offset + 1] | data[offset + 2] | data[offset + 3]) >= 0x80) return -1;
            return (data[offset] << 21) | (data[offset + 1] << 14) | (data[offset + 2] << 7) | data[offset + 3];
        }

        static void WriteBigEndian(byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value >> 24); data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8); data[offset + 3] = (byte)value;
        }

        static void WriteSynchsafe(byte[] data, int offset, int value)
        {
            if (value < 0 || value > 0x0FFFFFFF) throw new IOException("Слишком большой ID3-тег");
            data[offset] = (byte)((value >> 21) & 0x7F); data[offset + 1] = (byte)((value >> 14) & 0x7F);
            data[offset + 2] = (byte)((value >> 7) & 0x7F); data[offset + 3] = (byte)(value & 0x7F);
        }

        static byte[] CopyRange(byte[] data, int offset, int count)
        {
            byte[] copy = new byte[count]; Buffer.BlockCopy(data, offset, copy, 0, count); return copy;
        }

        static void ReadFully(Stream stream, byte[] data, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(data, offset, count);
                if (read <= 0) throw new EndOfStreamException();
                offset += read; count -= read;
            }
        }

        static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[64 * 1024]; int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, read);
        }
    }

    public sealed class AudioPlayer : IDisposable
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
        static extern int Send(string command, StringBuilder result, int length, IntPtr callback);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciGetErrorStringW")]
        static extern bool Error(int code, StringBuilder result, int length);

        bool opened;

        string Command(string command)
        {
            StringBuilder result = new StringBuilder(256);
            int code = Send(command, result, result.Capacity, IntPtr.Zero);
            if (code != 0)
            {
                StringBuilder error = new StringBuilder(256);
                Error(code, error, error.Capacity);
                throw new IOException("MP3-плеер: " + (error.Length == 0 ? code.ToString() : error.ToString()));
            }
            return result.ToString();
        }

        public string Mode { get { return opened ? Command("status oldym_audio mode") : "closed"; } }
        public int Position
        {
            get
            {
                int value;
                return opened && Int32.TryParse(Command("status oldym_audio position"), out value) ? value : 0;
            }
        }
        public int Length
        {
            get
            {
                int value;
                return opened && Int32.TryParse(Command("status oldym_audio length"), out value) ? value : 0;
            }
        }

        public void Open(string path)
        {
            Stop();
            if (path.IndexOf('"') >= 0) throw new IOException("Неверный путь аудио");
            Command("open \"" + Path.GetFullPath(path) + "\" type mpegvideo alias oldym_audio");
            opened = true;
            Command("set oldym_audio time format milliseconds");
            Command("play oldym_audio");
        }

        public void Toggle()
        {
            if (!opened) return;
            Command(Mode == "paused" ? "resume oldym_audio" : "pause oldym_audio");
        }

        public void Seek(int position)
        {
            if (!opened) return;
            bool paused = Mode == "paused";
            Command("seek oldym_audio to " + Math.Max(0, position));
            if (!paused) Command("play oldym_audio");
        }

        public void Stop()
        {
            if (!opened) return;
            try { Command("close oldym_audio"); }
            finally { opened = false; }
        }

        public void Dispose() { Stop(); }
    }
}
