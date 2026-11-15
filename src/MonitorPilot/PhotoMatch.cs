using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MonitorPilot;

// Linear-light average of one screen in the photo. Code* are the gamma-encoded means for exposure checks.
internal readonly record struct PatchSample(double R,double G,double B,double CodeMax,double CodeMean)
{
    public double Y=>0.2126*R+0.7152*G+0.0722*B;
    public bool Clipped=>CodeMax>=250;
    public bool Dark=>CodeMean<12;
}
// A lit screen found in the photo: center in normalized photo coordinates, bounding-box width/height.
internal readonly record struct ScreenSpot(double X,double Y,double Aspect,int Area);
internal sealed class MatchPlan
{
    public string Key {get;init;}="";
    public bool Reference {get;init;}
    public double RelativeBrightness {get;init;}
    public double Cooler {get;init;}
    public double Greener {get;init;}
    public int? Brightness {get;init;}
    public int? Red {get;init;}
    public int? Green {get;init;}
    public int? Blue {get;init;}
    public int Dimming {get;init;}
    public bool TooDim {get;init;}
    // Phone photos scatter by a few percent between shots, so tighter limits would never be reported as reached.
    public bool Matched=>Math.Abs(RelativeBrightness-1)<0.07&&Math.Abs(Cooler)<4&&Math.Abs(Greener)<4;
}
internal static class PhotoMatch
{
    // Light gray keeps phone photos away from clipping while still showing white point and brightness.
    public const byte PatchLevel=176;
    public const double SampleRadius=0.025;
    // Backlight luminance is modelled as proportional to (percent + offset): monitors still emit light at 0 %.
    public const double BrightnessOffset=25;
    // Phone cameras tone-map screens differently per shot, so one round never moves a control further than this.
    public const int MaxBrightnessStep=12;
    public const int MaxColorStep=10;
    private static readonly double[] Linear=Enumerable.Range(0,256).Select(i=>{double v=i/255.0;return v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4);}).ToArray();
    public static BitmapSource Load(byte[] data,int maxSide=2000)
    {
        using var stream=new MemoryStream(data);
        var frame=BitmapDecoder.Create(stream,BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnLoad).Frames[0];
        ushort orientation=1;
        foreach(var query in new[]{"System.Photo.Orientation","/app1/ifd/{ushort=274}"})
            try{if(frame.Metadata is BitmapMetadata meta&&meta.GetQuery(query) is ushort value){orientation=value;break;}}catch{}
        return Normalize(frame,maxSide,orientation switch{3=>180,6=>90,8=>270,_=>0});
    }
    public static BitmapSource Normalize(BitmapSource source,int maxSide=2000,double angle=0)
    {
        double scale=Math.Min(1,maxSide/(double)Math.Max(source.PixelWidth,source.PixelHeight));
        var transform=new TransformGroup();transform.Children.Add(new ScaleTransform(scale,scale));if(angle!=0)transform.Children.Add(new RotateTransform(angle));
        var result=new WriteableBitmap(new FormatConvertedBitmap(new TransformedBitmap(source,transform),PixelFormats.Bgra32,null,0));result.Freeze();return result;
    }
    // x/y are normalized photo coordinates. The brightest and darkest 15 % are dropped to ignore reflections and bezels.
    public static PatchSample Sample(BitmapSource image,double x,double y,double radius=SampleRadius)
    {
        int w=image.PixelWidth,h=image.PixelHeight,r=Math.Max(2,(int)(radius*Math.Min(w,h)));
        int cx=(int)Math.Round(x*(w-1)),cy=(int)Math.Round(y*(h-1));
        int x0=Math.Clamp(cx-r,0,w-1),y0=Math.Clamp(cy-r,0,h-1),x1=Math.Clamp(cx+r,0,w-1),y1=Math.Clamp(cy+r,0,h-1);
        int rw=x1-x0+1,rh=y1-y0+1;var pixels=new byte[rw*rh*4];image.CopyPixels(new Int32Rect(x0,y0,rw,rh),pixels,rw*4,0);
        var values=new List<(byte R,byte G,byte B,double Y)>(rw*rh);
        for(int i=0;i<pixels.Length;i+=4){byte b=pixels[i],g=pixels[i+1],red=pixels[i+2];values.Add((red,g,b,0.2126*Linear[red]+0.7152*Linear[g]+0.0722*Linear[b]));}
        values.Sort((a,b)=>a.Y.CompareTo(b.Y));
        int trim=values.Count*15/100;var kept=values.Skip(trim).Take(Math.Max(1,values.Count-2*trim)).ToList();
        double codeR=kept.Average(v=>v.R),codeG=kept.Average(v=>v.G),codeB=kept.Average(v=>v.B);
        return new(kept.Average(v=>Linear[v.R]),kept.Average(v=>Linear[v.G]),kept.Average(v=>Linear[v.B]),Math.Max(codeR,Math.Max(codeG,codeB)),(codeR+codeG+codeB)/3);
    }
    public static double Cooler(PatchSample s,PatchSample reference)=>((s.B/s.R)/(reference.B/reference.R)-1)*100;
    public static double Greener(PatchSample s,PatchSample reference)=>((s.G/((s.R+s.B)/2))/(reference.G/((reference.R+reference.B)/2))-1)*100;
    // Lit screens in a photo of the gray test image, largest first. One erosion step splits screens that only touch
    // through a thin bezel; regions under 0.4 % of the photo (status bars, LEDs, reflections) are ignored.
    public static List<ScreenSpot> FindScreens(BitmapSource image,int count,int side=480)
    {
        double scale=Math.Min(1,side/(double)Math.Max(image.PixelWidth,image.PixelHeight));
        var small=new FormatConvertedBitmap(scale<1?new TransformedBitmap(image,new ScaleTransform(scale,scale)):image,PixelFormats.Bgra32,null,0);
        int w=small.PixelWidth,h=small.PixelHeight;var px=new byte[w*h*4];small.CopyPixels(px,w*4,0);
        var lum=new double[w*h];for(int i=0;i<lum.Length;i++)lum[i]=0.0722*px[4*i]+0.7152*px[4*i+1]+0.2126*px[4*i+2];
        var sorted=(double[])lum.Clone();Array.Sort(sorted);double threshold=sorted[(int)(0.98*(sorted.Length-1))]*0.6;
        if(threshold<24)return new();
        bool Bright(int x,int y)=>x<0||y<0||x>=w||y>=h||lum[y*w+x]>threshold;
        var lit=new bool[w*h];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)lit[y*w+x]=Bright(x,y)&&Bright(x-1,y)&&Bright(x+1,y)&&Bright(x,y-1)&&Bright(x,y+1);
        var seen=new bool[w*h];var spots=new List<ScreenSpot>();var stack=new Stack<int>();
        for(int start=0;start<lit.Length;start++)
        {
            if(!lit[start]||seen[start])continue;
            int area=0,minX=w,maxX=0,minY=h,maxY=0;double sumX=0,sumY=0;seen[start]=true;stack.Push(start);
            while(stack.Count>0)
            {
                int i=stack.Pop(),x=i%w,y=i/w;area++;sumX+=x;sumY+=y;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
                foreach(int n in new[]{x>0?i-1:-1,x<w-1?i+1:-1,y>0?i-w:-1,y<h-1?i+w:-1})if(n>=0&&lit[n]&&!seen[n]){seen[n]=true;stack.Push(n);}
            }
            if(area>=0.004*w*h)spots.Add(new((sumX/area+0.5)/w,(sumY/area+0.5)/h,(maxX-minX+1)/(double)(maxY-minY+1),area));
        }
        return spots.OrderByDescending(s=>s.Area).Take(count).ToList();
    }
    // Pairs screens in the photo with displays through their Windows arrangement, which normally mirrors the desk.
    // Both layouts are normalized per axis; aspect ratio separates a portrait display from its neighbours.
    public static Dictionary<string,Point>? Assign(IReadOnlyList<ScreenSpot> spots,IReadOnlyList<(string Key,double X,double Y,double Aspect)> displays)
    {
        int n=displays.Count;if(n==0||n>8||spots.Count!=n)return null;
        static double[] Normalize(IEnumerable<double> values){var a=values.ToArray();double mean=a.Average(),sd=Math.Sqrt(a.Average(v=>(v-mean)*(v-mean)));return a.Select(v=>sd<1e-9?0:(v-mean)/sd).ToArray();}
        double[] sx=Normalize(spots.Select(s=>s.X)),sy=Normalize(spots.Select(s=>s.Y)),dx=Normalize(displays.Select(d=>d.X)),dy=Normalize(displays.Select(d=>d.Y));
        var cost=new double[n,n];
        for(int d=0;d<n;d++)for(int s=0;s<n;s++)cost[d,s]=Math.Pow(dx[d]-sx[s],2)+Math.Pow(dy[d]-sy[s],2)+0.5*Math.Abs(Math.Log(displays[d].Aspect/spots[s].Aspect));
        int[] best=new int[n],pick=new int[n];double bestCost=double.MaxValue;var used=new bool[n];
        void Search(int d,double total)
        {
            if(total>=bestCost)return;
            if(d==n){bestCost=total;Array.Copy(pick,best,n);return;}
            for(int s=0;s<n;s++)if(!used[s]){used[s]=true;pick[d]=s;Search(d+1,total+cost[d,s]);used[s]=false;}
        }
        Search(0,0);
        return Enumerable.Range(0,n).ToDictionary(d=>displays[d].Key,d=>new Point(spots[best[d]].X,spots[best[d]].Y));
    }
    // Levels that keep a saved match when the reference moves to a new brightness. Light output follows the same
    // (percent + offset) model as the match itself; below 0 % the rest comes from software dimming.
    public static Dictionary<string,(int Brightness,int Dimming)> Follow(MatchCalibration match,int reference)
    {
        var anchor=match.Levels[match.Reference];double scale=(reference+BrightnessOffset)/(anchor.Brightness+BrightnessOffset);
        return match.Levels.ToDictionary(pair=>pair.Key,pair=>
        {
            double light=(pair.Value.Brightness+BrightnessOffset)*(1-pair.Value.Dimming/100.0)*scale;
            return light>=BrightnessOffset?((int)Math.Round(Math.Min(100,light-BrightnessOffset)),0):(0,(int)Math.Round(Math.Clamp((1-light/BrightnessOffset)*100,0,80)));
        },StringComparer.OrdinalIgnoreCase);
    }
    // The display whose white point is closest to all others; an outlier as reference drags every screen towards it.
    public static string? Central(IReadOnlyDictionary<string,PatchSample> samples)=>
        samples.Count<2?null:samples.MinBy(c=>samples.Values.Sum(s=>Math.Abs(Cooler(s,c.Value))+Math.Abs(Greener(s,c.Value)))).Key;
    // One damped correction step. Gains only go down so no channel clips; brightness follows the offset model and
    // falls back to software dimming once the backlight is at 0 %.
    public static MatchPlan Plan(string key,PatchSample s,PatchSample reference,int? brightness,int? red,int? green,int? blue,int dimming,bool isReference,bool colors=true)
    {
        double relative=s.Y/reference.Y,cooler=Cooler(s,reference),greener=Greener(s,reference);
        if(isReference)return new(){Key=key,Reference=true,RelativeBrightness=1,Brightness=brightness,Red=red,Green=green,Blue=blue,Dimming=dimming};
        double mr=1,mg=1,mb=1;int? newRed=red,newGreen=green,newBlue=blue;
        if(colors&&red is int r&&green is int g&&blue is int b&&r>0&&g>0&&b>0)
        {
            mr=Math.Pow((reference.R/reference.G)/(s.R/s.G),0.85);mb=Math.Pow((reference.B/reference.G)/(s.B/s.G),0.85);
            double k=Math.Max(mg,Math.Max(mr,mb));mr/=k;mg/=k;mb/=k;
            int Step(int now,double factor)=>Math.Max(now-MaxColorStep,(int)Math.Round(Math.Clamp(now*factor,0,100)));
            newRed=Step(r,mr);newGreen=Step(g,mg);newBlue=Step(b,mb);
            mr=newRed.Value/(double)r;mg=newGreen.Value/(double)g;mb=newBlue.Value/(double)b;
        }
        double undimmed=(0.2126*s.R*mr+0.7152*s.G*mg+0.0722*s.B*mb)/Math.Max(0.2,1-dimming/100.0);
        double ratio=reference.Y/undimmed;int? newBrightness=brightness;double dim=0;bool tooDim=false;
        if(brightness is int current)
        {
            double target=(current+BrightnessOffset)*Math.Pow(ratio,0.9)-BrightnessOffset;
            if(target<0)dim=1-reference.Y/(undimmed*BrightnessOffset/(current+BrightnessOffset));
            tooDim=target>100.5;newBrightness=(int)Math.Round(Math.Clamp(target,Math.Max(0,current-MaxBrightnessStep),Math.Min(100,current+MaxBrightnessStep)));
            if(newBrightness>0)dim=0;
        }
        else{if(ratio<1)dim=1-ratio;else tooDim=ratio>1.05;}
        return new(){Key=key,RelativeBrightness=relative,Cooler=cooler,Greener=greener,Brightness=newBrightness,Red=newRed,Green=newGreen,Blue=newBlue,Dimming=(int)Math.Round(Math.Clamp(dim*100,0,80)),TooDim=tooDim};
    }
}

