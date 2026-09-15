using System;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickPPPoE {
    // ras.h uses 4-byte packing, including on x64.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    public struct RasEntry {
        public uint size, options, countryId, countryCode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=11)] public string area;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=129)] public string phone;
        public uint alternate, ip, dns, dnsAlt, wins, winsAlt, frameSize, protocols, framing;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string script;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string autodialDll;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string autodialFunc;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=17)] public string deviceType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=129)] public string deviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=33)] public string pad;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=201)] public string address;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=201)] public string facilities;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=201)] public string userData;
        public uint channels, reserved1, reserved2, subEntries, dialMode, dialExtraPercent, dialExtraSeconds, hangupPercent, hangupSeconds, idle, type, encryption, authKey;
        public Guid id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string customDialDll;
        public uint vpnStrategy, options2, options3;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string dnsSuffix;
        public uint tcpWindow;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string prerequisitePbk;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string prerequisiteEntry;
        public uint redialCount, redialPause;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=16)] public byte[] ipv6Dns;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=16)] public byte[] ipv6DnsAlt;
        public uint ipv4Metric, ipv6Metric;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=16)] public byte[] ipv6;
        public uint ipv6Prefix, outage;
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode, Pack=4)]
    public struct DialParams {
        public uint size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string entry;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=129)] public string phone;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=129)] public string callback;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string user;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=257)] public string password;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=16)] public string domain;
        public uint subEntry;
        public UIntPtr callbackId;
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode, Pack=4)]
    public struct RasStatus {
        public uint size, state, error;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=17)] public string deviceType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=129)] public string deviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=129)] public string phone;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=40)] public byte[] endpoints;
        public uint substate;
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode, Pack=4)]
    public struct RasDevice {
        public uint size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=17)] public string type;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=129)] public string name;
    }
    public static class NativeRas {
        private const uint RASNP_Ip = 0x00000004;
        private const uint RASNP_Ipv6 = 0x00000008;
        private const uint RASEO2_IPv6RemoteDefaultGateway = 0x00002000;
        public const string EntryName = "QuickPPPoE";
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate void DialCallback(uint message, uint state, uint error);
        // Keep the callback alive for the complete process lifetime. Status is read on the UI timer.
        private static readonly DialCallback callback = delegate(uint m, uint s, uint e) {};
        [DllImport("rasapi32.dll", CharSet=CharSet.Unicode)]
        public static extern uint RasGetEntryProperties(string pbk, string name, ref RasEntry entry, ref uint bytes, IntPtr device, IntPtr deviceBytes);
        [DllImport("rasapi32.dll", CharSet=CharSet.Unicode)]
        public static extern uint RasSetEntryProperties(string pbk, string name, ref RasEntry entry, uint bytes, IntPtr device, uint deviceBytes);
        [DllImport("rasapi32.dll", CharSet=CharSet.Unicode)]
        public static extern uint RasEnumDevices(IntPtr devices, ref uint bytes, out uint count);
        [DllImport("rasapi32.dll", CharSet=CharSet.Unicode)]
        private static extern uint RasDial(IntPtr extensions, string pbk, ref DialParams parameters, uint notifierType, DialCallback notifier, out IntPtr handle);
        [DllImport("rasapi32.dll", CharSet=CharSet.Unicode)]
        private static extern uint RasGetConnectStatus(IntPtr handle, ref RasStatus status);
        [DllImport("rasapi32.dll", CharSet=CharSet.Unicode)]
        public static extern uint RasHangUp(IntPtr handle);
        [DllImport("rasapi32.dll", CharSet=CharSet.Unicode)]
        private static extern uint RasGetErrorString(uint error, StringBuilder text, uint length);
        public static string Error(uint code) {
            var text = new StringBuilder(1024);
            return "错误 " + code + "：" + (RasGetErrorString(code, text, 1024)==0 ? text.ToString() : new System.ComponentModel.Win32Exception((int)code).Message);
        }
        public static void Check(uint code) { if(code!=0) throw new InvalidOperationException(Error(code)); }
        public static string FindDevice() {
            uint bytes=0, count;
            uint result=RasEnumDevices(IntPtr.Zero, ref bytes, out count);
            if(result!=603 && result!=0) Check(result);
            if(bytes==0) throw new InvalidOperationException("未找到 Windows 拨号设备。");
            IntPtr buffer=Marshal.AllocHGlobal((int)bytes);
            try {
                Marshal.WriteInt32(buffer, Marshal.SizeOf(typeof(RasDevice)));
                Check(RasEnumDevices(buffer, ref bytes, out count));
                for(int i=0; i<count; i++) {
                    var device=(RasDevice)Marshal.PtrToStructure(IntPtr.Add(buffer,i*Marshal.SizeOf(typeof(RasDevice))), typeof(RasDevice));
                    if(string.Equals(device.type,"PPPoE",StringComparison.OrdinalIgnoreCase)) return device.name;
                }
                throw new InvalidOperationException("未找到 WAN Miniport (PPPOE)，请检查系统网络组件。");
            } finally { Marshal.FreeHGlobal(buffer); }
        }
        public static RasEntry MakeEntry(string service) {
            if(service==null || service.Length>128 || service.IndexOf('\0')>=0) throw new ArgumentException("Service-name 最多 128 个字符。");
            var entry=new RasEntry();
            entry.size=(uint)Marshal.SizeOf(typeof(RasEntry));
            entry.ipv6=new byte[16]; entry.ipv6Dns=new byte[16]; entry.ipv6DnsAlt=new byte[16];
            entry.options=0x10 | 0x10000; // Default gateway; disable file/print sharing.
            entry.options2=3 | RASEO2_IPv6RemoteDefaultGateway;
            // Offer both NCPs. Windows PPP can keep a connection with only one
            // successfully negotiated IP protocol; IPv6 availability is not a
            // condition for success, and is never used to trigger redial.
            // Leave specific-address/DNS flags clear for automatic configuration.
            entry.protocols=RASNP_Ip | RASNP_Ipv6;
            entry.framing=1; entry.type=5; entry.encryption=0;
            entry.deviceType="PPPoE"; entry.deviceName=FindDevice();
            entry.phone=service; // PPPoE service-name, per Microsoft RASENTRY documentation.
            return entry;
        }
        public static void SaveEntry(string pbk, string service) {
            var entry=MakeEntry(service);
            Check(RasSetEntryProperties(pbk,EntryName,ref entry,entry.size,IntPtr.Zero,0));
        }
        public static uint Dial(string pbk, string user, string password, out IntPtr handle) {
            var p=new DialParams {size=(uint)Marshal.SizeOf(typeof(DialParams)), entry=EntryName, user=user,password=password,domain="",phone="",callback=""};
            return RasDial(IntPtr.Zero,pbk,ref p,0,callback,out handle);
        }
        public static uint Status(IntPtr handle, out uint state, out uint error) {
            var s=new RasStatus {size=(uint)Marshal.SizeOf(typeof(RasStatus)),endpoints=new byte[40]};
            uint result=RasGetConnectStatus(handle,ref s); state=s.state; error=s.error; return result;
        }
    }
}
