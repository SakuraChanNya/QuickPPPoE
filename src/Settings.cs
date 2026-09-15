using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace QuickPPPoE {
    public sealed class Settings {
        public string User="", Service="", Secret="";
        public bool Remember=false, AutoReconnect=true;
        public int RetrySeconds=1;
        public static string DirectoryPath {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"QuickPPPoE");}}
        public static string FilePath {get {return Path.Combine(DirectoryPath,"settings.xml");}}
        public static string Phonebook {get {return Path.Combine(DirectoryPath,"connections.pbk");}}
        public string Password() {
            if(!Remember || string.IsNullOrEmpty(Secret)) return "";
            byte[] bytes=ProtectedData.Unprotect(Convert.FromBase64String(Secret),null,DataProtectionScope.CurrentUser);
            try {return Encoding.UTF8.GetString(bytes);} finally {Array.Clear(bytes,0,bytes.Length);}
        }
        public void SetPassword(string password) {
            Secret=""; if(!Remember) return;
            byte[] bytes=Encoding.UTF8.GetBytes(password);
            try {Secret=Convert.ToBase64String(ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser));}
            finally {Array.Clear(bytes,0,bytes.Length);}
        }
        public static Settings Load() {
            if(!File.Exists(FilePath)) return new Settings();
            using(var stream=File.OpenRead(FilePath)) return (Settings)new XmlSerializer(typeof(Settings)).Deserialize(stream);
        }
        public void Save() {
            Directory.CreateDirectory(DirectoryPath);
            string temp=FilePath+".tmp";
            using(var stream=File.Create(temp)) new XmlSerializer(typeof(Settings)).Serialize(stream,this);
            if(File.Exists(FilePath)) File.Replace(temp,FilePath,null); else File.Move(temp,FilePath);
        }
    }
}
