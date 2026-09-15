using System;

namespace QuickPPPoE {
    public interface ILink {
        bool HasHandle {get;}
        uint Dial();
        uint Status(out uint state, out uint error);
        uint HangUp();
        void Forget();
    }
    public sealed class RasLink : ILink {
        private IntPtr handle;
        private readonly string pbk, user, password;
        public RasLink(string p, string u, string secret) {pbk=p;user=u;password=secret;}
        public bool HasHandle {get {return handle!=IntPtr.Zero;}}
        public uint Dial() {return NativeRas.Dial(pbk,user,password,out handle);}
        public uint Status(out uint state,out uint error) {return NativeRas.Status(handle,out state,out error);}
        public uint HangUp() {return NativeRas.RasHangUp(handle);}
        public void Forget() {handle=IntPtr.Zero;}
    }
    public enum Phase {Idle, Dialing, Connected, Releasing, Waiting}
    // Single-threaded state machine. All methods run on the WinForms UI thread.
    // A handle must become invalid before another attempt is allowed.
    public sealed class ConnectionController {
        private readonly ILink link;
        private long started, next, now;
        private bool wanted, warned;
        public Phase Phase {get;private set;}
        public bool AutoReconnect {get;set;}
        public int RetryMilliseconds {get;set;}
        public int Attempts {get;private set;}
        public long ConnectedAt {get;private set;}
        public event Action<string> Log;
        public ConnectionController(ILink l) {link=l;RetryMilliseconds=1000;AutoReconnect=true;}
        void Say(string text) {if(Log!=null) Log(text);}
        public void Start(long time) {if(Phase!=Phase.Idle) return;now=time;wanted=true;Begin();}
        void Begin() {
            Attempts++; started=now; Phase=Phase.Dialing;
            Say("开始拨号 · 第 " + Attempts + " 次尝试");
            uint result=link.Dial();
            if(result!=0) Fail(result,false);
        }
        static bool NeedsInput(uint error) {
            return error==691 || error==646 || error==647 || error==648 || error==649 || error==709 || error==623 || error==703;
        }
        void Fail(uint error,bool dropped) {
            Say(error==0 ? "连接已断开。" : NativeRas.Error(error));
            if(NeedsInput(error)) {wanted=false;Say("已暂停重试，请检查账号、密码或运营商限制后重新连接。");}
            if(!AutoReconnect) wanted=false;
            next=now+(dropped ? 0 : RetryMilliseconds);
            Release();
        }
        void Release() {
            if(link.HasHandle) {
                uint r=link.HangUp();
                if(r==6 || r==668) {link.Forget(); FinishRelease();return;}
                if(r!=0) {wanted=false;Say("释放连接失败："+NativeRas.Error(r));}
                Phase=Phase.Releasing; started=now; warned=false;
            } else FinishRelease();
        }
        void FinishRelease() {
            Phase=wanted ? Phase.Waiting : Phase.Idle;
            if(wanted) Say("等待系统释放完成后的下一次重拨。");
            else Say("已停止。");
        }
        public void Stop(long time) {
            now=time;wanted=false;
            if(Phase==Phase.Releasing) return;
            if(Phase==Phase.Idle) return;
            Say("正在停止并断开连接…");Release();
        }
        public void Tick(long time) {
            now=time;
            if(Phase==Phase.Idle) return;
            if(Phase==Phase.Waiting) {
                if(!AutoReconnect) {wanted=false;Phase=Phase.Idle;return;}
                if(wanted && now>=next) Begin();
                return;
            }
            uint state,error;
            uint result=link.Status(out state,out error);
            if(Phase==Phase.Releasing) {
                if(result==6) {link.Forget();FinishRelease();}
                else if(!warned && now-started>15000) {warned=true;Say("系统仍在释放连接，暂缓重拨以避免重复占用端口。");}
                return;
            }
            if(result!=0 || error!=0 || state==0x2001) {
                bool dropped=Phase==Phase.Connected;
                if(result==6) link.Forget();
                Fail(result==6 ? 0 : result!=0 ? result : error,dropped);return;
            }
            if(state==0x2000) {
                if(Phase!=Phase.Connected) {Phase=Phase.Connected;ConnectedAt=now;Say("已连接 · 自动监测已启用");}
            } else if(Phase==Phase.Dialing && now-started>=60000) {
                Say("本次拨号超过 60 秒，取消后重试。");Fail(638,false);
            }
        }
    }
}
