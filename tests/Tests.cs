using System;
using System.IO;
using System.Runtime.InteropServices;
using QuickPPPoE;

sealed class FakeLink : ILink {
    public bool HasHandle {get;private set;}
    public uint State=0, Error=0, Result=0, DialError=0;
    public int Dials, Hangups;
    public uint Dial() {Dials++;HasHandle=true;State=0;return DialError;}
    public uint Status(out uint s,out uint e) {s=State;e=Error;return Result;}
    public uint HangUp() {Hangups++;return 0;}
    public void Forget() {HasHandle=false;}
}
static class Tests {
    static int count;
    static void Assert(bool value,string name) {if(!value) throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);count++;}
    static ConnectionController New(FakeLink l) {return new ConnectionController(l);}
    static void StateTests() {
        var l=new FakeLink();var c=New(l);c.Start(0);Assert(l.Dials==1,"initial attempt");
        l.State=0x2000;c.Tick(250);Assert(c.Phase==Phase.Connected,"connected");
        l.State=0x2001;c.Tick(500);Assert(l.Hangups==1 && c.Phase==Phase.Releasing,"drop releases handle");
        c.Tick(750);Assert(l.Dials==1,"no redial while handle is valid");
        l.Result=6;c.Tick(1000);l.Result=0;c.Tick(1250);Assert(l.Dials==2,"drop redials after release");
        c.Stop(1300);l.Result=6;c.Tick(1500);c.Tick(9000);Assert(c.Phase==Phase.Idle && l.Dials==2,"manual stop suppresses retry");
        l=new FakeLink {DialError=691};c=New(l);c.Start(0);l.Result=6;c.Tick(250);c.Tick(5000);Assert(l.Dials==1 && c.Phase==Phase.Idle,"authentication failure pauses");
        l=new FakeLink {DialError=651};c=New(l);c.Start(0);l.Result=6;c.Tick(250);c.Tick(999);Assert(l.Dials==1,"transient error obeys retry interval");
        l.Result=0;l.DialError=0;c.Tick(1000);Assert(l.Dials==2,"transient error retries");
        c.Stop(1100);Assert(c.Phase==Phase.Releasing,"cancel in-flight dial");
        l=new FakeLink();c=New(l);c.AutoReconnect=false;c.Start(0);l.State=0x2000;c.Tick(250);l.Result=6;c.Tick(500);c.Tick(9999);Assert(l.Dials==1 && c.Phase==Phase.Idle,"auto reconnect off");
        l=new FakeLink();c=New(l);c.Start(0);c.Tick(59999);Assert(l.Hangups==0,"no premature dial timeout");c.Tick(60000);Assert(l.Hangups==1,"60 second dial timeout cancels");
        l=new FakeLink {DialError=651};c=New(l);c.Start(0);l.Result=6;c.Tick(250);c.Stop(300);c.Tick(9000);Assert(l.Dials==1,"stop during retry wait");
        l=new FakeLink {DialError=651};c=New(l);c.Start(0);l.Result=6;c.Tick(250);c.AutoReconnect=false;c.Tick(1000);Assert(l.Dials==1 && c.Phase==Phase.Idle,"disable reconnect while waiting");
        l=new FakeLink();c=New(l);c.Start(0);l.State=0x2000;c.Tick(250);l.Result=6;c.Tick(500);l.Result=0;c.Tick(750);Assert(l.Dials==2,"invalidated connection redials within one extra tick");
        l=new FakeLink();c=New(l);c.Start(0);c.Stop(50);c.Tick(16000);Assert(l.Dials==1 && c.Phase==Phase.Releasing,"slow release never overlaps another dial");
    }
    static int Main(string[] args) {
        try {
            StateTests();
            var settings=new Settings {Remember=true};
            try {settings.SetPassword("test-密码-&$\"123");Assert(settings.Secret.IndexOf("test-")<0 && settings.Password()=="test-密码-&$\"123","DPAPI round trip");}
            catch(System.Security.Cryptography.CryptographicException e) {Console.WriteLine("SKIP: DPAPI unavailable in this user profile: "+e.Message);}
            settings.Remember=false;settings.SetPassword("secret");Assert(settings.Secret=="","forget password clears ciphertext");
            if(args.Length>0) {
                string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);
                string pbk=Path.Combine(folder,"native-test-"+Guid.NewGuid().ToString("N")+".pbk");
                Console.WriteLine("Native struct sizes: entry="+Marshal.SizeOf(typeof(RasEntry))+", dial="+Marshal.SizeOf(typeof(DialParams))+", status="+Marshal.SizeOf(typeof(RasStatus)));
                Console.WriteLine("Device: "+NativeRas.FindDevice());
                foreach(string service in new[]{"", "isp-service", "测试-service",new string('a',128)}) {
                    NativeRas.SaveEntry(pbk,service);
                    var e=NativeRas.MakeEntry("");uint bytes=e.size;
                    NativeRas.Check(NativeRas.RasGetEntryProperties(pbk,NativeRas.EntryName,ref e,ref bytes,IntPtr.Zero,IntPtr.Zero));
                    Console.WriteLine("Read-back type="+e.type+", device="+e.deviceType+", service="+e.phone);
                    Assert(e.phone==service && string.Equals(e.deviceType,"PPPoE",StringComparison.OrdinalIgnoreCase) && e.type==5,"native service-name round trip (length="+service.Length+")");
                }
                uint state,error;Assert(NativeRas.Status(new IntPtr(1234567),out state,out error)==6,"native status accepts structure and rejects invalid handle");
                uint hangup=NativeRas.RasHangUp(new IntPtr(1234567));
                Assert(hangup==6 || hangup==668,"native hangup rejects nonexistent connection ("+hangup+")");
                IntPtr handle;
                uint result=NativeRas.Dial(Path.Combine(folder,"nonexistent-"+Guid.NewGuid().ToString("N")+".pbk"),"test","test",out handle);
                if(handle!=IntPtr.Zero) NativeRas.RasHangUp(handle);
                Assert(result==623 || result==621,"native dial parameters accepted; missing phonebook rejected ("+result+")");
                Console.WriteLine("Test phonebook: "+pbk);
            }
            Console.WriteLine("ALL "+count+" CHECKS PASSED. No real connection attempted.");return 0;
        } catch(Exception e) {Console.Error.WriteLine(e);return 1;}
    }
}