// Minimal LAN page so the phone can send the photo straight to this PC. It only runs while the match window is
// open and only answers on a random path.
internal sealed class PhotoUploadServer : IDisposable
{
    private readonly TcpListener? listener;
    private readonly CancellationTokenSource stop=new();
    // Kept across sessions so the phone can bookmark the page; a random path keeps other LAN devices out.
    public string Token {get;}=StoredToken();
    private static string StoredToken()
    {
        try
        {
            var path=Store.FilePath("upload-token.txt");
            if(File.Exists(path)&&File.ReadAllText(path).Trim() is {Length:8} saved&&saved.All(char.IsAsciiLetterOrDigit))return saved;
            var token=Guid.NewGuid().ToString("N")[..8];Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);File.WriteAllText(path,token);return token;
        }
        catch{return Guid.NewGuid().ToString("N")[..8];}
    }
    public int Port {get;}
    // Handles an uploaded photo; the returned text is shown on the phone.
    public Func<byte[],Task<string>>? Received {get;set;}
    public PhotoUploadServer(IPAddress? bind=null,int[]? ports=null)
    {
        foreach(int port in ports??new[]{8765,8766,8767,0})
        {
            try{listener=new TcpListener(bind??IPAddress.Any,port);listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;break;}
            catch(SocketException){listener=null;}
        }
        if(listener!=null)_=Accept();
    }
    public bool Running=>listener!=null;
    public IReadOnlyList<string> Urls=>Addresses().Select(a=>$"http://{a}:{Port}/{Token}").ToList();
    // Only adapters with a default gateway (the home network) plus Tailscale; host-only, WSL and Hyper-V switches
    // have no gateway and are unreachable from a phone.
    internal static IEnumerable<string> Addresses()
    {
        var lan=new List<string>();var tailscale=new List<string>();
        try
        {
            foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback))
            {
                var props=nic.GetIPProperties();bool gateway=props.GatewayAddresses.Any(g=>g.Address.AddressFamily==AddressFamily.InterNetwork&&!g.Address.Equals(IPAddress.Any));
                foreach(var address in props.UnicastAddresses.Select(a=>a.Address).Where(a=>a.AddressFamily==AddressFamily.InterNetwork))
                {
                    var bytes=address.GetAddressBytes();
                    if(bytes[0]==100&&bytes[1]>=64&&bytes[1]<=127)tailscale.Add(address.ToString());
                    else if(gateway&&!(bytes[0]==169&&bytes[1]==254))lan.Add(address.ToString());
                }
            }
        }
        catch{}
        // VPN adapters (often 10.x) also carry a gateway; home routers mostly hand out 192.168.x, so those come first.
        return lan.OrderBy(a=>a.StartsWith("192.168.")?0:a.StartsWith("172.")?1:2).Concat(tailscale).Distinct();
    }
    private async Task Accept()
    {
        while(!stop.IsCancellationRequested)
        {
            TcpClient client;
            try{client=await listener!.AcceptTcpClientAsync(stop.Token);}catch{break;}
            _=Task.Run(()=>Handle(client));
        }
    }
    private async Task Handle(TcpClient client)
    {
        using(client)
        try
        {
            var stream=client.GetStream();stream.ReadTimeout=30000;
            var buffer=new MemoryStream();var chunk=new byte[16384];int headerEnd=-1;
            while(headerEnd<0&&buffer.Length<32768)
            {
                int read=await stream.ReadAsync(chunk,stop.Token);if(read==0)return;buffer.Write(chunk,0,read);
                headerEnd=IndexOf(buffer.GetBuffer(),(int)buffer.Length,"\r\n\r\n"u8);
            }
            if(headerEnd<0)return;
            var header=Encoding.ASCII.GetString(buffer.GetBuffer(),0,headerEnd).Split("\r\n");var request=header[0].Split(' ');
            if(request.Length<2)return;
            string method=request[0],path=request[1].Split('?')[0];
            long length=header.Skip(1).Select(h=>h.Split(':',2)).Where(p=>p.Length==2&&p[0].Trim().Equals("Content-Length",StringComparison.OrdinalIgnoreCase)).Select(p=>long.TryParse(p[1].Trim(),out var v)?v:0).FirstOrDefault();
            if(method=="GET"&&path=="/"+Token){await Respond(stream,"200 OK","text/html; charset=utf-8",Encoding.UTF8.GetBytes(Page()));return;}
            if(method=="POST"&&path=="/"+Token+"/upload"&&length>0&&length<=40_000_000)
            {
                var body=new byte[length];int have=(int)Math.Min(length,buffer.Length-(headerEnd+4));
                Array.Copy(buffer.GetBuffer(),headerEnd+4,body,0,have);
                while(have<length){int read=await stream.ReadAsync(body.AsMemory(have),stop.Token);if(read==0)return;have+=read;}
                string reply;try{reply=Received!=null?await Received(body):"ok";}catch(Exception ex){reply=ex.Message;}
                await Respond(stream,"200 OK","text/plain; charset=utf-8",Encoding.UTF8.GetBytes(reply));return;
            }
            await Respond(stream,"404 Not Found","text/plain","not found"u8.ToArray());
        }
        catch{}
    }
    private static int IndexOf(byte[] data,int length,ReadOnlySpan<byte> value)=>data.AsSpan(0,length).IndexOf(value);
    private static async Task Respond(NetworkStream stream,string status,string type,byte[] body)
    {
        var head=Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head);await stream.WriteAsync(body);
    }
    private string Page()=>$$"""
<!doctype html><html lang="{{L.Language}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>MonitorPilot</title><style>
:root{color-scheme:light dark;--bg:#fff;--fg:#0a0a0a;--muted:#737373;--border:#e5e5e5;--primary:#171717;--primary-fg:#fafafa}
@media (prefers-color-scheme:dark){:root{--bg:#0a0a0a;--fg:#fafafa;--muted:#a1a1a1;--border:#2e2e2e;--primary:#e5e5e5;--primary-fg:#171717} }
body{margin:0;font-family:system-ui,sans-serif;background:var(--bg);color:var(--fg);padding:24px 16px}
h1{font-size:22px;margin:0 0 6px}p{color:var(--muted);font-size:15px;line-height:1.5;margin:0 0 18px}
label{display:block;text-align:center;background:var(--primary);color:var(--primary-fg);border-radius:10px;padding:16px;font-size:17px;font-weight:600}
input{display:none}#s{margin-top:18px;font-size:17px;font-weight:600;line-height:1.45;text-align:center}
</style></head><body>
<h1>{{Html(L.T("Monitore angleichen"))}}</h1>
<p>{{Html(L.T("Ein Foto, auf dem alle Bildschirme mit dem grauen Testbild zu sehen sind. Gerade davor stehen, Blitz aus. Wenn möglich die Belichtung festhalten."))}}</p>
<label for="f">{{Html(L.T("Foto aufnehmen"))}}</label><input id="f" type="file" accept="image/*" capture="environment">
<div id="s"></div>
<script>
const f=document.getElementById('f'),s=document.getElementById('s');
f.onchange=async()=>{const file=f.files[0];if(!file)return;s.textContent={{System.Text.Json.JsonSerializer.Serialize(L.T("Wird gesendet und angewendet …"))}};
try{const r=await fetch(location.pathname+'/upload',{method:'POST',body:file});s.textContent=r.ok?await r.text():{{System.Text.Json.JsonSerializer.Serialize(L.T("Senden fehlgeschlagen."))}};}
catch(e){s.textContent={{System.Text.Json.JsonSerializer.Serialize(L.T("Senden fehlgeschlagen."))}};}f.value='';};
</script></body></html>
""";
    private static string Html(string text)=>WebUtility.HtmlEncode(text);
    public void Dispose(){stop.Cancel();try{listener?.Stop();}catch{}}
}
